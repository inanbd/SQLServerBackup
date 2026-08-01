using System.Windows;
using Microsoft.Win32;
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

    private void OnGoogleClientSecretChanged(object sender, RoutedEventArgs e) =>
        _vm.SetGoogleClientSecret(GoogleClientSecretInput.Password);

    private void OnLoadServiceAccountKey(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select the Google service account key",
            Filter = "Service account key (*.json)|*.json|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) == true)
            _vm.LoadServiceAccountKey(dialog.FileName);
    }

    private void OnBrowseShare(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select the share folder" };
        if (dialog.ShowDialog(this) == true)
            _vm.SmbPath = dialog.FolderName;
    }

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
