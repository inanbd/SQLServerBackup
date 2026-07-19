using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SqlBackup.Core;
using SqlBackup.Core.Config;
using SqlBackup.Core.History;
using SqlBackup.Core.Ipc;
using SqlBackup.Core.Logging;
using SqlBackup.Core.Security;

namespace SqlBackup.App.Infrastructure;

/// <summary>Composition root for the desktop app.</summary>
public sealed class AppServices
{
    public AppServices()
    {
        LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
            builder.AddProvider(new RollingFileLoggerProvider(AppPaths.LogDir, "app", LogLevel.Information, retainDays: 14)));
    }

    public ILoggerFactory LoggerFactory { get; }
    public ConfigStore ConfigStore { get; } = new();
    public HistoryStore History { get; } = new();
    public ISecretProtector Protector { get; } = new SecretProtector();
    public IpcClient Ipc { get; } = new();
    public ServiceManager ServiceManager { get; } = new();

    public ILogger CreateLogger(string name) => LoggerFactory.CreateLogger(name);

    /// <summary>
    /// Asks the running service to pick up config.json immediately. Quietly a no-op
    /// when the service is stopped — it re-reads the file on its next poll anyway.
    /// </summary>
    public async Task<bool> TryNotifyServiceOfConfigChangeAsync()
    {
        try
        {
            await Ipc.ReloadConfigAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
