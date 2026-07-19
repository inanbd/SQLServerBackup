using System.Text.Json;
using SqlBackup.Core.Config;
using SqlBackup.Core.Models;

namespace SqlBackup.Core.History;

/// <summary>
/// Append-only JSONL store for job run history with simple size-based rotation
/// (history.jsonl -> history.1.jsonl). Both the service and the desktop app read it;
/// only backup runners write to it.
/// </summary>
public sealed class HistoryStore
{
    private readonly object _gate = new();
    private readonly string _dir;
    private readonly long _maxFileBytes;

    public HistoryStore(string? dir = null, long maxFileBytes = 5 * 1024 * 1024)
    {
        _dir = dir ?? AppPaths.HistoryDir;
        _maxFileBytes = maxFileBytes;
    }

    private string CurrentFile => Path.Combine(_dir, "history.jsonl");
    private string ArchiveFile => Path.Combine(_dir, "history.1.jsonl");

    public void Append(JobHistoryEntry entry)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_dir);
            var current = new FileInfo(CurrentFile);
            if (current.Exists && current.Length >= _maxFileBytes)
                File.Move(CurrentFile, ArchiveFile, overwrite: true);

            File.AppendAllText(CurrentFile, JsonSerializer.Serialize(entry, JsonDefaults.Compact) + Environment.NewLine);
        }
    }

    public IReadOnlyList<JobHistoryEntry> ReadRecent(int maxEntries, Guid? jobId = null)
    {
        List<JobHistoryEntry> all;
        lock (_gate)
        {
            all = ReadAllUnlocked();
        }

        IEnumerable<JobHistoryEntry> query = all;
        if (jobId is { } id)
            query = query.Where(e => e.JobId == id);
        return query.OrderByDescending(e => e.StartedUtc).Take(Math.Max(1, maxEntries)).ToList();
    }

    /// <summary>Most recent entry per job — used to restore "last run" state after a service restart.</summary>
    public Dictionary<Guid, JobHistoryEntry> GetLastEntryPerJob()
    {
        List<JobHistoryEntry> all;
        lock (_gate)
        {
            all = ReadAllUnlocked();
        }

        return all
            .GroupBy(e => e.JobId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.StartedUtc).First());
    }

    private List<JobHistoryEntry> ReadAllUnlocked()
    {
        var entries = new List<JobHistoryEntry>();
        foreach (var file in new[] { ArchiveFile, CurrentFile })
        {
            if (!File.Exists(file))
                continue;
            foreach (var line in ReadLinesSafe(file))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                try
                {
                    if (JsonSerializer.Deserialize<JobHistoryEntry>(line, JsonDefaults.Compact) is { } entry)
                        entries.Add(entry);
                }
                catch (JsonException)
                {
                    // A torn write at the tail of the file — skip the line.
                }
            }
        }
        return entries;
    }

    private static IEnumerable<string> ReadLinesSafe(string file)
    {
        // Share-friendly read so we can read while the service appends.
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
            yield return line;
    }
}
