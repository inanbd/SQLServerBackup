using System.Windows;
using Microsoft.Win32;
using SqlBackup.App.ViewModels;

namespace SqlBackup.App.Views;

public partial class JobEditorWindow : Window
{
    private readonly JobEditorViewModel _vm;

    public JobEditorWindow(JobEditorViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
    }

    private void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select the backup destination folder" };
        if (dialog.ShowDialog(this) == true)
            _vm.DestinationFolder = dialog.FolderName;
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
