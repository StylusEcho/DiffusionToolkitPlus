using System;
using System.Windows;
using System.Windows.Threading;
using Diffusion.Common;
using Diffusion.Toolkit.Services;

namespace Diffusion.Toolkit
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private SingleInstanceService? _singleInstance;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += OnDispatcherUnhandledException;

            if (!ClaimSingleInstance())
            {
                Shutdown();
                return;
            }

            // The window is created here rather than through StartupUri so that the copy which is
            // about to exit above never builds one at all, not even briefly
            var window = new MainWindow();

            this.MainWindow = window;

            window.Show();
        }

        /// <summary>
        /// Returns false when another copy is already running and this one should exit.
        /// </summary>
        private bool ClaimSingleInstance()
        {
            if (!ServiceLocator.ExtendedSettings.SingleInstance) return true;

            var singleInstance = new SingleInstanceService();

            if (!singleInstance.TryAcquire())
            {
                singleInstance.SignalRunningInstance();
                singleInstance.Dispose();

                return false;
            }

            singleInstance.ListenForActivation(() =>
                Dispatcher.InvokeAsync(() => SingleInstanceService.Surface(this.MainWindow)));

            _singleInstance = singleInstance;

            return true;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _singleInstance?.Dispose();
            _singleInstance = null;

            base.OnExit(e);
        }

        /// <summary>
        /// The AppDomain handler the main window installs cannot stop an exception raised on the UI
        /// thread from taking the process down with it, so a bug in a click or key handler loses the
        /// session. Catch those here instead and report them.
        /// </summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Logger.Log($"An unhandled exception occured: {e.Exception.Message}\r\n\r\n{e.Exception.StackTrace}");

            MessageBox.Show(e.Exception.Message, "An unhandled exception occured", MessageBoxButton.OK, MessageBoxImage.Exclamation);

            e.Handled = true;
        }
    }


}
