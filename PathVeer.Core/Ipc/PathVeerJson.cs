using System.Text.Json;
using System.Text.Json.Serialization;

namespace PathVeer.Core.Ipc;

public static class PathVeerJson
{
    public static JsonSerializerOptions Options { get; } =
        CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true
        };

        options.Converters.Add(
            new JsonStringEnumConverter());

        return options;
    }
}