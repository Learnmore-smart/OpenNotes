using System;
using System.Threading.Tasks;
using Caelum.Services;
using Microsoft.UI.Xaml;

namespace Caelum
{
    /// <summary>
    /// V6 WinUI 3 application entry point. Provides application initialization
    /// and creates the main window.
    /// </summary>
    public partial class App : Application
    {
        private Window _window;

        public App()
        {
            // Crash hooks attach before InitializeComponent so even a XAML
            // load failure (e.g. missing xbf payload) leaves a managed stack
            // in the crash log. Handlers log only — they never swallow.
            UnhandledException += App_UnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            this.InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();
            _window.Activate();
        }

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            CrashLogger.Log("App.UnhandledException", e.Exception);
            e.Handled = false; // log only — the crash must still reach WER
        }

        private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            CrashLogger.Log("AppDomain.CurrentDomain.UnhandledException",
                e.ExceptionObject as Exception);
        }

        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            CrashLogger.Log("TaskScheduler.UnobservedTaskException", e.Exception);
            // No SetObserved() — logging, not suppression.
        }
    }
}
