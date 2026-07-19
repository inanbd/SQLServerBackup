using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using SqlBackup.Core.Config;
using SqlBackup.Core.Ipc;

namespace SqlBackup.Service;

/// <summary>
/// Named-pipe server for the desktop app: one JSON line in, one JSON line out
/// per connection. On Windows the pipe ACL admits SYSTEM, Administrators and
/// (read/write) Authenticated Users — the pipe is local-machine only.
/// </summary>
public sealed class IpcServer : BackgroundService
{
    private readonly ServiceState _state;
    private readonly SchedulerWorker _scheduler;
    private readonly ILogger<IpcServer> _log;

    public IpcServer(ServiceState state, SchedulerWorker scheduler, ILogger<IpcServer> log)
    {
        _state = state;
        _scheduler = scheduler;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("IPC server listening on pipe '{Pipe}'", IpcProtocol.PipeName);

        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = CreateServerStream();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Could not create the IPC pipe; retrying in 5s");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                }
                continue;
            }

            try
            {
                await server.WaitForConnectionAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                await server.DisposeAsync();
                break;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "IPC accept failed");
                await server.DisposeAsync();
                continue;
            }

            _ = HandleClientAsync(server, stoppingToken);
        }

        _log.LogInformation("IPC server stopped");
    }

    private static NamedPipeServerStream CreateServerStream()
    {
        const int maxInstances = 16;

        if (OperatingSystem.IsWindows())
        {
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                PipeAccessRights.ReadWrite, AccessControlType.Allow));

            return NamedPipeServerStreamAcl.Create(
                IpcProtocol.PipeName, PipeDirection.InOut, maxInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
                inBufferSize: 0, outBufferSize: 0, security);
        }

        return new NamedPipeServerStream(
            IpcProtocol.PipeName, PipeDirection.InOut, maxInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        await using (pipe)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                var line = await IpcFraming.ReadLineAsync(pipe, cts.Token);
                if (line is null)
                    return;

                IpcResponse response;
                try
                {
                    var request = JsonSerializer.Deserialize<IpcRequest>(line, JsonDefaults.Compact);
                    response = request is null ? IpcResponse.Fail("Empty request.") : Dispatch(request);
                }
                catch (JsonException ex)
                {
                    response = IpcResponse.Fail("Malformed request: " + ex.Message);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "IPC request handling failed");
                    response = IpcResponse.Fail(ex.Message);
                }

                await IpcFraming.WriteLineAsync(pipe, JsonSerializer.Serialize(response, JsonDefaults.Compact), cts.Token);

                if (OperatingSystem.IsWindows())
                {
                    try
                    {
                        pipe.WaitForPipeDrain();
                    }
                    catch
                    {
                        // Client may already have disconnected.
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
                // Client went away mid-conversation.
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "IPC connection failed");
            }
        }
    }

    private IpcResponse Dispatch(IpcRequest request)
    {
        switch (request.Type)
        {
            case IpcProtocol.MessageTypes.Ping:
                return IpcResponse.Success(new PingResponse
                {
                    Version = typeof(IpcServer).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
                    ProcessId = Environment.ProcessId,
                });

            case IpcProtocol.MessageTypes.GetStatus:
                return IpcResponse.Success(_state.BuildStatus());

            case IpcProtocol.MessageTypes.GetHistory:
            {
                var query = request.PayloadAs<GetHistoryRequest>() ?? new GetHistoryRequest();
                var entries = _state.History.ReadRecent(Math.Clamp(query.MaxEntries, 1, 2000), query.JobId);
                return IpcResponse.Success(new GetHistoryResponse { Entries = entries.ToList() });
            }

            case IpcProtocol.MessageTypes.RunJob:
            {
                var payload = request.PayloadAs<RunJobRequest>();
                if (payload is null || payload.JobId == Guid.Empty)
                    return IpcResponse.Fail("run-job requires a jobId.");
                return IpcResponse.Success(_scheduler.TriggerManualRun(payload.JobId));
            }

            case IpcProtocol.MessageTypes.ReloadConfig:
            {
                _state.ReloadIfChanged(force: true);
                return _state.ConfigError is { } error
                    ? IpcResponse.Fail($"Config reload failed: {error}")
                    : IpcResponse.Success(new ReloadConfigResponse { ConfigModifiedUtc = _state.Config.ModifiedUtc });
            }

            default:
                return IpcResponse.Fail($"Unknown request type '{request.Type}'.");
        }
    }
}
