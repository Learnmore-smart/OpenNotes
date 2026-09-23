using System.IO;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Caelum.Pages
{
    /// <summary>
    /// Task 5 stub for the editor tab: displays the target PDF path and keeps
    /// the small public surface the shell already needs —
    /// <see cref="CurrentPdfPath"/> (tab title/info + rename flow) and
    /// <see cref="UpdateCurrentPdfPath"/> (MainWindow.HandleFilePathChanged).
    /// The real editor arrives with Task 6.
    /// </summary>
    public sealed partial class EditorPage : Page
    {
        public EditorPage()
        {
            this.InitializeComponent();
        }

        /// <summary>Full path of the PDF this tab is showing.</summary>
        public string CurrentPdfPath { get; private set; } = string.Empty;

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            ApplyPath(e.Parameter as string);
        }

        /// <summary>
        /// Rename flow: MainWindow calls this when a library rename changes
        /// the file path behind an open tab.
        /// </summary>
        public void UpdateCurrentPdfPath(string newPath)
        {
            ApplyPath(newPath);
        }

        private void ApplyPath(string path)
        {
            CurrentPdfPath = path ?? string.Empty;
            EditorTitleTextBlock.Text = string.IsNullOrWhiteSpace(CurrentPdfPath)
                ? string.Empty
                : Path.GetFileName(CurrentPdfPath);
            EditorPathTextBlock.Text = CurrentPdfPath;
        }
    }
}
