using System.Windows;
using SqlBackup.App.ViewModels;

namespace SqlBackup.App.Views;

public partial class OffsiteDestinationEditorWindow : Window
{
    private readonly OffsiteDestinationEditorViewModel _vm;

    public OffsiteDestinationEditorWindow(OffsiteDestinationEditorViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
    }

    private void OnSecretChanged(object sender, RoutedEventArgs e) =>
        _vm.SetSecret(SecretInput.Password);

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
