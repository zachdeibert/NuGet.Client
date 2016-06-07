using System.Windows;

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
    }
}
