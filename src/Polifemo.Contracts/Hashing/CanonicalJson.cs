using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Polifemo.Contracts.Hashing;

/// <summary>Keys sorted ordinally, no insignificant whitespace, numbers in the invariant culture, UTF-8.</summary>
public static class CanonicalJson
{
    public static string Serialize(object? value)
    {
        var node = value as JsonNode ?? JsonSerializer.SerializeToNode(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            Write(writer, node);
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Write(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonObject obj:
                writer.WriteStartObject();
                foreach (var (key, child) in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(key);
                    Write(writer, child);
                }
                writer.WriteEndObject();
                break;
            case JsonArray array:
                writer.WriteStartArray();
                foreach (var child in array) Write(writer, child);
                writer.WriteEndArray();
                break;
            case JsonValue v when v.TryGetValue<decimal>(out var d) && v.GetValueKind() == JsonValueKind.Number:
                writer.WriteRawValue(d.ToString(CultureInfo.InvariantCulture));
                break;
            default:
                node.WriteTo(writer);
                break;
        }
    }
}
