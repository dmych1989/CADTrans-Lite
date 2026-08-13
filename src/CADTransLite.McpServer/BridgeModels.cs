// BridgeModels.cs
// JSON-RPC request/response contracts shared between the C# bridge and the Python MCP server.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CADTransLite.McpServer;

/// <summary>Incoming command envelope (newline-delimited JSON over TCP).</summary>
internal sealed class BridgeRequest
{
    [JsonPropertyName("command")] public string? Command { get; set; }
    [JsonPropertyName("params")] public JsonElement? Params { get; set; }
    [JsonPropertyName("id")] public int? Id { get; set; }
}

/// <summary>Outgoing response envelope.</summary>
internal sealed class BridgeResponse
{
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("data")] public JsonElement? Data { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("id")] public int? Id { get; set; }

    public static BridgeResponse Ok(object data, int? id = null) => new()
    {
        Success = true,
        Data = JsonSerializer.SerializeToElement(data, JsonUtils.Options),
        Id = id
    };

    public static BridgeResponse Fail(string error, int? id = null) => new()
    {
        Success = false,
        Error = error,
        Id = id
    };
}

internal static class JsonUtils
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>Small helpers for reading optional fields from a params JsonElement.</summary>
internal static class JsonExt
{
    public static string Str(this JsonElement p, string key, string def = "")
    {
        if (p.ValueKind == JsonValueKind.Object && p.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString()!;
        return def;
    }

    public static bool Bool(this JsonElement p, string key, bool def = false)
    {
        if (p.ValueKind == JsonValueKind.Object && p.TryGetProperty(key, out var v))
        {
            if (v.ValueKind == JsonValueKind.True) return true;
            if (v.ValueKind == JsonValueKind.False) return false;
        }
        return def;
    }

    public static bool Has(this JsonElement p, string key) =>
        p.ValueKind == JsonValueKind.Object && p.TryGetProperty(key, out _);
}
