using System.Text.Json;
using System.Text.Json.Nodes;

namespace DawVcs.Domain.Serialization;

/// <summary>
/// Deterministic UTF-8 JSON serializer guaranteeing property sorting and no unneeded whitespace.
/// Satisfies FR-OBJ-008 for canonical metadata object persistence.
/// </summary>
public static class CanonicalJsonSerializer
{
    private static readonly JsonSerializerOptions SerializationOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Serializes an object into canonical UTF-8 JSON bytes with recursively sorted object keys.
    /// </summary>
    public static byte[] SerializeCanonical<T>(T value)
    {
        var node = JsonSerializer.SerializeToNode(value, SerializationOptions);
        if (node is null)
        {
            return "null"u8.ToArray();
        }

        var sortedNode = SortNode(node);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false, SkipValidation = false }))
        {
            sortedNode.WriteTo(writer);
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Deserializes canonical UTF-8 JSON bytes into the target type.
    /// </summary>
    public static T? Deserialize<T>(ReadOnlySpan<byte> utf8Json)
    {
        return JsonSerializer.Deserialize<T>(utf8Json, SerializationOptions);
    }

    private static JsonNode SortNode(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            var sortedObj = new JsonObject();
            var sortedProperties = obj.OrderBy(p => p.Key, StringComparer.Ordinal).ToList();

            foreach (var prop in sortedProperties)
            {
                sortedObj.Add(prop.Key, prop.Value is not null ? SortNode(prop.Value.DeepClone()) : null);
            }

            return sortedObj;
        }

        if (node is JsonArray arr)
        {
            var sortedArr = new JsonArray();
            foreach (var item in arr)
            {
                sortedArr.Add(item is not null ? SortNode(item.DeepClone()) : null);
            }

            return sortedArr;
        }

        return node.DeepClone();
    }
}
