using System.Windows.Controls;
using System.Windows.Input;
using SqlBackup.App.ViewModels;

namespace SqlBackup.App.Views;

public partial class ConnectionsView : UserControl
{
    public ConnectionsView() => InitializeComponent();

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ConnectionsViewModel vm && vm.EditCommand.CanExecute(null))
            vm.EditCommand.Execute(null);
    }
}
