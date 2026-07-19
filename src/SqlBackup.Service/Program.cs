using SqlBackup.Core;
using SqlBackup.Core.Backup;
using SqlBackup.Core.Config;
using SqlBackup.Core.History;
using SqlBackup.Core.Logging;
using SqlBackup.Core.Security;

namespace SqlBackup.Service;

public static class Program
{
    public static async Task Main(string[] args)
    {
        using var host = BuildHost(args);
        await host.RunAsync();
    }

    /// <summary>Composes the service host. Public so integration tests can run the real wiring in-process.</summary>
    public static IHost BuildHost(string[] args)
    {
        AppPaths.EnsureDirectories();

        // Read the config once, outside DI, to configure logging before the host starts.
        var configStore = new ConfigStore();
        var serviceSettings = TryLoadServiceSettings(configStore);

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Services.AddWindowsService(options => options.ServiceName = ServiceConstants.ServiceName);
        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(40));

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
        builder.Logging.AddProvider(new RollingFileLoggerProvider(
            AppPaths.LogDir, "service",
            RollingFileLoggerProvider.ParseLevel(serviceSettings.MinimumLogLevel),
            serviceSettings.LogRetentionDays));

        builder.Services.AddSingleton(configStore);
        builder.Services.AddSingleton(new HistoryStore());
        builder.Services.AddSingleton<ISecretProtector, SecretProtector>();
        builder.Services.AddSingleton<ServiceState>();
        builder.Services.AddSingleton(provider => new JobRunner(
            provider.GetRequiredService<ILoggerFactory>().CreateLogger<JobRunner>(),
            provider.GetRequiredService<HistoryStore>(),
            provider.GetRequiredService<ISecretProtector>()));

        builder.Services.AddSingleton<SchedulerWorker>();
        builder.Services.AddHostedService(provider => provider.GetRequiredService<SchedulerWorker>());
        builder.Services.AddHostedService<IpcServer>();

        return builder.Build();
    }

    private static Core.Models.ServiceSettings TryLoadServiceSettings(ConfigStore store)
    {
        try
        {
            return store.Load().Service;
        }
        catch
        {
            return new Core.Models.ServiceSettings();
        }
    }
}
