using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using Diffusion.Common;

namespace Diffusion.Toolkit.Services;

/// <summary>
/// Keeps a second copy of the application from starting, handing the running window to the front
/// instead.
/// </summary>
/// <remarks>
/// Two kernel objects, both scoped to the current logon session so that two Windows users each get
/// their own instance: a mutex that says whether anyone is already running, and an event the second
/// copy sets to ask the first one to show itself.
///
/// Both names carry a hash of the settings path. A portable copy and one running out of %APPDATA%
/// are separate libraries, and blocking one because the other is open would be wrong.
/// </remarks>
public sealed class SingleInstanceService : IDisposable
{
    private const string MutexPrefix = @"Local\DiffusionToolkitPlus.Instance.";
    private const string SignalPrefix = @"Local\DiffusionToolkitPlus.Activate.";

    private Mutex? _mutex;
    private EventWaitHandle? _signal;
    private RegisteredWaitHandle? _registration;
    private bool _owned;

    /// <summary>
    /// Claims the instance for this process.
    /// </summary>
    /// <returns>
    /// False when another copy already holds it, and this process should exit. Any failure to
    /// create the mutex is reported as True - refusing to start because a handle could not be
    /// opened would be a worse outcome than briefly allowing two copies.
    /// </returns>
    public bool TryAcquire()
    {
        try
        {
            var key = GetInstanceKey();

            _mutex = new Mutex(true, MutexPrefix + key, out var createdNew);

            if (!createdNew)
            {
                _mutex.Dispose();
                _mutex = null;
                return false;
            }

            _owned = true;

            return true;
        }
        catch (Exception e)
        {
            Logger.Log($"Could not claim the single instance mutex, starting anyway: {e.Message}");
            return true;
        }
    }

    /// <summary>
    /// Asks the copy that is already running to show itself. Called by the process that is about
    /// to exit.
    /// </summary>
    public void SignalRunningInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(SignalPrefix + GetInstanceKey(), out var handle))
            {
                using (handle)
                {
                    handle.Set();
                }
            }
        }
        catch (Exception e)
        {
            // The other copy may be shutting down as we ask. Nothing here is worth a dialog.
            Logger.Log($"Could not signal the running instance: {e.Message}");
        }
    }

    /// <summary>
    /// Starts listening for a second copy asking us to come to the front. The callback runs on a
    /// thread pool thread.
    /// </summary>
    public void ListenForActivation(Action onActivate)
    {
        try
        {
            _signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalPrefix + GetInstanceKey());

            _registration = ThreadPool.RegisterWaitForSingleObject(
                _signal,
                (state, timedOut) => onActivate(),
                null,
                Timeout.Infinite,
                false);
        }
        catch (Exception e)
        {
            // Without this the second copy simply exits without raising us, which is survivable.
            Logger.Log($"Could not listen for activation requests: {e.Message}");
        }
    }

    /// <summary>
    /// Brings a window to the front from wherever it is - minimised, or behind another application.
    /// </summary>
    /// <remarks>
    /// Windows will not let a process that does not already own the foreground take it, so the
    /// Topmost flick is needed to get the window raised rather than its taskbar button flashed.
    /// It is the usual workaround rather than a guarantee: under some focus-lock conditions the
    /// window still only flashes.
    /// </remarks>
    public static void Surface(Window? window)
    {
        if (window == null) return;

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Show();
        window.Activate();

        var wasTopmost = window.Topmost;

        window.Topmost = true;
        window.Topmost = wasTopmost;

        window.Focus();
    }

    /// <summary>
    /// Same name for the same library, and a different one for a library somewhere else.
    /// </summary>
    private static string GetInstanceKey()
    {
        var path = AppInfo.SettingsPath ?? string.Empty;

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));

        return Convert.ToHexString(hash, 0, 8);
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _registration = null;

        _signal?.Dispose();
        _signal = null;

        if (_mutex != null)
        {
            if (_owned)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // Released on another thread, or never actually held. Nothing to do.
                }
            }

            _mutex.Dispose();
            _mutex = null;
        }
    }
}
