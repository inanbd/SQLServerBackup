namespace SqlBackup.Core.Backup;

public sealed record PreflightIssue(bool IsFatal, string Message);

/// <summary>
/// Best-effort checks before running a backup. Destination problems are reported
/// as warnings, not failures: BACKUP ... TO DISK is executed by the SQL Server
/// engine on the SQL Server host, so a path that is not visible from this
/// machine can still be perfectly valid for the server (and vice versa).
/// </summary>
public static class PreflightChecker
{
    public static List<PreflightIssue> CheckDestination(string folder, long? expectedBytes, int minFreeMb)
    {
        var issues = new List<PreflightIssue>();
        if (string.IsNullOrWhiteSpace(folder))
        {
            issues.Add(new PreflightIssue(true, "Destination folder is not set."));
            return issues;
        }

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            issues.Add(new PreflightIssue(false,
                $"Destination '{folder}' could not be created from this machine ({ex.Message}). " +
                "The backup will still be attempted — the path is resolved by the SQL Server host."));
            return issues;
        }

        // Probe write access with a throwaway file.
        try
        {
            var probe = Path.Combine(folder, $".sqlbackup-writetest-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "probe");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            issues.Add(new PreflightIssue(false,
                $"Destination '{folder}' is not writable from this machine ({ex.Message})."));
        }

        // Disk space check only works for paths this machine can resolve to a drive.
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(folder));
            if (!string.IsNullOrEmpty(root) && !root.StartsWith(@"\\", StringComparison.Ordinal))
            {
                var free = new DriveInfo(root).AvailableFreeSpace;
                var required = Math.Max(expectedBytes ?? 0, (long)minFreeMb * 1024 * 1024);
                if (free < required)
                {
                    issues.Add(new PreflightIssue(false,
                        $"Low disk space on '{root}': {free / (1024 * 1024)} MB free, " +
                        $"~{required / (1024 * 1024)} MB expected/required."));
                }
            }
        }
        catch
        {
            // Unknown drive layout — skip the space check.
        }

        return issues;
    }
}
