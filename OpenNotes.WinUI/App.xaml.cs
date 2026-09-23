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
            this.InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();
            _window.Activate();
        }
    }
}
