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

    /// <summary>Serializes a response object; a text longer than <see cref="RegulatorLimits.ResponseSizeHint"/>
    /// characters gets a <c>sizeWarning</c> property that names the filters of the tool</summary>
    /// <param name="value">The response object</param>
    /// <param name="filterHint">The ways to narrow the output of the tool, e.g. the names of its filter parameters</param>
    internal static string SerializeWithSizeHint(object value, string filterHint)
    {
        var text = Serialize(value);
        if (text.Length <= RegulatorLimits.ResponseSizeHint || ToNode(value) is not JsonObject node)
        {
            return text;
        }
        node["sizeWarning"] = SizeWarning(text.Length, filterHint);
        return node.ToJsonString(OutputOptions);
    }

    /// <summary>The text of the size hint</summary>
    internal static string SizeWarning(int length, string filterHint) =>
        $"The response has {length} characters; an MCP client may move a result of this size out of the " +
        $"conversation (hint above {RegulatorLimits.ResponseSizeHint}). Narrow the output: {filterHint}.";

    /// <summary>Serializes a value to a JSON node</summary>
    internal static JsonNode ToNode(object value) =>
        JsonSerializer.SerializeToNode(value, OutputOptions);
}
