using System.Windows.Controls;
using System.Windows.Input;
using SqlBackup.App.ViewModels;

namespace SqlBackup.App.Views;

public partial class JobsView : UserControl
{
    public JobsView() => InitializeComponent();

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is JobsViewModel vm && vm.EditCommand.CanExecute(null))
            vm.EditCommand.Execute(null);
    }
}
