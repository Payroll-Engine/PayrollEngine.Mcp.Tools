using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace PayrollEngine.Mcp.Tools.RegulatorTools;

/// <summary>JSON helpers of the Regulator read tools</summary>
internal static class RegulatorJson
{
    /// <summary>Output options: camelCase names, enums as text, no null values</summary>
    private static readonly JsonSerializerOptions OutputOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>Tolerant reading of lex.json and snapshot.json (comments, trailing commas)</summary>
    internal static readonly JsonSerializerOptions InputOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>Serializes a value to JSON text</summary>
    internal static string Serialize(object value) =>
        JsonSerializer.Serialize(value, OutputOptions);

    /// <summary>Serializes a value to a JSON node</summary>
    internal static JsonNode ToNode(object value) =>
        JsonSerializer.SerializeToNode(value, OutputOptions);
}
