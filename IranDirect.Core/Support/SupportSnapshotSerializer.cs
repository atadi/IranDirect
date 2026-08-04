using System.Text.Json;

namespace IranDirect.Core.Support;

public sealed class SupportSnapshotSerializer :
    ISupportSnapshotSerializer,
    ISupportSnapshotUtf8Serializer
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

    public byte[] SerializeToUtf8Bytes(SupportSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return JsonSerializer.SerializeToUtf8Bytes(
            snapshot,
            s_options);
    }

    private static JsonSerializerOptions CreateOptions() =>
        new() { WriteIndented = true };
}
