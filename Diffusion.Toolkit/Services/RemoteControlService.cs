using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Diffusion.Common;
using Diffusion.Toolkit.Configuration;
using Diffusion.Toolkit.Models;

namespace Diffusion.Toolkit.Services;

/// <summary>
/// Lets another application on this computer drive the toolkit - rate, tag, page through results -
/// while its own window has focus.
/// </summary>
/// <remarks>
/// Raw TCP rather than HTTP on purpose. HttpListener sits on HTTP.SYS, which wants a URL
/// reservation registered by an administrator, and the application runs asInvoker. A socket needs
/// nothing but the port.
///
/// The protocol is one JSON object per line, in both directions. Requests carry an action and
/// optionally an id, which is echoed back on the reply so a client can match the two up. State is
/// pushed unsolicited whenever it changes, and once when a client connects so that a controller
/// starting up late is still correct.
///
/// Bound to <see cref="IPAddress.Loopback"/>, so nothing off this machine can reach it. Note that
/// this is IPv4 only, and "localhost" resolves to ::1 first on Windows - clients must connect to
/// 127.0.0.1 by address.
/// </remarks>
public sealed class RemoteControlService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ConcurrentDictionary<Guid, Client> _clients = new();

    private RemoteControlActions? _actions;
    private CancellationTokenSource? _cancellation;
    private TcpListener? _listener;
    private Action? _broadcastState;
    private ImageViewModel? _watchedImage;
    private bool _initialized;
    private bool _subscribed;
    private int _port;

    /// <summary>
    /// Starts listening if the settings ask for it, and keeps in step with them from then on.
    /// </summary>
    public void Initialize()
    {
        if (_initialized) return;

        _initialized = true;

        var settings = ServiceLocator.ExtendedSettings;

        settings.SettingChanged += OnSettingChanged;

        // A burst of property changes during a page load should produce one message, not twenty
        _broadcastState = Utility.Debounce(BroadcastState, 100);

        Apply();
    }

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.SettingName is not (nameof(ExtendedSettings.RemoteControlEnabled)
            or nameof(ExtendedSettings.RemoteControlPort)))
        {
            return;
        }

        Apply();
    }

    private void Apply()
    {
        var settings = ServiceLocator.ExtendedSettings;

        if (!settings.RemoteControlEnabled)
        {
            Stop();
            return;
        }

        if (_listener != null && _port == settings.RemoteControlPort) return;

        Stop();
        Start(settings.RemoteControlPort);
    }

    private void Start(int port)
    {
        if (port is < 1 or > 65535)
        {
            Logger.Log($"Remote control not started: {port} is not a usable port");
            return;
        }

        try
        {
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
        }
        catch (SocketException e)
        {
            // Most likely another copy of the application already has the port. Not being able to
            // listen is not a reason to stop the application working.
            Logger.Log($"Remote control could not listen on 127.0.0.1:{port}: {e.Message}");

            _listener = null;

            return;
        }

        _port = port;
        _actions ??= new RemoteControlActions();
        _cancellation = new CancellationTokenSource();

        Subscribe();

        var token = _cancellation.Token;
        var listener = _listener;

        Task.Run(() => AcceptLoop(listener, token), token);

        Logger.Log($"Remote control listening on 127.0.0.1:{port}");
    }

    private void Stop()
    {
        if (_listener == null && _cancellation == null) return;

        try
        {
            _cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }

        _clients.Clear();

        try
        {
            _listener?.Stop();
        }
        catch (SocketException)
        {
        }

        _listener = null;

        _cancellation?.Dispose();
        _cancellation = null;
    }

    private async Task AcceptLoop(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient tcpClient;

            try
            {
                tcpClient = await listener.AcceptTcpClientAsync(token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }

            var client = new Client(tcpClient);

            _clients[client.Id] = client;

            _ = Task.Run(() => ServeClient(client, token), token);
        }
    }

    private async Task ServeClient(Client client, CancellationToken token)
    {
        try
        {
            // So a controller that connects after the fact still starts out correct
            await client.SendAsync(BuildStatePayload(), token);

            while (!token.IsCancellationRequested)
            {
                var line = await client.Reader.ReadLineAsync(token);

                if (line == null) return;

                if (string.IsNullOrWhiteSpace(line)) continue;

                var reply = await HandleAsync(line);

                if (reply != null)
                {
                    await client.SendAsync(reply, token);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
            // The client went away mid-read. Ordinary.
        }
        catch (Exception e)
        {
            Logger.Log($"Remote control client error: {e.Message}");
        }
        finally
        {
            _clients.TryRemove(client.Id, out _);
            client.Dispose();
        }
    }

    /// <summary>
    /// Turns one request line into one reply line. Never throws - a bad request has to come back
    /// as an error rather than taking the connection or the application down.
    /// </summary>
    private async Task<string?> HandleAsync(string line)
    {
        int? id = null;

        try
        {
            using var document = JsonDocument.Parse(line);

            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return Reply(null, false, "expected a json object");
            }

            if (root.TryGetProperty("id", out var idElement) && idElement.TryGetInt32(out var parsedId))
            {
                id = parsedId;
            }

            if (!root.TryGetProperty("action", out var actionElement)
                || actionElement.ValueKind != JsonValueKind.String)
            {
                return Reply(id, false, "missing action");
            }

            var action = actionElement.GetString() ?? string.Empty;

            var value = root.TryGetProperty("value", out var valueElement) ? valueElement : (JsonElement?)null;

            // Actions run on the UI thread, which a modal dialog can hold indefinitely. Time out
            // rather than leaving the connection waiting on a reply that may never come.
            var result = await (_actions ?? new RemoteControlActions())
                .InvokeAsync(action, value)
                .WaitAsync(TimeSpan.FromSeconds(10));

            return Reply(id, result.Ok, result.Error);
        }
        catch (JsonException)
        {
            return Reply(id, false, "invalid json");
        }
        catch (TimeoutException)
        {
            return Reply(id, false, "timed out");
        }
        catch (Exception e)
        {
            Logger.Log($"Remote control action failed: {e.Message}");

            return Reply(id, false, "failed");
        }
    }

    private static string Reply(int? id, bool ok, string? error)
    {
        var payload = new { id, ok, error };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    /// <summary>
    /// Watches the state a controller wants to reflect on its buttons.
    /// </summary>
    private void Subscribe()
    {
        if (_subscribed) return;

        if (ServiceLocator.MainModel != null)
        {
            ServiceLocator.MainModel.PropertyChanged += (sender, args) => _broadcastState?.Invoke();
        }

        var search = ServiceLocator.SearchModel;

        if (search != null)
        {
            search.PropertyChanged += (sender, args) =>
            {
                // The image object is replaced on every selection change, so the subscription has
                // to move with it or marking would stop being reported after the first image
                if (args.PropertyName == nameof(SearchModel.CurrentImage))
                {
                    WatchCurrentImage(search.CurrentImage);
                }

                _broadcastState?.Invoke();
            };

            WatchCurrentImage(search.CurrentImage);
        }

        _subscribed = true;
    }

    /// <summary>
    /// Follows how the selected image is marked, so favouriting it from the keyboard reaches a
    /// controller as well as the window.
    /// </summary>
    private void WatchCurrentImage(ImageViewModel? image)
    {
        if (ReferenceEquals(_watchedImage, image)) return;

        if (_watchedImage != null)
        {
            _watchedImage.PropertyChanged -= OnCurrentImageChanged;
        }

        _watchedImage = image;

        if (_watchedImage != null)
        {
            _watchedImage.PropertyChanged += OnCurrentImageChanged;
        }
    }

    private void OnCurrentImageChanged(object? sender, PropertyChangedEventArgs e)
    {
        _broadcastState?.Invoke();
    }

    private void BroadcastState()
    {
        if (_clients.IsEmpty) return;

        var payload = BuildStatePayload();

        foreach (var client in _clients.Values)
        {
            _ = client.SendAsync(payload, CancellationToken.None);
        }
    }

    private static string BuildStatePayload()
    {
        var main = ServiceLocator.MainModel;
        var search = ServiceLocator.SearchModel;

        var image = main?.CurrentImage;

        // An unavailable file is built without an id, and the padding entries that fill out a page
        // sit at id 0 as well, so neither counts as something a controller can act on
        var hasSelection = image is { Id: > 0 };

        var payload = new
        {
            @event = "state",
            page = search?.Page ?? 0,
            pages = search?.Pages ?? 0,
            results = search?.TotalFiles ?? 0,
            reviewing = main?.IsReviewing ?? false,
            hasReviewSession = main?.HasReviewSession ?? false,
            autoAdvance = main?.AutoAdvance ?? false,
            fitToPreview = main?.FitToPreview ?? false,
            actualSize = main?.ActualSize ?? false,
            hasFilter = main?.HasFilter ?? false,
            busy = main?.IsBusy ?? false,

            // How the current image is marked, so a controller can show a key as already on
            hasSelection,
            favorite = hasSelection && image!.Favorite,
            nsfw = hasSelection && image!.NSFW,
            forDeletion = hasSelection && image!.ForDeletion,
            inQuickAlbum = hasSelection && image!.IsInQuickAlbum,
            rating = hasSelection ? image!.Rating : null,
            infoVisible = hasSelection && image!.IsParametersVisible,

            view = CurrentView(),

            // Something is waiting to be answered, so a key that would normally navigate is a
            // confirmation instead
            hasPopup = ServiceLocator.MessageService?.HasPopup ?? false
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    /// <summary>
    /// Which section of the library is showing, as the same short name the view commands take.
    /// </summary>
    private static string? CurrentView()
    {
        var url = ServiceLocator.NavigatorService?.CurrentUrl;

        if (string.IsNullOrEmpty(url)) return null;

        var hash = url.IndexOf('#');

        return hash >= 0 && hash < url.Length - 1 ? url[(hash + 1)..] : null;
    }

    public void Dispose()
    {
        Stop();
    }

    private sealed class Client : IDisposable
    {
        private readonly TcpClient _tcpClient;
        private readonly StreamWriter _writer;
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public Client(TcpClient tcpClient)
        {
            _tcpClient = tcpClient;

            var stream = tcpClient.GetStream();

            Reader = new StreamReader(stream, new UTF8Encoding(false));

            _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        }

        public Guid Id { get; } = Guid.NewGuid();

        public StreamReader Reader { get; }

        /// <summary>
        /// State pushes and replies can race, so writes are serialised - two interleaved lines
        /// would be unparseable at the other end.
        /// </summary>
        public async Task SendAsync(string line, CancellationToken token)
        {
            try
            {
                await _writeLock.WaitAsync(token);

                try
                {
                    await _writer.WriteLineAsync(line.AsMemory(), token);
                }
                finally
                {
                    _writeLock.Release();
                }
            }
            catch (Exception)
            {
                // A client that has gone away is removed by its own read loop
            }
        }

        public void Dispose()
        {
            try
            {
                _tcpClient.Close();
                _tcpClient.Dispose();
            }
            catch (Exception)
            {
            }

            _writeLock.Dispose();
        }
    }
}
