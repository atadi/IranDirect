using System.Text.Json;

namespace IranDirect.Core.Support;

public sealed class SupportSnapshotSerializer :
    ISupportSnapshotSerializer
{
    private static readonly JsonSerializerOptions s_options =
        CreateOptions();

    public string Serialize(SupportSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return JsonSerializer.Serialize(
            snapshot,
            s_options);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true
        };
    }
}
