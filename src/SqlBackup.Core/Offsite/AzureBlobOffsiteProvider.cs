using Azure.Storage.Blobs;

namespace SqlBackup.Core.Offsite;

/// <summary>Azure Blob Storage via a container SAS (needs create+write+list+delete rights).</summary>
public sealed class AzureBlobOffsiteProvider : IOffsiteProvider
{
    private readonly BlobContainerClient _container;
    private readonly string _describe;

    public AzureBlobOffsiteProvider(string containerUrl, string sasToken)
    {
        var builder = new UriBuilder(containerUrl) { Query = sasToken.TrimStart('?') };
        _container = new BlobContainerClient(builder.Uri);
        _describe = containerUrl;
    }

    public string Describe() => _describe;

    public async Task TestAsync(CancellationToken ct = default)
    {
        await foreach (var _ in _container.GetBlobsAsync(cancellationToken: ct))
            break; // one page proves connectivity + list permission
    }

    public async Task UploadAsync(string localFilePath, string remoteKey, CancellationToken ct = default) =>
        await _container.GetBlobClient(remoteKey).UploadAsync(localFilePath, overwrite: true, ct);

    public async Task<IReadOnlyList<string>> ListKeysAsync(string keyPrefix, CancellationToken ct = default)
    {
        var keys = new List<string>();
        await foreach (var blob in _container.GetBlobsAsync(prefix: keyPrefix, cancellationToken: ct))
            keys.Add(blob.Name);
        return keys;
    }

    public async Task DeleteAsync(string remoteKey, CancellationToken ct = default) =>
        await _container.GetBlobClient(remoteKey).DeleteIfExistsAsync(cancellationToken: ct);

    public void Dispose()
    {
    }
}
