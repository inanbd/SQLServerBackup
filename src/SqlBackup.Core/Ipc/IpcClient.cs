using System.IO.Pipes;
using System.Text.Json;
using SqlBackup.Core.Config;
using SqlBackup.Core.Models;

namespace SqlBackup.Core.Ipc;

public sealed class IpcException : Exception
{
    public IpcException(string message) : base(message) { }
    public IpcException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Desktop-app side of the IPC channel. One connection per request.</summary>
public sealed class IpcClient
{
    private readonly string _pipeName;

    public IpcClient(string? pipeName = null) => _pipeName = pipeName ?? IpcProtocol.PipeName;

    public async Task<IpcResponse> SendAsync(
        string type,
        object? payload = null,
        int timeoutMs = 10_000,
        CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        try
        {
            await using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(cts.Token);

            var request = JsonSerializer.Serialize(IpcRequest.Create(type, payload), JsonDefaults.Compact);
            await IpcFraming.WriteLineAsync(pipe, request, cts.Token);

            var line = await IpcFraming.ReadLineAsync(pipe, cts.Token)
                       ?? throw new IpcException("The service closed the connection without responding.");
            return JsonSerializer.Deserialize<IpcResponse>(line, JsonDefaults.Compact)
                   ?? throw new IpcException("Empty response from the service.");
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new IpcException($"The backup service did not respond within {timeoutMs} ms. Is it running?", ex);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            throw new IpcException($"Could not reach the backup service: {ex.Message}", ex);
        }
    }

    private async Task<T> RequestAsync<T>(string type, object? payload = null, int timeoutMs = 10_000, CancellationToken ct = default)
    {
        var response = await SendAsync(type, payload, timeoutMs, ct);
        if (!response.Ok)
            throw new IpcException(response.Error ?? "The service reported an unknown error.");
        return response.PayloadAs<T>() ?? throw new IpcException("The service response had no payload.");
    }

    public Task<PingResponse> PingAsync(int timeoutMs = 3_000, CancellationToken ct = default) =>
        RequestAsync<PingResponse>(IpcProtocol.MessageTypes.Ping, null, timeoutMs, ct);

    public Task<ServiceStatusInfo> GetStatusAsync(CancellationToken ct = default) =>
        RequestAsync<ServiceStatusInfo>(IpcProtocol.MessageTypes.GetStatus, null, 10_000, ct);

    public async Task<IReadOnlyList<JobHistoryEntry>> GetHistoryAsync(int maxEntries = 200, Guid? jobId = null, CancellationToken ct = default)
    {
        var response = await RequestAsync<GetHistoryResponse>(
            IpcProtocol.MessageTypes.GetHistory, new GetHistoryRequest { MaxEntries = maxEntries, JobId = jobId }, 15_000, ct);
        return response.Entries;
    }

    public Task<RunJobResponse> RunJobAsync(Guid jobId, CancellationToken ct = default) =>
        RequestAsync<RunJobResponse>(IpcProtocol.MessageTypes.RunJob, new RunJobRequest { JobId = jobId }, 10_000, ct);

    public Task<ReloadConfigResponse> ReloadConfigAsync(CancellationToken ct = default) =>
        RequestAsync<ReloadConfigResponse>(IpcProtocol.MessageTypes.ReloadConfig, null, 10_000, ct);

    public async Task<bool> IsServiceReachableAsync(int timeoutMs = 1_500)
    {
        try
        {
            await PingAsync(timeoutMs);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
