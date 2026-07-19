using SqlBackup.Core.Models;

namespace SqlBackup.Core.Backup;

/// <summary>
/// Turns the most common backup permission failures into actionable guidance.
/// A backup that runs inside the Windows service authenticates as the service
/// account (LocalSystem by default), not as the interactive user who configured
/// the job — the resulting errors ("NT AUTHORITY\SYSTEM is not able to access
/// the database ...") confuse everyone exactly once, so explain them inline.
/// </summary>
public static class SqlErrorHints
{
    // SQL Server error numbers this recognizes:
    // 916   - the server principal cannot access the database under the current security context
    // 262   - BACKUP DATABASE (etc.) permission denied in database
    // 4060  - cannot open the database requested by the login
    // 18456 - login failed for user
    private static readonly int[] PermissionErrorNumbers = { 916, 262, 4060, 18456 };

    /// <summary>The Windows identity this process runs as, e.g. "NT AUTHORITY\SYSTEM".</summary>
    public static string CurrentProcessAccount =>
        $"{Environment.UserDomainName}\\{Environment.UserName}";

    public static bool IsLoginFailure(IEnumerable<int> sqlErrorNumbers) =>
        sqlErrorNumbers.Contains(18456);

    /// <summary>
    /// Returns an explanation + remedy for permission-class failures, or null for
    /// anything else (network problems, full disks, ... need no identity lecture).
    /// </summary>
    public static string? ForBackupFailure(
        IReadOnlyCollection<int> sqlErrorNumbers,
        SqlAuthMode authMode,
        string runningAs)
    {
        if (!sqlErrorNumbers.Any(PermissionErrorNumbers.Contains))
            return null;

        if (authMode == SqlAuthMode.Windows)
        {
            return $"Note: this backup ran under Windows authentication as '{runningAs}' — the account the " +
                   "backup engine runs as — not as your interactive login. Fix one of: grant that login backup " +
                   $"rights in SQL Server (per database: CREATE USER [{runningAs}] FOR LOGIN [{runningAs}]; " +
                   $"ALTER ROLE [db_backupoperator] ADD MEMBER [{runningAs}];), change the service account " +
                   "(services.msc → SQL Server Backup Service → Log On) to one that has access, or switch the " +
                   "connection profile to SQL authentication.";
        }

        return "Note: the SQL login used by this connection lacks the required backup permission. " +
               "Add it to the db_backupoperator role in each database (or grant a broader server role).";
    }
}
