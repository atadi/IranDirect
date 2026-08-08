using System.Text.Json;
using PathVeer.Core.Persistence;

namespace PathVeer.Core.CustomRoutes;

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
