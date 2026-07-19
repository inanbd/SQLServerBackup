using System.Windows;
using SqlBackup.App.ViewModels;

namespace SqlBackup.App.Views;

public partial class ConnectionEditorWindow : Window
{
    private readonly ConnectionEditorViewModel _vm;

    public ConnectionEditorWindow(ConnectionEditorViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e) =>
        _vm.SetPassword(PasswordInput.Password);

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_vm.TryCommit(out var error))
        {
            DialogResult = true;
            return;
        }
        MessageBox.Show(error, "SQL Server Backup", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
