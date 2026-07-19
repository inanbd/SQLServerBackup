namespace SqlBackup.Core.Tests;

public sealed class TempDirectory : IDisposable
{
    public TempDirectory() => Path = Directory.CreateTempSubdirectory("sqlbackup-test-").FullName;

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // Best effort.
        }
    }
}
