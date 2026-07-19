using System.Text.Json;
using SqlBackup.Core.Config;

namespace SqlBackup.App.Infrastructure;

/// <summary>Deep clone via the shared JSON conventions — used to edit working copies in dialogs.</summary>
public static class Cloner
{
    public static T DeepClone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonDefaults.Compact), JsonDefaults.Compact)!;
}
