namespace SqlBackup.Core.Offsite;

/// <summary>
/// A storage backend for off-site backup copies. Keys are relative,
/// '/'-separated paths (e.g. "prefix/Sales/Sales_Full_20260719_020000.bak");
/// each provider maps them onto its own namespace.
/// </summary>
public interface IOffsiteProvider : IDisposable
{
    string Describe();

    /// <summary>Cheap connectivity + permission probe; throws with a useful message on failure.</summary>
    Task TestAsync(CancellationToken ct = default);

    Task UploadAsync(string localFilePath, string remoteKey, CancellationToken ct = default);

    /// <summary>All keys directly under the given folder-like prefix ("" = root).</summary>
    Task<IReadOnlyList<string>> ListKeysAsync(string keyPrefix, CancellationToken ct = default);

    Task DeleteAsync(string remoteKey, CancellationToken ct = default);
}
