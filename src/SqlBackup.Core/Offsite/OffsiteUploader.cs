using Microsoft.Extensions.Logging;
using SqlBackup.Core.Backup;
using SqlBackup.Core.Models;

namespace SqlBackup.Core.Offsite;

public sealed record OffsiteResult(bool Success, IReadOnlyList<string> Notes);

/// <summary>
/// Uploads a finished backup file to an off-site provider (with retry) and applies
/// the job's off-site retention there, using the same naming-pattern safety and
/// chain awareness as local retention.
/// </summary>
public sealed class OffsiteUploader
{
    private static readonly TimeSpan[] DefaultRetryDelays = { TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) };

    private readonly IOffsiteProvider _provider;
    private readonly ILogger _log;
    private readonly TimeSpan[] _retryDelays;

    public OffsiteUploader(IOffsiteProvider provider, ILogger log, TimeSpan[]? retryDelays = null)
    {
        _provider = provider;
        _log = log;
        _retryDelays = retryDelays ?? DefaultRetryDelays;
    }

    /// <summary>Relative remote key: [prefix/][database/]fileName with '/' separators.</summary>
    public static string BuildRemoteKey(string? prefix, string? databaseSubfolder, string fileName)
    {
        var parts = new[] { prefix, databaseSubfolder, fileName }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim('/', '\\'));
        return string.Join('/', parts);
    }

    public async Task<OffsiteResult> ProcessAsync(
        string localFilePath,
        string database,
        bool subfolderPerDatabase,
        BackupType effectiveType,
        RetentionPolicy retention,
        string? destinationPrefix,
        DateTime nowLocal,
        CancellationToken ct)
    {
        var notes = new List<string>();
        var safeDb = BackupFileNamer.SanitizeForFileName(database);
        var subfolder = subfolderPerDatabase ? safeDb : null;
        var key = BuildRemoteKey(destinationPrefix, subfolder, Path.GetFileName(localFilePath));

        var uploaded = false;
        for (var attempt = 0; !uploaded; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await _provider.UploadAsync(localFilePath, key, ct);
                uploaded = true;
                notes.Add($"Off-site: uploaded to {_provider.Describe()} as '{key}'.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (attempt >= _retryDelays.Length)
                {
                    notes.Add($"Off-site upload FAILED after {attempt + 1} attempt(s): {ex.Message}");
                    _log.LogError(ex, "Off-site upload of {File} failed", localFilePath);
                    return new OffsiteResult(false, notes);
                }
                _log.LogWarning("Off-site upload attempt {Attempt} failed: {Error}. Retrying in {Delay}s",
                    attempt + 1, ex.Message, _retryDelays[attempt].TotalSeconds);
                await Task.Delay(_retryDelays[attempt], ct);
            }
        }

        if (retention.Mode != RetentionMode.KeepAll)
        {
            try
            {
                var folderPrefix = BuildRemoteKey(destinationPrefix, subfolder, "");
                var listPrefix = folderPrefix.Length == 0 ? "" : folderPrefix + "/";
                var files = new List<RetentionFile>();
                foreach (var remoteKey in await _provider.ListKeysAsync(listPrefix, ct))
                {
                    var name = remoteKey[(remoteKey.LastIndexOf('/') + 1)..];
                    if (BackupFileNamer.TryParse(name, out var db, out var token, out var ts) &&
                        db.Equals(safeDb, StringComparison.OrdinalIgnoreCase))
                    {
                        files.Add(new RetentionFile(remoteKey, token, ts));
                    }
                }

                var (doomed, keptForChain) = RetentionEnforcer.Plan(files, effectiveType, retention, nowLocal);
                var deleted = 0;
                foreach (var file in doomed)
                {
                    try
                    {
                        await _provider.DeleteAsync(file.Key, ct);
                        deleted++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        notes.Add($"Off-site retention error ({file.Key}): {ex.Message}");
                    }
                }
                if (deleted > 0)
                    notes.Add($"Off-site retention: deleted {deleted} old file(s).");
                if (keptForChain.Count > 0)
                    notes.Add($"Off-site retention: kept {keptForChain.Count} full backup(s) still needed by newer differential/log backups.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                notes.Add($"Off-site retention skipped: {ex.Message}");
            }
        }

        return new OffsiteResult(true, notes);
    }
}
