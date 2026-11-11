using System.Windows;

namespace DeepCoolLite.Settings;

public partial class InstallDialog : Window
{
    public InstallDialog(bool officialAppActive)
    {
        InitializeComponent();
        if (!officialAppActive)
        {
            DisableDeepCool.IsChecked = false;
            DisableDeepCoolCard.Visibility = Visibility.Collapsed;
        }
    }

    public bool DisableOfficialApp => DisableDeepCool.IsChecked == true;

    public bool CreateDesktopShortcut => DesktopShortcut.IsChecked == true;

    private void Install_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
