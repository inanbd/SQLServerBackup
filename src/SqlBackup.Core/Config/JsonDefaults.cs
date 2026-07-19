using System.Text.Json;
using System.Text.Json.Serialization;

namespace SqlBackup.Core.Config;

public static class JsonDefaults
{
    /// <summary>camelCase, enums as strings, indented — used for config.json.</summary>
    public static readonly JsonSerializerOptions Pretty = Create(indented: true);

    /// <summary>Same conventions but compact — used for JSONL history and IPC framing.</summary>
    public static readonly JsonSerializerOptions Compact = Create(indented: false);

    private static JsonSerializerOptions Create(bool indented)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = indented };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
