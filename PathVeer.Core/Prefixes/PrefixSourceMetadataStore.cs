using System.Text.Json;
using System.Text.Json.Serialization;
using PathVeer.Core.Persistence;

namespace PathVeer.Core.Prefixes;

public sealed class PrefixSourceMetadataStore :
    JsonStore<PrefixSourceMetadataDocument>
{
    public PrefixSourceMetadataStore(string path)
        : base(path, CreateJsonOptions())
    {
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new()
        {
            WriteIndented = true
        };

        options.Converters.Add(
            new JsonStringEnumConverter());

        return options;
    }
}
