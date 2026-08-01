using System.Collections.Concurrent;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace SqlBackup.Core.Offsite;

/// <summary>
/// Google Drive as an off-site target. Drive has no real paths — folders are
/// objects with IDs — so the uploader's "prefix/database/file.bak" keys are
/// materialized as nested folders under the configured target folder, with the
/// resolved IDs cached for the lifetime of the provider.
///
/// Shared Drives are supported (all calls set SupportsAllDrives), which is what
/// service accounts need: a service account has no Drive storage of its own.
/// </summary>
public sealed class GoogleDriveOffsiteProvider : IOffsiteProvider
{
    internal const string FolderMimeType = "application/vnd.google-apps.folder";
    private const string UploadMimeType = "application/octet-stream";

    private readonly DriveService _service;
    private readonly string _rootFolderId;
    private readonly string _describe;
    private readonly ConcurrentDictionary<string, string> _folderIds = new(StringComparer.OrdinalIgnoreCase);

    public GoogleDriveOffsiteProvider(ICredential credential, string rootFolderId, string describe)
    {
        _service = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "SqlBackup",
        });
        _rootFolderId = rootFolderId;
        _describe = describe;
    }

    public string Describe() => _describe;

    /// <summary>Drive query literals are single-quoted; backslash-escape the specials.</summary>
    internal static string EscapeQueryValue(string value) =>
        value.Replace("\\", "\\\\").Replace("'", "\\'");

    /// <summary>"a/b/file.bak" -> ("a/b", "file.bak"); "file.bak" -> ("", "file.bak").</summary>
    internal static (string FolderPath, string FileName) SplitKey(string remoteKey)
    {
        var trimmed = remoteKey.Trim('/');
        var slash = trimmed.LastIndexOf('/');
        return slash < 0 ? ("", trimmed) : (trimmed[..slash], trimmed[(slash + 1)..]);
    }

    public async Task TestAsync(CancellationToken ct = default)
    {
        var request = _service.Files.Get(_rootFolderId);
        request.Fields = "id, name, mimeType";
        request.SupportsAllDrives = true;
        var folder = await request.ExecuteAsync(ct);

        if (folder.MimeType != FolderMimeType)
            throw new InvalidOperationException(
                $"'{folder.Name}' is not a folder — use the ID of a Drive folder (the last part of its URL).");
    }

    public async Task UploadAsync(string localFilePath, string remoteKey, CancellationToken ct = default)
    {
        var (folderPath, fileName) = SplitKey(remoteKey);
        var parentId = await ResolveFolderAsync(folderPath, create: true, ct)
                       ?? throw new InvalidOperationException($"Could not create the Drive folder '{folderPath}'.");
        var existingId = await FindFileIdAsync(parentId, fileName, ct);

        await using var stream = File.OpenRead(localFilePath);
        IUploadProgress progress;
        if (existingId is null)
        {
            var request = _service.Files.Create(
                new DriveFile { Name = fileName, Parents = new[] { parentId } }, stream, UploadMimeType);
            request.SupportsAllDrives = true;
            request.Fields = "id";
            progress = await request.UploadAsync(ct);
        }
        else
        {
            // Update keeps the file in place: parents must not be set on the body.
            var request = _service.Files.Update(new DriveFile { Name = fileName }, existingId, stream, UploadMimeType);
            request.SupportsAllDrives = true;
            request.Fields = "id";
            progress = await request.UploadAsync(ct);
        }

        if (progress.Status != UploadStatus.Completed)
        {
            throw progress.Exception
                  ?? new IOException($"The Drive upload of '{fileName}' ended with status {progress.Status}.");
        }
    }

    public async Task<IReadOnlyList<string>> ListKeysAsync(string keyPrefix, CancellationToken ct = default)
    {
        var folderPath = keyPrefix.Trim('/');
        var folderId = await ResolveFolderAsync(folderPath, create: false, ct);
        if (folderId is null)
            return Array.Empty<string>();

        var keys = new List<string>();
        string? pageToken = null;
        do
        {
            var request = _service.Files.List();
            request.Q = $"'{EscapeQueryValue(folderId)}' in parents and trashed = false and mimeType != '{FolderMimeType}'";
            request.Fields = "nextPageToken, files(id, name)";
            request.PageSize = 200;
            request.PageToken = pageToken;
            request.SupportsAllDrives = true;
            request.IncludeItemsFromAllDrives = true;

            var response = await request.ExecuteAsync(ct);
            foreach (var file in response.Files)
                keys.Add(folderPath.Length == 0 ? file.Name : $"{folderPath}/{file.Name}");
            pageToken = response.NextPageToken;
        } while (pageToken is not null);

        return keys;
    }

    public async Task DeleteAsync(string remoteKey, CancellationToken ct = default)
    {
        var (folderPath, fileName) = SplitKey(remoteKey);
        var folderId = await ResolveFolderAsync(folderPath, create: false, ct);
        if (folderId is null)
            return;

        var fileId = await FindFileIdAsync(folderId, fileName, ct);
        if (fileId is null)
            return;

        var request = _service.Files.Delete(fileId);
        request.SupportsAllDrives = true;
        await request.ExecuteAsync(ct);
    }

    private async Task<string?> FindFileIdAsync(string parentId, string fileName, CancellationToken ct)
    {
        var request = _service.Files.List();
        request.Q = $"name = '{EscapeQueryValue(fileName)}' and '{EscapeQueryValue(parentId)}' in parents " +
                    $"and trashed = false and mimeType != '{FolderMimeType}'";
        request.Fields = "files(id)";
        request.PageSize = 1;
        request.SupportsAllDrives = true;
        request.IncludeItemsFromAllDrives = true;

        var response = await request.ExecuteAsync(ct);
        return response.Files.FirstOrDefault()?.Id;
    }

    /// <summary>Walks (and optionally creates) the folder chain below the configured target folder.</summary>
    private async Task<string?> ResolveFolderAsync(string folderPath, bool create, CancellationToken ct)
    {
        if (folderPath.Length == 0)
            return _rootFolderId;
        if (_folderIds.TryGetValue(folderPath, out var cached))
            return cached;

        var currentId = _rootFolderId;
        var walked = "";
        foreach (var segment in folderPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            walked = walked.Length == 0 ? segment : $"{walked}/{segment}";
            if (_folderIds.TryGetValue(walked, out var known))
            {
                currentId = known;
                continue;
            }

            var found = await FindFolderIdAsync(currentId, segment, ct);
            if (found is null)
            {
                if (!create)
                    return null;
                found = await CreateFolderAsync(currentId, segment, ct);
            }

            _folderIds[walked] = found;
            currentId = found;
        }

        return currentId;
    }

    private async Task<string?> FindFolderIdAsync(string parentId, string name, CancellationToken ct)
    {
        var request = _service.Files.List();
        request.Q = $"name = '{EscapeQueryValue(name)}' and '{EscapeQueryValue(parentId)}' in parents " +
                    $"and mimeType = '{FolderMimeType}' and trashed = false";
        request.Fields = "files(id)";
        request.PageSize = 1;
        request.SupportsAllDrives = true;
        request.IncludeItemsFromAllDrives = true;

        var response = await request.ExecuteAsync(ct);
        return response.Files.FirstOrDefault()?.Id;
    }

    private async Task<string> CreateFolderAsync(string parentId, string name, CancellationToken ct)
    {
        var request = _service.Files.Create(new DriveFile
        {
            Name = name,
            MimeType = FolderMimeType,
            Parents = new[] { parentId },
        });
        request.Fields = "id";
        request.SupportsAllDrives = true;
        var created = await request.ExecuteAsync(ct);
        return created.Id;
    }

    public void Dispose() => _service.Dispose();
}
