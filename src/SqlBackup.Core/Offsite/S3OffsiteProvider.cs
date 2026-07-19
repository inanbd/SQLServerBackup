using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;

namespace SqlBackup.Core.Offsite;

/// <summary>Amazon S3 or any S3-compatible endpoint (MinIO, Backblaze B2, ...).</summary>
public sealed class S3OffsiteProvider : IOffsiteProvider
{
    private readonly AmazonS3Client _client;
    private readonly string _bucket;
    private readonly string _describe;

    public S3OffsiteProvider(
        string accessKeyId, string secretKey, string bucket,
        string? region, string? serviceUrl, bool forcePathStyle)
    {
        var config = new AmazonS3Config();
        if (!string.IsNullOrWhiteSpace(serviceUrl))
        {
            config.ServiceURL = serviceUrl;
            config.ForcePathStyle = forcePathStyle;
        }
        else
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(region);
        }
        _client = new AmazonS3Client(accessKeyId, secretKey, config);
        _bucket = bucket;
        _describe = string.IsNullOrWhiteSpace(serviceUrl) ? $"s3://{bucket} ({region})" : $"{serviceUrl}/{bucket}";
    }

    public string Describe() => _describe;

    public async Task TestAsync(CancellationToken ct = default) =>
        await _client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = _bucket, MaxKeys = 1 }, ct);

    public async Task UploadAsync(string localFilePath, string remoteKey, CancellationToken ct = default)
    {
        using var transfer = new TransferUtility(_client);
        await transfer.UploadAsync(localFilePath, _bucket, remoteKey, ct);
    }

    public async Task<IReadOnlyList<string>> ListKeysAsync(string keyPrefix, CancellationToken ct = default)
    {
        var keys = new List<string>();
        var request = new ListObjectsV2Request { BucketName = _bucket, Prefix = keyPrefix };
        ListObjectsV2Response response;
        do
        {
            response = await _client.ListObjectsV2Async(request, ct);
            keys.AddRange(response.S3Objects.Select(o => o.Key));
            request.ContinuationToken = response.NextContinuationToken;
        } while (response.IsTruncated);
        return keys;
    }

    public async Task DeleteAsync(string remoteKey, CancellationToken ct = default) =>
        await _client.DeleteObjectAsync(_bucket, remoteKey, ct);

    public void Dispose() => _client.Dispose();
}
