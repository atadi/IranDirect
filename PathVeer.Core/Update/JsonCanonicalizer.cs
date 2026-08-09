namespace PathVeer.Core.Update;

using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// Deterministic JSON canonicalization for manifest signing.
///
/// Produces stable bytes regardless of property order or insignificant
/// whitespace so a signature computed by the release pipeline (PowerShell) and
/// verified by the client (.NET) agree. Rules:
///   * property names sorted lexicographically (recursively for objects)
///   * no insignificant whitespace
///   * arrays preserve order
///   * UTF-8 output
/// </summary>
public static class JsonCanonicalizer
{
    public static byte[] Canonicalize(JsonElement element)
    {
        using var ms = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false });
        Write(writer, element);
        writer.Flush();
        return ms.ToArray();
    }

    public static byte[] Canonicalize(JsonNode? node)
    {
        using var ms = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false });
        Write(writer, node);
        writer.Flush();
        return ms.ToArray();
    }

    private static void Write(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var prop in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(prop.Name);
                    Write(writer, prop.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    Write(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var l) && element.TryGetDouble(out var d) && l == d)
                    writer.WriteNumberValue(l);
                else
                    writer.WriteNumberValue(element.GetDouble());
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }

    private static void Write(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                writer.WriteStartObject();
                foreach (var prop in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(prop.Key);
                    Write(writer, prop.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonArray arr:
                writer.WriteStartArray();
                foreach (var item in arr)
                    Write(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValue val:
                var raw = val.GetValue<JsonElement>();
                Write(writer, raw);
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }
}
