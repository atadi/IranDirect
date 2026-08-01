using System.Text.Json;
using IranDirect.Core.Persistence;

namespace IranDirect.Core.CustomRoutes;

public sealed class CustomRouteDnsCacheStore :
    JsonStore<CustomRouteDnsCacheCollection>
{
    public CustomRouteDnsCacheStore(string path)
        : base(path, CreateJsonOptions())
    {
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true
        };
    }
}
