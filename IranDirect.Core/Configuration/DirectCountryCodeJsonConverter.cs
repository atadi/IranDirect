using System.Text.Json;
using System.Text.Json.Serialization;

namespace IranDirect.Core.Configuration;

/// <summary>
/// Persists <see cref="DirectCountryCode"/> as a plain ISO alpha-2 string so
/// the on-disk / IPC representation stays SaaS/API friendly. An absent field
/// resolves to the property default (IR); an invalid value fails closed during
/// deserialization.
/// </summary>
public sealed class DirectCountryCodeJsonConverter :
    JsonConverter<DirectCountryCode?>
{
    public override DirectCountryCode? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException(
                "DirectCountryCode must be a string.");
        }

        string? raw = reader.GetString();

        // Invalid country codes fail closed (deserialization error) rather
        // than being silently coerced. The store wraps this as a corrupt
        // configuration, consistent with Phase 34.4.
        try
        {
            return DirectCountryCode.Parse(raw);
        }
        catch (CountryCodeFormatException exception)
        {
            throw new JsonException(
                "DirectCountryCode is not a valid ISO 3166-1 alpha-2 " +
                "country code.",
                exception);
        }
    }

    public override void Write(
        Utf8JsonWriter writer,
        DirectCountryCode? value,
        JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Code);
    }
}
