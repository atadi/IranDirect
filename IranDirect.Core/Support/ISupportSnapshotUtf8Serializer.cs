namespace IranDirect.Core.Support;

/// <summary>
/// Narrow serializer contract for callers that need UTF-8 bytes directly
/// (for example, the support-bundle export path) without an intermediate
/// string allocation. Implemented by <see cref="SupportSnapshotSerializer"/>
/// alongside <see cref="ISupportSnapshotSerializer"/>.
/// </summary>
public interface ISupportSnapshotUtf8Serializer
{
    byte[] SerializeToUtf8Bytes(SupportSnapshot snapshot);
}
