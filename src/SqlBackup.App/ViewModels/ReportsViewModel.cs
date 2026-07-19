using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using SqlBackup.App.Infrastructure;
using SqlBackup.Core.Reporting;

namespace SqlBackup.App.ViewModels;

public sealed record TrendBar(double Height, string Tooltip);

public sealed class ReportRowVm
{
    public required DatabaseReportRow Row { get; init; }
    public required IReadOnlyList<TrendBar> Bars { get; init; }

    public string JobName => Row.JobName;
    public string Database => Row.Database;
    public string LastFullText => Row.LastFullUtc is { } t
        ? $"{t.ToLocalTime():dd MMM HH:mm}" + (Row.LastFullSizeBytes is { } s ? $" ({FormatBytes(s)})" : "")
        : "never";
    public string SuccessRateText => $"{Row.SuccessRatePercent:F0}% of {Row.RunCount}";
    public bool HasFailures => Row.SuccessRatePercent < 100;
    public string AvgDurationText => $"{Row.AvgDurationSeconds:F1} s";
    public string MaxDurationText => $"{Row.MaxDurationSeconds:F1} s";
    public string GrowthText => Row.GrowthPercent is { } g ? $"{(g >= 0 ? "+" : "")}{g:F1}%" : "—";

    internal static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):F1} GB",
        >= 1024L * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / 1024.0:F0} KB",
    };
}

public sealed class ReportsViewModel : ObservableObject, IActivatable
{
    private const double MaxBarHeight = 36;

    private readonly AppServices _services;
    private string _successRateText = "—";
    private string _runsText = "—";
    private string _footprintText = "—";
    private string _writtenText = "—";

    public ReportsViewModel(AppServices services)
    {
        _services = services;
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
    }

    public ObservableCollection<ReportRowVm> Rows { get; } = new();
    public ICommand RefreshCommand { get; }

    public string SuccessRateText
    {
        get => _successRateText;
        private set => Set(ref _successRateText, value);
    }

    public string RunsText
    {
        get => _runsText;
        private set => Set(ref _runsText, value);
    }

    public string FootprintText
    {
        get => _footprintText;
        private set => Set(ref _footprintText, value);
    }

    public string WrittenText
    {
        get => _writtenText;
        private set => Set(ref _writtenText, value);
    }

    public void Activated() => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        // Reports read the local history store directly: same file the service writes,
        // and the 30-day window can exceed the IPC page size.
        var entries = await Task.Run(() => _services.History.ReadRecent(50_000));
        var model = ReportBuilder.Build(entries, DateTimeOffset.UtcNow);

        SuccessRateText = $"{model.Summary.SuccessRatePercent:F1}%";
        RunsText = $"{model.Summary.TotalRuns} runs, {model.Summary.Failures} failed";
        FootprintText = ReportRowVm.FormatBytes(model.Summary.LatestFullTotalBytes);
        WrittenText = ReportRowVm.FormatBytes(model.Summary.BytesWritten);

        Rows.Clear();
        foreach (var row in model.Rows)
        {
            var max = Math.Max(row.DailyFullSizesMb.DefaultIfEmpty(0).Max(), 0.001);
            var today = DateTime.Today;
            var bars = row.DailyFullSizesMb.Select((mb, index) =>
            {
                var day = today.AddDays(index - (ReportBuilder.TrendDays - 1));
                return new TrendBar(
                    mb <= 0 ? 1 : Math.Max(3, mb / max * MaxBarHeight),
                    $"{day:dd MMM}: {(mb <= 0 ? "no full backup" : $"{mb:F1} MB")}");
            }).ToList();
            Rows.Add(new ReportRowVm { Row = row, Bars = bars });
        }
    }
}
