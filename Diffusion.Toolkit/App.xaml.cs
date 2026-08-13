using System;
using System.Windows;
using System.Windows.Threading;
using Diffusion.Common;

namespace Diffusion.Toolkit
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += OnDispatcherUnhandledException;
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
