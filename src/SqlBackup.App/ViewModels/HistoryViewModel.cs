using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using SqlBackup.App.Infrastructure;
using SqlBackup.Core.Models;

namespace SqlBackup.App.ViewModels;

public sealed class JobFilterItem
{
    public Guid? JobId { get; init; }
    public string Title { get; init; } = "";
    public override string ToString() => Title;
}

public sealed class HistoryRow
{
    public required JobHistoryEntry Entry { get; init; }

    public string StartedText => Entry.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string JobName => Entry.JobName;
    public string Database => Entry.Database;
    public string TypeText => Entry.Type.ToString();
    public string TriggerText => Entry.Trigger.ToString();
    public string ResultText => !Entry.Success ? "FAILED"
        : Entry.OffsiteSuccess == false ? "OK / off-site FAILED"
        : "OK";
    public bool Success => Entry.Success;
    public bool OffsiteFailed => Entry.Success && Entry.OffsiteSuccess == false;
    public string DurationText => $"{Entry.DurationSeconds:F1} s";
    public string SizeText => Entry.FileSizeBytes is { } size ? $"{size / (1024.0 * 1024.0):F1} MB" : "—";
    public string FileName => Entry.FilePath is { } path ? System.IO.Path.GetFileName(path) : "—";

    public string DetailText
    {
        get
        {
            var lines = new List<string>
            {
                $"Job:      {Entry.JobName}",
                $"Database: {Entry.Database}   Type: {Entry.Type}   Trigger: {Entry.Trigger}",
                $"Started:  {StartedText}   Duration: {DurationText}",
            };
            if (Entry.FilePath is not null)
                lines.Add($"File:     {Entry.FilePath}" + (Entry.FileSizeBytes is { } s ? $" ({s:N0} bytes)" : ""));
            if (!string.IsNullOrEmpty(Entry.Message))
                lines.Add($"Notes:    {Entry.Message}");
            if (!string.IsNullOrEmpty(Entry.Error))
                lines.Add($"ERROR:    {Entry.Error}");
            return string.Join(Environment.NewLine, lines);
        }
    }
}

public sealed class HistoryViewModel : ObservableObject, IActivatable
{
    private readonly AppServices _services;
    private JobFilterItem? _selectedFilter;
    private HistoryRow? _selectedRow;
    private string _sourceText = "";

    public HistoryViewModel(AppServices services)
    {
        _services = services;
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
    }

    public ObservableCollection<JobFilterItem> JobFilters { get; } = new();
    public ObservableCollection<HistoryRow> Rows { get; } = new();

    public ICommand RefreshCommand { get; }

    public JobFilterItem? SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (Set(ref _selectedFilter, value))
                _ = RefreshAsync();
        }
    }

    public HistoryRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (Set(ref _selectedRow, value))
                OnPropertyChanged(nameof(DetailText));
        }
    }

    public string DetailText => SelectedRow?.DetailText ?? "Select a run to see its details.";

    public string SourceText
    {
        get => _sourceText;
        private set => Set(ref _sourceText, value);
    }

    public void Activated()
    {
        RebuildFilters();
        _ = RefreshAsync();
    }

    private void RebuildFilters()
    {
        var selectedId = SelectedFilter?.JobId;
        JobFilters.Clear();
        JobFilters.Add(new JobFilterItem { JobId = null, Title = "All jobs" });
        try
        {
            foreach (var job in _services.ConfigStore.Load().Jobs.OrderBy(j => j.Name))
                JobFilters.Add(new JobFilterItem { JobId = job.Id, Title = job.Name });
        }
        catch
        {
            // Config unreadable — the "All jobs" filter still works.
        }
        _selectedFilter = JobFilters.FirstOrDefault(f => f.JobId == selectedId) ?? JobFilters[0];
        OnPropertyChanged(nameof(SelectedFilter));
    }

    private async Task RefreshAsync()
    {
        var jobId = SelectedFilter?.JobId;

        IReadOnlyList<JobHistoryEntry> entries;
        try
        {
            entries = await _services.Ipc.GetHistoryAsync(500, jobId);
            SourceText = "Source: backup service";
        }
        catch
        {
            entries = await Task.Run(() => _services.History.ReadRecent(500, jobId));
            SourceText = "Source: local history file (service not reachable)";
        }

        Rows.Clear();
        foreach (var entry in entries)
            Rows.Add(new HistoryRow { Entry = entry });
    }
}
