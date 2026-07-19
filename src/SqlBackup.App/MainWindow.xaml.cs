using System.ComponentModel;
using System.Windows;

namespace SqlBackup.App;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void OnClosing(object sender, CancelEventArgs e)
    {
        if (App.ExitRequested)
            return;

        // Closing hides to the tray; the service keeps backing up regardless.
        e.Cancel = true;
        Hide();
        ((App)Application.Current).NotifyMainWindowHidden();
    }
}
