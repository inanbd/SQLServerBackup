using System.Windows;
using System.Windows.Controls;
using SqlBackup.App.ViewModels;

namespace SqlBackup.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private void OnSmtpPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            vm.SetSmtpPassword(SmtpPasswordInput.Password);
    }
}
