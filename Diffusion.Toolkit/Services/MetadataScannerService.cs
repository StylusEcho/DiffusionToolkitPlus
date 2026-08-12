using Diffusion.ComfyUI;
using Diffusion.Common;
using Diffusion.IO;
using Diffusion.Toolkit.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Diffusion.Toolkit.Services;

public class ScanCompletionEvent
{
    public Action? OnMetadataCompleted { get; set; }
    public Action? OnDatabaseWriteCompleted { get; set; }
}

public class MetadataScannerService
{
    private Channel<FileScanJob> _channel;
    private Channel<FileScanJob> _queueChannel = Channel.CreateUnbounded<FileScanJob>();

    private CancellationTokenSource _cancellationTokenSource;

    private readonly int _degreeOfParallelism = 2;

    private Settings _settings => ServiceLocator.Settings!;


    public async Task QueueBatchAsync(IEnumerable<string> paths, ScanCompletionEvent scanCompletionEvent, CancellationToken cancellationToken)
    {
        var dt = ServiceLocator.DatabaseWriterService.StartAsync(cancellationToken);

        if (!dt.IsStarted)
        {
            dt.Task.ContinueWith(d =>
            {
                var message = new List<string>();
                if (d.Result.Added > 0)
                {
                    message.Add($"{d.Result.Added} images added");
                }
                if (d.Result.Updated > 0)
                {
                    message.Add($"{d.Result.Updated} images updated");
                }
                if (d.Result is { Added: 0, Updated: 0 })
                {
                    message.Add("No images were found");
                }

                var toast = string.Join("\r\n", message);

                ServiceLocator.ToastService.Toast(toast, "");
                scanCompletionEvent?.OnDatabaseWriteCompleted?.Invoke();
                ServiceLocator.ProgressService.CompleteTask();
                ServiceLocator.ProgressService.ClearProgress();
                ServiceLocator.ProgressService.SetStatus("");

                _ = ServiceLocator.FolderService.LoadFolders().ContinueWith(d =>
                {
                    if (ServiceLocator.Settings.AutoRefresh)
                    {
                        ServiceLocator.SearchService.RefreshResults();
                    }
                });


            });
        }

        var mt = StartAsync(cancellationToken);


        if (!mt.IsStarted)
        {
            mt.Task.ContinueWith(d =>
            {
                scanCompletionEvent?.OnMetadataCompleted?.Invoke();
                ServiceLocator.DatabaseWriterService.Complete();
            });
        }

        var i = 0;

        foreach (var path in paths)
        {
            await _channel.Writer.WriteAsync(new FileScanJob() { Path = path });

            i++;
            if (i % 33 == 0)
            {
                ServiceLocator.ProgressService.AddTotal(i);
                i = 0;
            }
        }

        ServiceLocator.ProgressService.AddTotal(i);


        _channel.Writer.Complete();
    }

    private bool _queueRunning;

    public void StartQueue(CancellationToken cancellationToken)
    {
        if (!_queueRunning)
        {
            Task.Run(async () => await ProcessQueueTaskAsync(cancellationToken));
            _queueRunning = true;
        }
    }

    public async Task QueueAsync(string path, CancellationToken cancellationToken)
    {
        ServiceLocator.DatabaseWriterService.StartQueueAsync(cancellationToken);
        StartQueue(cancellationToken);

        await _queueChannel.Writer.WriteAsync(new FileScanJob() { Path = path });
    }

    private bool _isStarted;
    private bool _isCompleted;
    private Task<int> _currentTask;

    public StartResult<int> StartAsync(CancellationToken token)
    {
        Debug.WriteLine("Entering Start");

        if (_isStarted && !_isCompleted)
        {
            return new StartResult<int>() { IsStarted = true, Task = _currentTask };
        }

        _isStarted = true;
        _isCompleted = false;

        ServiceLocator.ProgressService.ResetTotal();

        _cancellationTokenSource = new CancellationTokenSource();
        _channel = Channel.CreateUnbounded<FileScanJob>();

        CancellationTokenSource linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, token);

        var consumers = new List<Task<int>>();

        for (var i = 0; i < _degreeOfParallelism; i++)
        {
            consumers.Add(Task.Run(async () => await ProcessTaskAsync(linkedCts.Token)));
        }

        Debug.WriteLine($"Created {consumers.Count} consumers");

        _currentTask = Task.WhenAll(consumers).ContinueWith(t =>
        {
            _isCompleted = true;

            return consumers.Where(q => q.IsCompletedSuccessfully).Select(p => p.Result).Sum();
        });

        return new StartResult<int>() { Task = _currentTask };
    }

    public void Stop()
    {
        _cancellationTokenSource.Cancel();
        _channel.Writer.Complete();
    }

    public void Close()
    {
        _channel.Writer.Complete();
    }


    private async Task<int> ProcessTaskAsync(CancellationToken token)
    {
        Debug.WriteLine($"Entering ProcessTaskAsync");

        var count = 0;

        while (await _channel.Reader.WaitToReadAsync(token))
        {
            var job = await _channel.Reader.ReadAsync(token);

            try
            {
                if (File.Exists(job.Path))
                {
                    var fileParameters = Metadata.ReadFromFile(job.Path, new ComfyUIParser(ServiceLocator.NodePropertyCache));

                    count++;

                    var hashMatches = ServiceLocator.DataStore.GetImageIdByHash(fileParameters.Hash);

                    if (hashMatches.Any())
                    {
                        var moved = hashMatches.FirstOrDefault(d => !File.Exists(d.Path));

                        if (moved != null)
                        {
                            ServiceLocator.DataStore.UpdateImagePath(moved.Id, job.Path);

                            await ServiceLocator.DatabaseWriterService.QueueUpdateAsync(fileParameters, _settings.StoreMetadata, _settings.StoreWorkflow);
                            continue;
                        }
                    }

                    if (ServiceLocator.DataStore.ImageExists(job.Path))
                    {
                        await ServiceLocator.DatabaseWriterService.QueueUpdateAsync(fileParameters, _settings.StoreMetadata, _settings.StoreWorkflow);
                    }
                    else
                    {
                        await ServiceLocator.DatabaseWriterService.QueueAddAsync(fileParameters, _settings.StoreMetadata, _settings.StoreWorkflow);
                    }
                }
                else
                {
                    await ServiceLocator.DatabaseWriterService.QueueSkipAsync(new FileParameters()
                    {
                        Path = job.Path
                    });

                }

            }
            catch (Exception ex)
            {
                Logger.Log($"Error scanning {job.Path}:" + ex.Message);
            }
        }

        Debug.WriteLine($"Exiting Task... Count: {count}");

        return count;
    }



    /// <summary>
    /// How long to keep waiting for a file that is still being written before giving up on it.
    /// Long enough for a video encode to flush, short enough not to wedge the queue.
    /// </summary>
    private static readonly TimeSpan FileReadyTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan FileReadyPollInterval = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// Waits until a file has stopped growing and its writer has released it.
    /// </summary>
    /// <returns>
    /// False when the file vanished or never settled, in which case it should not be indexed.
    /// A file deleted while we waited is the normal case for intermediate render passes.
    /// </returns>
    private static async Task<bool> WaitForFileReady(string path, CancellationToken token)
    {
        var deadline = DateTime.UtcNow + FileReadyTimeout;
        long lastLength = -1;

        while (true)
        {
            if (token.IsCancellationRequested) return false;

            long length;

            try
            {
                var info = new FileInfo(path);

                if (!info.Exists) return false;

                length = info.Length;
            }
            catch (IOException)
            {
                length = -1;
            }

            // An exclusive open is the reliable signal that whatever produced the file is done
            if (length > 0 && length == lastLength && CanOpenExclusively(path))
            {
                return true;
            }

            lastLength = length;

            if (DateTime.UtcNow >= deadline)
            {
                Logger.Log($"Timed out waiting for {path} to finish being written, skipping it");
                return false;
            }

            try
            {
                await Task.Delay(FileReadyPollInterval, token);
            }
            catch (TaskCanceledException)
            {
                return false;
            }
        }
    }

    private static bool CanOpenExclusively(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // Read-only or permission denied is not the same as still being written
            return true;
        }
    }

    private async Task<int> ProcessQueueTaskAsync(CancellationToken token)
    {
        var count = 0;

        while (await _queueChannel.Reader.WaitToReadAsync(token))
        {
            var job = await _queueChannel.Reader.ReadAsync(token);

            try
            {
                // This queue is fed by the folder watchers, so a file may still be being written.
                // Indexing a half written video stores a broken thumbnail and preview that stick
                // around until the folder is rescanned, so wait for the writer to let go first.
                if (!await WaitForFileReady(job.Path, token))
                {
                    continue;
                }

                if (File.Exists(job.Path))
                {
                    var fileParameters = Metadata.ReadFromFile(job.Path, new ComfyUIParser(ServiceLocator.NodePropertyCache));

                    count++;

                    if (fileParameters.Hash != null)
                    {
                        var hashMatches = ServiceLocator.DataStore.GetImageIdByHash(fileParameters.Hash);

                        if (hashMatches.Any())
                        {
                            var moved = hashMatches.FirstOrDefault(d => !File.Exists(d.Path));

                            if (moved != null)
                            {
                                ServiceLocator.DataStore.UpdateImagePath(moved.Id, job.Path);

                                await ServiceLocator.DatabaseWriterService.QueueAsync(fileParameters, QueueType.Move, _settings.StoreMetadata, _settings.StoreWorkflow);

                                continue;
                            }
                        }
                    }

                    if (ServiceLocator.DataStore.ImageExists(job.Path))
                    {
                        await ServiceLocator.DatabaseWriterService.QueueAsync(fileParameters, QueueType.Update, _settings.StoreMetadata, _settings.StoreWorkflow);
                    }
                    else
                    {
                        await ServiceLocator.DatabaseWriterService.QueueAsync(fileParameters, QueueType.Add, _settings.StoreMetadata, _settings.StoreWorkflow);
                    }


                }
                else
                {
                    await ServiceLocator.DatabaseWriterService.QueueAsync(new FileParameters() { Path = job.Path }, QueueType.Skip, _settings.StoreMetadata, _settings.StoreWorkflow);
                }

            }
            catch (Exception ex)
            {
                Logger.Log($"Error scanning {job.Path}:" + ex.Message);
            }
        }

        return count;
    }
}