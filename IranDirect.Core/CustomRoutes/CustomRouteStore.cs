using System.Text.Json;
using System.Text.Json.Serialization;
using IranDirect.Core.Persistence;

namespace IranDirect.Core.CustomRoutes;

public sealed class CustomRouteStore :
    JsonStore<CustomRouteCollection>
{
    public CustomRouteStore(string path)
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
