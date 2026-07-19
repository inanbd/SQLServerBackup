using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using SqlBackup.App.Infrastructure;
using SqlBackup.App.Views;
using SqlBackup.Core.Models;

namespace SqlBackup.App.ViewModels;

public sealed class JobRow
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public bool Enabled { get; init; }
    public string ConnectionName { get; init; } = "";
    public string DatabasesText { get; init; } = "";
    public string TypeText { get; init; } = "";
    public string ScheduleText { get; init; } = "";
    public string DestinationFolder { get; init; } = "";
    public string RetentionText { get; init; } = "";
}

public sealed class JobsViewModel : ObservableObject, IActivatable
{
    private readonly AppServices _services;
    private readonly StandaloneRunner _standalone;
    private JobRow? _selectedJob;
    private bool _isBusy;
    private string _busyText = "";

    public JobsViewModel(AppServices services)
    {
        _services = services;
        _standalone = new StandaloneRunner(services);
        AddCommand = new RelayCommand(_ => AddOrEdit(null));
        EditCommand = new RelayCommand(_ => AddOrEdit(SelectedJob?.Id), _ => SelectedJob is not null);
        DeleteCommand = new AsyncRelayCommand(_ => DeleteAsync(), _ => SelectedJob is not null);
        RunNowCommand = new AsyncRelayCommand(_ => RunNowAsync(), _ => SelectedJob is not null && !IsBusy);
        ToggleEnabledCommand = new AsyncRelayCommand(_ => ToggleEnabledAsync(), _ => SelectedJob is not null);
    }

    public ObservableCollection<JobRow> Jobs { get; } = new();

    public JobRow? SelectedJob
    {
        get => _selectedJob;
        set => Set(ref _selectedJob, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => Set(ref _isBusy, value);
    }

    public string BusyText
    {
        get => _busyText;
        private set => Set(ref _busyText, value);
    }

    public ICommand AddCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand RunNowCommand { get; }
    public ICommand ToggleEnabledCommand { get; }

    public void Activated() => Reload();

    private void Reload()
    {
        var config = _services.ConfigStore.Load();
        Jobs.Clear();
        foreach (var job in config.Jobs.OrderBy(j => j.Name))
        {
            Jobs.Add(new JobRow
            {
                Id = job.Id,
                Name = job.Name,
                Enabled = job.Enabled,
                ConnectionName = config.FindConnection(job.ConnectionId)?.Name ?? "(missing connection)",
                DatabasesText = string.Join(", ", job.Databases),
                TypeText = job.Type switch
                {
                    BackupType.Full => "Full",
                    BackupType.Differential => "Differential",
                    BackupType.TransactionLog => "Transaction log",
                    _ => job.Type.ToString(),
                },
                ScheduleText = job.Schedule.Describe(),
                DestinationFolder = job.DestinationFolder,
                RetentionText = job.Retention.Describe(),
            });
        }
    }

    private void AddOrEdit(Guid? jobId)
    {
        var config = _services.ConfigStore.Load();
        if (config.Connections.Count == 0)
        {
            MessageBox.Show("Add a SQL Server connection first (Connections page).", "SQL Server Backup",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var existing = jobId is { } id ? config.FindJob(id) : null;
        var working = existing is null ? new BackupJob() : Cloner.DeepClone(existing);
        var editor = new JobEditorViewModel(working, config.Connections, _services);
        var dialog = new JobEditorWindow(editor) { Owner = Application.Current.MainWindow };

        if (dialog.ShowDialog() != true)
            return;

        _services.ConfigStore.Update(c =>
        {
            var index = c.Jobs.FindIndex(j => j.Id == working.Id);
            if (index >= 0)
                c.Jobs[index] = working;
            else
                c.Jobs.Add(working);
        });
        _ = _services.TryNotifyServiceOfConfigChangeAsync();
        Reload();
        SelectedJob = Jobs.FirstOrDefault(j => j.Id == working.Id);
    }

    private async Task DeleteAsync()
    {
        if (SelectedJob is not { } row)
            return;
        if (MessageBox.Show($"Delete job '{row.Name}'? Existing backup files are kept.", "SQL Server Backup",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _services.ConfigStore.Update(c => c.Jobs.RemoveAll(j => j.Id == row.Id));
        await _services.TryNotifyServiceOfConfigChangeAsync();
        Reload();
    }

    private async Task ToggleEnabledAsync()
    {
        if (SelectedJob is not { } row)
            return;
        _services.ConfigStore.Update(c =>
        {
            if (c.FindJob(row.Id) is { } job)
                job.Enabled = !job.Enabled;
        });
        await _services.TryNotifyServiceOfConfigChangeAsync();
        Reload();
        SelectedJob = Jobs.FirstOrDefault(j => j.Id == row.Id);
    }

    private async Task RunNowAsync()
    {
        if (SelectedJob is not { } row)
            return;

        if (await _services.Ipc.IsServiceReachableAsync())
        {
            var response = await _services.Ipc.RunJobAsync(row.Id);
            MessageBox.Show(
                response.Accepted
                    ? $"Job '{row.Name}' was handed to the service. Watch the Dashboard/History for progress."
                    : response.Reason ?? "The job was not started.",
                "SQL Server Backup", MessageBoxButton.OK,
                response.Accepted ? MessageBoxImage.Information : MessageBoxImage.Warning);
            return;
        }

        var runStandalone = MessageBox.Show(
            "The backup service is not reachable.\n\nRun this job directly inside the app instead? " +
            "(The backup then runs under your Windows account.)",
            "SQL Server Backup", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (runStandalone != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        BusyText = $"Running '{row.Name}' …";
        try
        {
            var result = await _standalone.RunAsync(row.Id);
            MessageBox.Show($"Job '{row.Name}': {result.Summarize()}\nDetails are in History.",
                "SQL Server Backup", MessageBoxButton.OK,
                result.AllSucceeded ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
            BusyText = "";
        }
    }
}
