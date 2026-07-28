namespace SqlBackup.Core.Offsite;

/// <summary>
/// A Windows file share (UNC path) as an off-site target. With no credentials the
/// share is accessed as the identity running the backup engine — the service
/// account — which is the usual setup; supplying a user establishes a dedicated
/// session for the copy instead (Windows only).
/// </summary>
public sealed class SmbOffsiteProvider : IOffsiteProvider
{
    private readonly string _root;
    private readonly string? _username;
    private readonly string? _password;
    private WindowsNetworkConnection? _connection;

    public SmbOffsiteProvider(string path, string? username, string? password)
    {
        _root = path.TrimEnd('\\', '/');
        _username = string.IsNullOrWhiteSpace(username) ? null : username;
        _password = password;
    }

    public string Describe() => _root;

    /// <summary>\\server\share\a\b -> \\server\share (the connectable unit).</summary>
    internal static string GetShareRoot(string path)
    {
        if (!path.StartsWith(@"\\", StringComparison.Ordinal))
            return path;
        var parts = path.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $@"\\{parts[0]}\{parts[1]}" : path;
    }

    private void EnsureConnected()
    {
        if (_username is null || _connection is not null)
            return;
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Connecting to a file share with explicit credentials is only supported on Windows. " +
                "Mount the share first, or leave the user name empty to use the process identity.");
        }
        _connection = new WindowsNetworkConnection(GetShareRoot(_root), _username, _password ?? "");
    }

    private string FullPath(string key) =>
        key.Length == 0 ? _root : Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar));

    public Task TestAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        EnsureConnected();
        Directory.CreateDirectory(_root);
        var probe = Path.Combine(_root, $".sqlbackup-writetest-{Guid.NewGuid():N}");
        File.WriteAllText(probe, "probe");
        File.Delete(probe);
    }, ct);

    public async Task UploadAsync(string localFilePath, string remoteKey, CancellationToken ct = default)
    {
        EnsureConnected();
        var target = FullPath(remoteKey);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        // Copy to a temp name first: a cancelled or failed copy must never leave a
        // truncated file that looks like a finished backup.
        var staging = target + ".uploading";
        try
        {
            await using (var source = new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                             bufferSize: 81920, useAsync: true))
            await using (var destination = new FileStream(staging, FileMode.Create, FileAccess.Write, FileShare.None,
                             bufferSize: 81920, useAsync: true))
            {
                await source.CopyToAsync(destination, ct);
            }
            File.Move(staging, target, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(staging);
            }
            catch
            {
                // Nothing more we can do about the partial file.
            }
            throw;
        }
    }

    public Task<IReadOnlyList<string>> ListKeysAsync(string keyPrefix, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<string>>(() =>
        {
            EnsureConnected();
            var folder = FullPath(keyPrefix);
            if (!Directory.Exists(folder))
                return Array.Empty<string>();
            var prefix = keyPrefix.Trim('/');
            return Directory.EnumerateFiles(folder)
                .Select(Path.GetFileName)
                .Where(name => name is not null)
                .Select(name => prefix.Length == 0 ? name! : $"{prefix}/{name}")
                .ToList();
        }, ct);

    public Task DeleteAsync(string remoteKey, CancellationToken ct = default) => Task.Run(() =>
    {
        EnsureConnected();
        File.Delete(FullPath(remoteKey));
    }, ct);

    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
    }
}
