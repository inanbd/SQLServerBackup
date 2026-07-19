using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SqlBackup.App.Infrastructure;
using SqlBackup.Core.Models;

namespace SqlBackup.App.ViewModels;

public sealed class JobStatusRow : ObservableObject
{
    public Guid JobId { get; init; }
    public string Name { get; init; } = "";
    public string StateText { get; init; } = "";
    public string NextRunText { get; init; } = "";
    public string LastRunText { get; init; } = "";
    public string LastResultText { get; init; } = "";
    public bool? LastRunSuccess { get; init; }
    public bool IsRunning { get; init; }
}

public sealed class DashboardViewModel : ObservableObject, IActivatable
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<Guid, DateTimeOffset?> _seenLastRuns = new();
    private bool _refreshing;
    private bool _firstRefreshDone;
    private string _serviceStateText = "Checking…";
    private string _serviceDetailText = "";
    private bool _serviceReachable;

    public DashboardViewModel(AppServices services)
    {
        _services = services;
        ServiceCommands = new ServiceCommands(services, RefreshAsync);
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
        RunJobCommand = new AsyncRelayCommand(p => RunJobAsync(p as JobStatusRow));
        ReloadServiceConfigCommand = new AsyncRelayCommand(_ => ReloadServiceConfigAsync());

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
    }

    public ServiceCommands ServiceCommands { get; }
    public ICommand RefreshCommand { get; }
    public ICommand RunJobCommand { get; }
    public ICommand ReloadServiceConfigCommand { get; }

    public ObservableCollection<JobStatusRow> Jobs { get; } = new();

    public string ServiceStateText
    {
        get => _serviceStateText;
        private set => Set(ref _serviceStateText, value);
    }

    public string ServiceDetailText
    {
        get => _serviceDetailText;
        private set => Set(ref _serviceDetailText, value);
    }

    public bool ServiceReachable
    {
        get => _serviceReachable;
        private set => Set(ref _serviceReachable, value);
    }

    public void Activated() => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_refreshing)
            return;
        _refreshing = true;
        try
        {
            var installState = _services.ServiceManager.GetState();

            ServiceStatusInfo? status = null;
            try
            {
                status = await _services.Ipc.GetStatusAsync();
            }
            catch
            {
                // Service not reachable — fall back to config-only display below.
            }

            ServiceReachable = status is not null;
            ServiceStateText = (installState, status) switch
            {
                (_, not null) => "Service running",
                (ServiceInstallState.NotInstalled, _) => "Service not installed",
                (ServiceInstallState.Running, null) => "Service running, IPC unavailable",
                (ServiceInstallState.Stopped, _) => "Service stopped",
                _ => "Service state changing…",
            };

            if (status is not null)
            {
                var uptime = DateTimeOffset.UtcNow - status.StartedUtc;
                ServiceDetailText = $"v{status.ServiceVersion}, pid {status.ProcessId}, up {FormatUptime(uptime)}"
                    + (status.ConfigError is { } err ? $" — CONFIG ERROR: {err}" : "");
                UpdateRows(status);
                DetectNewFailures(status);
            }
            else
            {
                ServiceDetailText = installState == ServiceInstallState.NotInstalled
                    ? "Install the service from Settings, or run jobs manually from Backup Jobs."
                    : "Start the service to resume scheduled backups.";
                UpdateRowsFromConfig();
            }
        }
        finally
        {
            _firstRefreshDone = true;
            _refreshing = false;
        }
    }

    private void UpdateRows(ServiceStatusInfo status)
    {
        Jobs.Clear();
        foreach (var job in status.Jobs)
        {
            Jobs.Add(new JobStatusRow
            {
                JobId = job.JobId,
                Name = job.Name,
                IsRunning = job.IsRunning,
                StateText = job.IsRunning ? "Running…"
                    : !job.Enabled ? "Disabled"
                    : job.ScheduleError is not null ? "Schedule error"
                    : "Scheduled",
                NextRunText = job.IsRunning ? "—"
                    : job.ScheduleError ?? (job.NextRunUtc?.ToLocalTime().ToString("ddd dd MMM HH:mm") ?? "—"),
                LastRunText = job.LastRunUtc?.ToLocalTime().ToString("dd MMM HH:mm") ?? "never",
                LastResultText = job.LastRunSummary ?? "—",
                LastRunSuccess = job.LastRunSuccess,
            });
        }
    }

    private void UpdateRowsFromConfig()
    {
        Jobs.Clear();
        try
        {
            foreach (var job in _services.ConfigStore.Load().Jobs)
            {
                Jobs.Add(new JobStatusRow
                {
                    JobId = job.Id,
                    Name = job.Name,
                    StateText = job.Enabled ? "Waiting for service" : "Disabled",
                    NextRunText = job.Schedule.Describe(),
                    LastRunText = "—",
                    LastResultText = "—",
                });
            }
        }
        catch (Exception ex)
        {
            ServiceDetailText = $"Config unreadable: {ex.Message}";
        }
    }

    private void DetectNewFailures(ServiceStatusInfo status)
    {
        foreach (var job in status.Jobs)
        {
            var known = _seenLastRuns.TryGetValue(job.JobId, out var seen);
            _seenLastRuns[job.JobId] = job.LastRunUtc;

            // Only balloon for failures that appeared while the app was open.
            if (_firstRefreshDone && known && job.LastRunUtc != seen && job.LastRunSuccess == false)
                AppEvents.RaiseBackupFailure($"Job '{job.Name}': {job.LastRunSummary}");
        }
    }

    private async Task RunJobAsync(JobStatusRow? row)
    {
        if (row is null)
            return;
        var response = await _services.Ipc.RunJobAsync(row.JobId);
        if (!response.Accepted)
        {
            MessageBox.Show(response.Reason ?? "The job was not started.", "SQL Server Backup",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        await Task.Delay(300);
        await RefreshAsync();
    }

    private async Task ReloadServiceConfigAsync()
    {
        await _services.Ipc.ReloadConfigAsync();
        await RefreshAsync();
    }

    private static string FormatUptime(TimeSpan uptime) =>
        uptime.TotalDays >= 1 ? $"{(int)uptime.TotalDays}d {uptime.Hours}h"
        : uptime.TotalHours >= 1 ? $"{(int)uptime.TotalHours}h {uptime.Minutes}m"
        : $"{Math.Max(0, (int)uptime.TotalMinutes)}m";
}
