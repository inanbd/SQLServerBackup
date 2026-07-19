using System;
using System.Threading.Tasks;
using SqlBackup.Core.Backup;
using SqlBackup.Core.Models;

namespace SqlBackup.App.Infrastructure;

/// <summary>
/// Runs a backup job directly inside the desktop app (no service involved).
/// Shares the exact same JobRunner code path as the service, so results land in
/// the same history store, marked with the ManualStandalone trigger.
/// </summary>
public sealed class StandaloneRunner
{
    private readonly AppServices _services;

    public StandaloneRunner(AppServices services) => _services = services;

    public async Task<JobRunResult> RunAsync(Guid jobId)
    {
        var config = _services.ConfigStore.Load();
        var job = config.FindJob(jobId)
                  ?? throw new InvalidOperationException("The job no longer exists — refresh the list.");
        var connection = config.FindConnection(job.ConnectionId)
                         ?? throw new InvalidOperationException(
                             $"Job '{job.Name}' references a connection profile that no longer exists.");

        var runner = new JobRunner(_services.CreateLogger("StandaloneRun"), _services.History, _services.Protector);
        return await runner.RunAsync(new JobRunContext
        {
            Job = job,
            Connection = connection,
            Trigger = RunTrigger.ManualStandalone,
            Settings = config.Service,
            Notifications = config.Notifications,
        });
    }
}
