using System.Windows.Controls;
using System.Windows.Input;
using SqlBackup.App.ViewModels;

namespace SqlBackup.App.Views;

public partial class OffsiteView : UserControl
{
    public OffsiteView() => InitializeComponent();

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is OffsiteViewModel vm && vm.EditCommand.CanExecute(null))
            vm.EditCommand.Execute(null);
    }
}
