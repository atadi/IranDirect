namespace IranDirect.Core.Prefixes;

public sealed record PrefixSourceDescriptor
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string? Uri { get; init; }
    public string Format { get; init; } = "";
    public string ParserVersion { get; init; } = "";

    public static void Validate(
        PrefixSourceDescriptor source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (string.IsNullOrWhiteSpace(source.Id))
        {
            throw new InvalidOperationException(
                "Prefix source ID must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(source.DisplayName))
        {
            throw new InvalidOperationException(
                "Prefix source display name must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(source.Format))
        {
            throw new InvalidOperationException(
                "Prefix source format must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(source.ParserVersion))
        {
            throw new InvalidOperationException(
                "Prefix source parser version must not be empty.");
        }
    }
}
