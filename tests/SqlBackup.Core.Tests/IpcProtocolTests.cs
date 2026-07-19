using System.Text;
using System.Text.Json;
using SqlBackup.Core.Config;
using SqlBackup.Core.Ipc;
using SqlBackup.Core.Models;
using Xunit;

namespace SqlBackup.Core.Tests;

public class IpcProtocolTests
{
    [Fact]
    public void RequestPayload_RoundTrips()
    {
        var jobId = Guid.NewGuid();
        var request = IpcRequest.Create(IpcProtocol.MessageTypes.RunJob, new RunJobRequest { JobId = jobId });

        var wire = JsonSerializer.Serialize(request, JsonDefaults.Compact);
        var parsed = JsonSerializer.Deserialize<IpcRequest>(wire, JsonDefaults.Compact)!;

        Assert.Equal(IpcProtocol.MessageTypes.RunJob, parsed.Type);
        Assert.Equal(jobId, parsed.PayloadAs<RunJobRequest>()!.JobId);
    }

    [Fact]
    public void ResponsePayload_RoundTrips_WithEnumsAsStrings()
    {
        var status = new ServiceStatusInfo
        {
            ServiceVersion = "1.0.0",
            Jobs = { new JobStatusInfo { Name = "nightly", NextRunUtc = DateTimeOffset.UtcNow } },
        };

        var wire = JsonSerializer.Serialize(IpcResponse.Success(status), JsonDefaults.Compact);
        var parsed = JsonSerializer.Deserialize<IpcResponse>(wire, JsonDefaults.Compact)!;

        Assert.True(parsed.Ok);
        var payload = parsed.PayloadAs<ServiceStatusInfo>()!;
        Assert.Equal("nightly", Assert.Single(payload.Jobs).Name);
    }

    [Fact]
    public void FailResponse_CarriesError()
    {
        var wire = JsonSerializer.Serialize(IpcResponse.Fail("boom"), JsonDefaults.Compact);
        var parsed = JsonSerializer.Deserialize<IpcResponse>(wire, JsonDefaults.Compact)!;

        Assert.False(parsed.Ok);
        Assert.Equal("boom", parsed.Error);
    }

    [Fact]
    public async Task Framing_WritesAndReadsLines()
    {
        using var stream = new MemoryStream();
        await IpcFraming.WriteLineAsync(stream, """{"type":"ping"}""", CancellationToken.None);
        stream.Position = 0;

        var line = await IpcFraming.ReadLineAsync(stream, CancellationToken.None);

        Assert.Equal("""{"type":"ping"}""", line);
    }

    [Fact]
    public void HistoryEntries_SerializeEnumsAsStrings()
    {
        var entry = new JobHistoryEntry { Type = BackupType.TransactionLog, Trigger = RunTrigger.ManualStandalone };
        var wire = JsonSerializer.Serialize(entry, JsonDefaults.Compact);

        Assert.Contains("TransactionLog", wire);
        Assert.Contains("ManualStandalone", wire);
        Assert.DoesNotContain(Encoding.UTF8.GetString(new byte[] { (byte)'\n' }), wire); // single line for JSONL
    }
}
