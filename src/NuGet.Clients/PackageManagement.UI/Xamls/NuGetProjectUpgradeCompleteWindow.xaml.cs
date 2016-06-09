using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;

namespace NuGet.PackageManagement.UI
{
    /// <summary>
    /// Interaction logic for NuGetProjectUpgradeCompleteWindow.xaml
    /// </summary>
    public partial class NuGetProjectUpgradeCompleteWindow : VsDialogWindow
    {
        public NuGetProjectUpgradeCompleteWindow(string backupLocation)
        {
            BackupLocation = backupLocation;
            InitializeComponent();
        }

        public string BackupLocation { get; }

        private void OkButtonClicked(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ExecuteOpenExternalLink(object sender, ExecutedRoutedEventArgs e)
        {
            var hyperlink = e.OriginalSource as Hyperlink;
            if (hyperlink?.NavigateUri != null)
            {
                Process.Start(hyperlink.NavigateUri.AbsoluteUri);
                e.Handled = true;
            }
        }
    }
}
