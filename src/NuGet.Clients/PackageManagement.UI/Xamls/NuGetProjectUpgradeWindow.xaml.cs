using System.Windows;

namespace NuGet.PackageManagement.UI
{
    /// <summary>
    /// Interaction logic for NuGetProjectUpgradeWindow.xaml
    /// </summary>
    public partial class NuGetProjectUpgradeWindow : VsDialogWindow
    {
        public NuGetProjectUpgradeWindow()
        {
            InitializeComponent();
        }

        private void OkButtonClicked(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void CancelButtonClicked(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
