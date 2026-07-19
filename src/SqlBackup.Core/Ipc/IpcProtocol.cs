using System.Text;
using System.Text.Json;
using SqlBackup.Core.Config;
using SqlBackup.Core.Models;

namespace SqlBackup.Core.Ipc;

/// <summary>
/// IPC contract between the desktop app and the service: a local named pipe,
/// one newline-delimited UTF-8 JSON request per connection, one JSON response.
/// See docs/ipc-contract.md for the full message reference.
/// </summary>
public static class IpcProtocol
{
    public const string DefaultPipeName = "SqlBackup.Control.v1";
    public const string PipeNameEnvVar = "SQLBACKUP_PIPE_NAME";

    /// <summary>Pipe name, overridable via environment for tests/parallel instances.</summary>
    public static string PipeName =>
        Environment.GetEnvironmentVariable(PipeNameEnvVar) is { Length: > 0 } overridden
            ? overridden
            : DefaultPipeName;

    public static class MessageTypes
    {
        public const string Ping = "ping";
        public const string GetStatus = "get-status";
        public const string GetHistory = "get-history";
        public const string RunJob = "run-job";
        public const string ReloadConfig = "reload-config";
    }
}

public sealed class IpcRequest
{
    public string Type { get; set; } = "";
    public JsonElement? Payload { get; set; }

    public static IpcRequest Create(string type, object? payload = null) => new()
    {
        Type = type,
        Payload = payload is null ? null : JsonSerializer.SerializeToElement(payload, JsonDefaults.Compact),
    };

    public T? PayloadAs<T>() =>
        Payload is { } p ? p.Deserialize<T>(JsonDefaults.Compact) : default;
}

public sealed class IpcResponse
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public JsonElement? Payload { get; set; }

    public static IpcResponse Success(object? payload = null) => new()
    {
        Ok = true,
        Payload = payload is null ? null : JsonSerializer.SerializeToElement(payload, JsonDefaults.Compact),
    };

    public static IpcResponse Fail(string error) => new() { Ok = false, Error = error };

    public T? PayloadAs<T>() =>
        Payload is { } p ? p.Deserialize<T>(JsonDefaults.Compact) : default;
}

// Typed payloads.

public sealed class PingResponse
{
    public string Version { get; set; } = "";
    public int ProcessId { get; set; }
}

public sealed class GetHistoryRequest
{
    public int MaxEntries { get; set; } = 200;
    public Guid? JobId { get; set; }
}

public sealed class GetHistoryResponse
{
    public List<JobHistoryEntry> Entries { get; set; } = new();
}

public sealed class RunJobRequest
{
    public Guid JobId { get; set; }
}

public sealed class RunJobResponse
{
    public bool Accepted { get; set; }
    public string? Reason { get; set; }
}

public sealed class ReloadConfigResponse
{
    public DateTimeOffset ConfigModifiedUtc { get; set; }
}

/// <summary>Newline-delimited framing helpers shared by client and server.</summary>
public static class IpcFraming
{
    public static async Task WriteLineAsync(Stream stream, string line, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
    }

    public static async Task<string?> ReadLineAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
            bufferSize: 8192, leaveOpen: true);
        return await reader.ReadLineAsync(ct);
    }
}
