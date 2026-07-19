using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using SqlBackup.App.Infrastructure;
using SqlBackup.App.Views;
using SqlBackup.Core.Models;

namespace SqlBackup.App.ViewModels;

public sealed class OffsiteRow
{
    public required OffsiteDestination Destination { get; init; }
    public string Name => Destination.Name;
    public string KindText => Destination.Kind.ToString();
    public string TargetText => Destination.DescribeTarget();
    public string PrefixText => Destination.Prefix;
}

public sealed class OffsiteViewModel : ObservableObject, IActivatable
{
    private readonly AppServices _services;
    private OffsiteRow? _selectedRow;

    public OffsiteViewModel(AppServices services)
    {
        _services = services;
        AddCommand = new RelayCommand(_ => AddOrEdit(null));
        EditCommand = new RelayCommand(_ => AddOrEdit(SelectedRow?.Destination), _ => SelectedRow is not null);
        DeleteCommand = new AsyncRelayCommand(_ => DeleteAsync(), _ => SelectedRow is not null);
    }

    public ObservableCollection<OffsiteRow> Destinations { get; } = new();

    public OffsiteRow? SelectedRow
    {
        get => _selectedRow;
        set => Set(ref _selectedRow, value);
    }

    public ICommand AddCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }

    public void Activated() => Reload();

    private void Reload()
    {
        Destinations.Clear();
        foreach (var destination in _services.ConfigStore.Load().OffsiteDestinations.OrderBy(d => d.Name))
            Destinations.Add(new OffsiteRow { Destination = destination });
    }

    private void AddOrEdit(OffsiteDestination? existing)
    {
        var working = existing is null ? new OffsiteDestination() : Cloner.DeepClone(existing);
        var editor = new OffsiteDestinationEditorViewModel(working, _services.Protector, isNew: existing is null);
        var dialog = new OffsiteDestinationEditorWindow(editor) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true)
            return;

        _services.ConfigStore.Update(config =>
        {
            var index = config.OffsiteDestinations.FindIndex(d => d.Id == working.Id);
            if (index >= 0)
                config.OffsiteDestinations[index] = working;
            else
                config.OffsiteDestinations.Add(working);
        });
        _ = _services.TryNotifyServiceOfConfigChangeAsync();
        Reload();
        SelectedRow = Destinations.FirstOrDefault(d => d.Destination.Id == working.Id);
    }

    private async Task DeleteAsync()
    {
        if (SelectedRow is not { } row)
            return;

        var config = _services.ConfigStore.Load();
        var dependents = config.Jobs
            .Where(j => j.OffsiteDestinationId == row.Destination.Id)
            .Select(j => j.Name)
            .ToList();
        if (dependents.Count > 0)
        {
            MessageBox.Show(
                $"'{row.Name}' is used by: {string.Join(", ", dependents)}.\nRemove the off-site copy from those jobs first.",
                "SQL Server Backup", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show($"Delete off-site destination '{row.Name}'? Files already uploaded are kept.",
                "SQL Server Backup", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _services.ConfigStore.Update(c => c.OffsiteDestinations.RemoveAll(d => d.Id == row.Destination.Id));
        await _services.TryNotifyServiceOfConfigChangeAsync();
        Reload();
    }
}
