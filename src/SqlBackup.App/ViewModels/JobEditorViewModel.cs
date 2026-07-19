using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using SqlBackup.App.Infrastructure;
using SqlBackup.Core.Backup;
using SqlBackup.Core.Models;
using SqlBackup.Core.Scheduling;

namespace SqlBackup.App.ViewModels;

public sealed class SelectableName : ObservableObject
{
    private bool _isSelected;

    public string Name { get; init; } = "";
    public string Note { get; init; } = "";

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }
}

public sealed class DayChoice : ObservableObject
{
    private bool _isSelected;

    public DayOfWeek Day { get; init; }
    public string Label => Day.ToString()[..3];

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }
}

public sealed class JobEditorViewModel : ObservableObject
{
    private readonly AppServices _services;
    private ConnectionProfile? _selectedConnection;
    private ScheduleKind _scheduleKind;
    private string _databasesStatusText = "Click 'Load databases' to list what's on the server.";
    private string _schedulePreviewText = "";
    private string _destinationStatusText = "";
    private string _newDatabaseName = "";

    public JobEditorViewModel(BackupJob working, IReadOnlyList<ConnectionProfile> connections, AppServices services)
    {
        Working = working;
        _services = services;
        Connections = connections;
        _selectedConnection = connections.FirstOrDefault(c => c.Id == working.ConnectionId) ?? connections.FirstOrDefault();
        _scheduleKind = working.Schedule.Kind;

        IntervalMinutesText = working.Schedule.IntervalMinutes.ToString(CultureInfo.InvariantCulture);
        TimeOfDayText = working.Schedule.TimeOfDay.ToString("HH':'mm");
        CronExpression = working.Schedule.CronExpression;
        KeepLastText = working.Retention.KeepLast.ToString(CultureInfo.InvariantCulture);
        MaxAgeDaysText = working.Retention.MaxAgeDays.ToString(CultureInfo.InvariantCulture);

        foreach (var database in working.Databases)
            DatabaseItems.Add(new SelectableName { Name = database, IsSelected = true });

        Days = new ObservableCollection<DayChoice>(
            new[]
            {
                DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
                DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
            }.Select(d => new DayChoice { Day = d, IsSelected = working.Schedule.Days.Contains(d) }));

        LoadDatabasesCommand = new AsyncRelayCommand(_ => LoadDatabasesAsync(), _ => SelectedConnection is not null);
        AddDatabaseCommand = new RelayCommand(_ => AddDatabaseManually(), _ => NewDatabaseName.Trim().Length > 0);
        PreviewScheduleCommand = new RelayCommand(_ => PreviewSchedule());
        ValidateDestinationCommand = new RelayCommand(_ => ValidateDestination());
    }

    public BackupJob Working { get; }
    public IReadOnlyList<ConnectionProfile> Connections { get; }
    public ObservableCollection<SelectableName> DatabaseItems { get; } = new();
    public ObservableCollection<DayChoice> Days { get; }

    public IReadOnlyList<BackupType> BackupTypes { get; } = Enum.GetValues<BackupType>();
    public IReadOnlyList<ScheduleKind> ScheduleKinds { get; } = Enum.GetValues<ScheduleKind>();
    public IReadOnlyList<RetentionMode> RetentionModes { get; } = Enum.GetValues<RetentionMode>();

    public ICommand LoadDatabasesCommand { get; }
    public ICommand AddDatabaseCommand { get; }
    public ICommand PreviewScheduleCommand { get; }
    public ICommand ValidateDestinationCommand { get; }

    public string Name
    {
        get => Working.Name;
        set { Working.Name = value; OnPropertyChanged(); }
    }

    public bool Enabled
    {
        get => Working.Enabled;
        set { Working.Enabled = value; OnPropertyChanged(); }
    }

    public ConnectionProfile? SelectedConnection
    {
        get => _selectedConnection;
        set
        {
            if (Set(ref _selectedConnection, value) && value is not null)
                Working.ConnectionId = value.Id;
        }
    }

    public BackupType Type
    {
        get => Working.Type;
        set { Working.Type = value; OnPropertyChanged(); }
    }

    public ScheduleKind ScheduleKind
    {
        get => _scheduleKind;
        set
        {
            if (Set(ref _scheduleKind, value))
            {
                Working.Schedule.Kind = value;
                SchedulePreviewText = "";
            }
        }
    }

    public string IntervalMinutesText { get; set; }
    public string TimeOfDayText { get; set; }
    public string CronExpression { get; set; }
    public string KeepLastText { get; set; }
    public string MaxAgeDaysText { get; set; }

    public string DestinationFolder
    {
        get => Working.DestinationFolder;
        set { Working.DestinationFolder = value; OnPropertyChanged(); }
    }

    public bool SubfolderPerDatabase
    {
        get => Working.SubfolderPerDatabase;
        set { Working.SubfolderPerDatabase = value; OnPropertyChanged(); }
    }

    public RetentionMode RetentionMode
    {
        get => Working.Retention.Mode;
        set { Working.Retention.Mode = value; OnPropertyChanged(); }
    }

    public bool OptionCompression
    {
        get => Working.Options.Compression;
        set { Working.Options.Compression = value; OnPropertyChanged(); }
    }

    public bool OptionChecksum
    {
        get => Working.Options.Checksum;
        set { Working.Options.Checksum = value; OnPropertyChanged(); }
    }

    public bool OptionCopyOnly
    {
        get => Working.Options.CopyOnly;
        set { Working.Options.CopyOnly = value; OnPropertyChanged(); }
    }

    public bool OptionVerify
    {
        get => Working.Options.VerifyAfterBackup;
        set { Working.Options.VerifyAfterBackup = value; OnPropertyChanged(); }
    }

    public bool OptionFallbackToFull
    {
        get => Working.Options.FallbackToFullIfNoBase;
        set { Working.Options.FallbackToFullIfNoBase = value; OnPropertyChanged(); }
    }

    public bool CatchUpMissedRun
    {
        get => Working.CatchUpMissedRun;
        set { Working.CatchUpMissedRun = value; OnPropertyChanged(); }
    }

    public string NewDatabaseName
    {
        get => _newDatabaseName;
        set => Set(ref _newDatabaseName, value);
    }

    public string DatabasesStatusText
    {
        get => _databasesStatusText;
        private set => Set(ref _databasesStatusText, value);
    }

    public string SchedulePreviewText
    {
        get => _schedulePreviewText;
        private set => Set(ref _schedulePreviewText, value);
    }

    public string DestinationStatusText
    {
        get => _destinationStatusText;
        private set => Set(ref _destinationStatusText, value);
    }

    private async Task LoadDatabasesAsync()
    {
        if (SelectedConnection is not { } connection)
            return;
        DatabasesStatusText = "Loading databases…";
        try
        {
            var connectionString = SqlConnectionFactory.BuildConnectionString(connection, _services.Protector);
            var onServer = await SqlServerQueries.ListDatabasesAsync(connectionString);

            var selected = DatabaseItems.Where(i => i.IsSelected).Select(i => i.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            DatabaseItems.Clear();
            foreach (var database in onServer)
                DatabaseItems.Add(new SelectableName { Name = database, IsSelected = selected.Remove(database) });
            foreach (var orphan in selected)
                DatabaseItems.Add(new SelectableName { Name = orphan, IsSelected = true, Note = "(not found on server)" });

            DatabasesStatusText = $"{onServer.Count} database(s) on '{connection.Name}'.";
        }
        catch (Exception ex)
        {
            DatabasesStatusText = "✗ " + ex.Message;
        }
    }

    private void AddDatabaseManually()
    {
        var name = NewDatabaseName.Trim();
        if (name.Length == 0)
            return;
        var existing = DatabaseItems.FirstOrDefault(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            existing.IsSelected = true;
        else
            DatabaseItems.Add(new SelectableName { Name = name, IsSelected = true });
        NewDatabaseName = "";
    }

    private void PreviewSchedule()
    {
        if (!TryBuildSchedule(out var schedule, out var error))
        {
            SchedulePreviewText = "✗ " + error;
            return;
        }
        var next = ScheduleCalculator.PreviewNext(schedule, 3, DateTimeOffset.Now, TimeZoneInfo.Local);
        SchedulePreviewText = next.Count == 0
            ? "No upcoming occurrences."
            : "Next: " + string.Join(",  ", next.Select(n => n.ToLocalTime().ToString("ddd dd MMM HH:mm")));
    }

    private void ValidateDestination()
    {
        var issues = PreflightChecker.CheckDestination(DestinationFolder, null, minFreeMb: 512);
        DestinationStatusText = issues.Count == 0
            ? "✓ Folder exists, is writable from this machine, and has disk space."
            : string.Join(" ", issues.Select(i => "• " + i.Message));
    }

    private bool TryBuildSchedule(out ScheduleSpec schedule, out string error)
    {
        schedule = new ScheduleSpec { Kind = ScheduleKind };
        error = "";

        switch (ScheduleKind)
        {
            case ScheduleKind.Interval:
                if (!int.TryParse(IntervalMinutesText, out var minutes) || minutes < 1)
                {
                    error = "Interval must be a whole number of minutes (≥ 1).";
                    return false;
                }
                schedule.IntervalMinutes = minutes;
                break;

            case ScheduleKind.Daily:
            case ScheduleKind.Weekly:
                if (!TimeOnly.TryParse(TimeOfDayText, CultureInfo.InvariantCulture, out var time))
                {
                    error = "Time of day must look like 02:30.";
                    return false;
                }
                schedule.TimeOfDay = time;
                schedule.Days = Days.Where(d => d.IsSelected).Select(d => d.Day).ToList();
                break;

            case ScheduleKind.Cron:
                schedule.CronExpression = CronExpression.Trim();
                break;
        }

        return ScheduleCalculator.TryValidate(schedule, out error);
    }

    public bool TryCommit(out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(Name))
        {
            error = "Enter a job name.";
            return false;
        }
        if (SelectedConnection is null)
        {
            error = "Pick a SQL Server connection.";
            return false;
        }

        var databases = DatabaseItems.Where(i => i.IsSelected).Select(i => i.Name).ToList();
        if (databases.Count == 0)
        {
            error = "Select at least one database to back up.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(DestinationFolder))
        {
            error = "Choose a destination folder.";
            return false;
        }
        if (!TryBuildSchedule(out var schedule, out error))
            return false;

        if (RetentionMode == RetentionMode.KeepLastN)
        {
            if (!int.TryParse(KeepLastText, out var keep) || keep < 1)
            {
                error = "Retention: 'keep last' must be a whole number ≥ 1.";
                return false;
            }
            Working.Retention.KeepLast = keep;
        }
        else if (RetentionMode == RetentionMode.MaxAgeDays)
        {
            if (!int.TryParse(MaxAgeDaysText, out var days) || days < 1)
            {
                error = "Retention: 'max age' must be a whole number of days ≥ 1.";
                return false;
            }
            Working.Retention.MaxAgeDays = days;
        }

        Working.ConnectionId = SelectedConnection.Id;
        Working.Databases = databases;
        Working.Schedule = schedule;
        Working.Retention.Mode = RetentionMode;
        return true;
    }
}
