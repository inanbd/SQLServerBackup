using System.Collections.ObjectModel;
using SqlBackup.App.Infrastructure;

namespace SqlBackup.App.ViewModels;

/// <summary>A view model that wants to refresh when its section is opened.</summary>
public interface IActivatable
{
    void Activated();
}

public sealed record Section(string Title, object ViewModel);

public sealed class MainViewModel : ObservableObject
{
    private Section _selectedSection;

    public MainViewModel(AppServices services)
    {
        var dashboard = new DashboardViewModel(services);
        Sections = new ObservableCollection<Section>
        {
            new("Dashboard", dashboard),
            new("Connections", new ConnectionsViewModel(services)),
            new("Backup Jobs", new JobsViewModel(services)),
            new("History", new HistoryViewModel(services)),
            new("Settings", new SettingsViewModel(services)),
        };
        _selectedSection = Sections[0];
        dashboard.Activated();
    }

    public ObservableCollection<Section> Sections { get; }

    public Section SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (Set(ref _selectedSection, value))
                (value?.ViewModel as IActivatable)?.Activated();
        }
    }
}
