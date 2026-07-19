using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using SqlBackup.App.Infrastructure;
using SqlBackup.App.Views;
using SqlBackup.Core.Models;

namespace SqlBackup.App.ViewModels;

public sealed class ConnectionsViewModel : ObservableObject, IActivatable
{
    private readonly AppServices _services;
    private ConnectionProfile? _selectedConnection;

    public ConnectionsViewModel(AppServices services)
    {
        _services = services;
        AddCommand = new RelayCommand(_ => AddOrEdit(null));
        EditCommand = new RelayCommand(_ => AddOrEdit(SelectedConnection), _ => SelectedConnection is not null);
        DeleteCommand = new AsyncRelayCommand(_ => DeleteAsync(), _ => SelectedConnection is not null);
    }

    public ObservableCollection<ConnectionProfile> Connections { get; } = new();

    public ConnectionProfile? SelectedConnection
    {
        get => _selectedConnection;
        set => Set(ref _selectedConnection, value);
    }

    public ICommand AddCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }

    public void Activated() => Reload();

    private void Reload()
    {
        Connections.Clear();
        foreach (var connection in _services.ConfigStore.Load().Connections.OrderBy(c => c.Name))
            Connections.Add(connection);
    }

    private void AddOrEdit(ConnectionProfile? existing)
    {
        var working = existing is null ? new ConnectionProfile() : Cloner.DeepClone(existing);
        var editor = new ConnectionEditorViewModel(working, _services.Protector, isNew: existing is null);
        var dialog = new ConnectionEditorWindow(editor) { Owner = Application.Current.MainWindow };

        if (dialog.ShowDialog() != true)
            return;

        _services.ConfigStore.Update(config =>
        {
            var index = config.Connections.FindIndex(c => c.Id == working.Id);
            if (index >= 0)
                config.Connections[index] = working;
            else
                config.Connections.Add(working);
        });
        _ = _services.TryNotifyServiceOfConfigChangeAsync();
        Reload();
        SelectedConnection = Connections.FirstOrDefault(c => c.Id == working.Id);
    }

    private async Task DeleteAsync()
    {
        if (SelectedConnection is not { } connection)
            return;

        var config = _services.ConfigStore.Load();
        var dependents = config.Jobs.Where(j => j.ConnectionId == connection.Id).Select(j => j.Name).ToList();
        if (dependents.Count > 0)
        {
            MessageBox.Show(
                $"'{connection.Name}' is used by: {string.Join(", ", dependents)}.\nDelete or repoint those jobs first.",
                "SQL Server Backup", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show($"Delete connection '{connection.Name}'?", "SQL Server Backup",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _services.ConfigStore.Update(c => c.Connections.RemoveAll(x => x.Id == connection.Id));
        await _services.TryNotifyServiceOfConfigChangeAsync();
        Reload();
    }
}
