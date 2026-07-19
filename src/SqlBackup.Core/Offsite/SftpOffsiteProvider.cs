using Renci.SshNet;

namespace SqlBackup.Core.Offsite;

/// <summary>SFTP server (password authentication). SSH.NET's sync API is wrapped in Task.Run.</summary>
public sealed class SftpOffsiteProvider : IOffsiteProvider
{
    private readonly SftpClient _client;
    private readonly string _rootPath;
    private readonly string _describe;

    public SftpOffsiteProvider(string host, int port, string username, string password, string rootPath)
    {
        _client = new SftpClient(host, port, username, password);
        _rootPath = "/" + rootPath.Trim('/');
        _describe = $"sftp://{username}@{host}:{port}{_rootPath}";
    }

    public string Describe() => _describe;

    private void EnsureConnected()
    {
        if (!_client.IsConnected)
            _client.Connect();
    }

    private string FullPath(string key) =>
        key.Length == 0 ? _rootPath : $"{_rootPath}/{key.Trim('/')}";

    public Task TestAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        EnsureConnected();
        EnsureDirectories(_rootPath);
        _client.ListDirectory(_rootPath);
    }, ct);

    public Task UploadAsync(string localFilePath, string remoteKey, CancellationToken ct = default) => Task.Run(() =>
    {
        EnsureConnected();
        var remotePath = FullPath(remoteKey);
        var directory = remotePath[..remotePath.LastIndexOf('/')];
        EnsureDirectories(directory);
        using var stream = File.OpenRead(localFilePath);
        _client.UploadFile(stream, remotePath, canOverride: true);
    }, ct);

    public Task<IReadOnlyList<string>> ListKeysAsync(string keyPrefix, CancellationToken ct = default) => Task.Run<IReadOnlyList<string>>(() =>
    {
        EnsureConnected();
        var folder = FullPath(keyPrefix);
        if (!_client.Exists(folder))
            return Array.Empty<string>();
        var prefix = keyPrefix.Trim('/');
        return _client.ListDirectory(folder)
            .Where(e => e.IsRegularFile)
            .Select(e => prefix.Length == 0 ? e.Name : $"{prefix}/{e.Name}")
            .ToList();
    }, ct);

    public Task DeleteAsync(string remoteKey, CancellationToken ct = default) => Task.Run(() =>
    {
        EnsureConnected();
        _client.DeleteFile(FullPath(remoteKey));
    }, ct);

    private void EnsureDirectories(string absolutePath)
    {
        var current = "";
        foreach (var part in absolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current += "/" + part;
            if (!_client.Exists(current))
                _client.CreateDirectory(current);
        }
    }

    public void Dispose()
    {
        try
        {
            if (_client.IsConnected)
                _client.Disconnect();
        }
        catch
        {
            // Disconnect is best effort.
        }
        _client.Dispose();
    }
}
