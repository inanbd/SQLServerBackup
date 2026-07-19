using Microsoft.Extensions.Logging;

namespace SqlBackup.Core.Logging;

/// <summary>
/// Minimal dependency-free file logger: one file per day
/// (&lt;prefix&gt;-yyyyMMdd.log), old files removed after a retention window.
/// </summary>
public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private readonly object _gate = new();
    private readonly string _dir;
    private readonly string _prefix;
    private readonly LogLevel _minLevel;
    private readonly int _retainDays;
    private DateTime _currentDate;
    private string? _currentPath;

    public RollingFileLoggerProvider(string dir, string filePrefix, LogLevel minLevel, int retainDays)
    {
        _dir = dir;
        _prefix = filePrefix;
        _minLevel = minLevel;
        _retainDays = Math.Max(1, retainDays);
    }

    public static LogLevel ParseLevel(string? name) =>
        Enum.TryParse<LogLevel>(name, ignoreCase: true, out var level) ? level : LogLevel.Information;

    public ILogger CreateLogger(string categoryName) => new RollingFileLogger(this, categoryName);

    public void Dispose()
    {
    }

    internal bool IsEnabled(LogLevel level) => level >= _minLevel && level != LogLevel.None;

    internal void Write(LogLevel level, string category, string message, Exception? exception)
    {
        if (!IsEnabled(level))
            return;

        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Abbreviate(level)}] {category}: {message}" +
                   (exception is null ? "" : Environment.NewLine + exception);

        lock (_gate)
        {
            try
            {
                var today = DateTime.Today;
                if (_currentPath is null || _currentDate != today)
                {
                    Directory.CreateDirectory(_dir);
                    _currentDate = today;
                    _currentPath = Path.Combine(_dir, $"{_prefix}-{today:yyyyMMdd}.log");
                    CleanupOldFiles(today);
                }
                File.AppendAllText(_currentPath, line + Environment.NewLine);
            }
            catch
            {
                // Logging must never take the host down.
            }
        }
    }

    private void CleanupOldFiles(DateTime today)
    {
        try
        {
            var cutoff = today.AddDays(-_retainDays);
            foreach (var file in Directory.EnumerateFiles(_dir, $"{_prefix}-*.log"))
            {
                var stem = Path.GetFileNameWithoutExtension(file);
                var datePart = stem[(_prefix.Length + 1)..];
                if (DateTime.TryParseExact(datePart, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date) &&
                    date < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // Best effort.
        }
    }

    private static string Abbreviate(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };

    private sealed class RollingFileLogger : ILogger
    {
        private readonly RollingFileLoggerProvider _provider;
        private readonly string _category;

        public RollingFileLogger(RollingFileLoggerProvider provider, string category)
        {
            _provider = provider;
            // Trim namespaces down to the type name for readable lines.
            var lastDot = category.LastIndexOf('.');
            _category = lastDot >= 0 ? category[(lastDot + 1)..] : category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => _provider.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            _provider.Write(logLevel, _category, formatter(state, exception), exception);
    }
}
