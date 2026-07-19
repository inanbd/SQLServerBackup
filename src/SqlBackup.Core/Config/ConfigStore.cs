using System.Text.Json;
using SqlBackup.Core.Models;

namespace SqlBackup.Core.Config;

/// <summary>
/// Loads and saves the shared JSON configuration file. Writes are atomic
/// (temp file + rename) and reads retry briefly so the service and the app
/// can share the file without a coordinator.
/// </summary>
public sealed class ConfigStore
{
    private readonly object _gate = new();

    public ConfigStore(string? path = null) => FilePath = path ?? AppPaths.ConfigFile;

    public string FilePath { get; }

    public AppConfig Load()
    {
        if (!File.Exists(FilePath))
            return new AppConfig();

        IOException? last = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<AppConfig>(json, JsonDefaults.Pretty)
                       ?? throw new InvalidDataException($"Config file '{FilePath}' is empty.");
            }
            catch (IOException ex)
            {
                last = ex;
                Thread.Sleep(100 * (attempt + 1));
            }
        }
        throw last!;
    }

    public void Save(AppConfig config)
    {
        lock (_gate)
        {
            config.ModifiedUtc = DateTimeOffset.UtcNow;
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var tmp = FilePath + "." + Environment.ProcessId + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(config, JsonDefaults.Pretty));
            File.Move(tmp, FilePath, overwrite: true);
        }
    }

    /// <summary>Load, mutate, save under the store lock. Returns the saved config.</summary>
    public AppConfig Update(Action<AppConfig> mutate)
    {
        lock (_gate)
        {
            var config = Load();
            mutate(config);
            Save(config);
            return config;
        }
    }

    public DateTimeOffset GetLastWriteUtc() =>
        File.Exists(FilePath) ? new DateTimeOffset(File.GetLastWriteTimeUtc(FilePath), TimeSpan.Zero) : DateTimeOffset.MinValue;
}
