using System.Text.Json;
using PathVeer.Core.Support;

namespace PathVeer.Core.Tests.Support;

/// <summary>
/// Test-only reference reproduction of the pre-change
/// <see cref="SupportSnapshotSerializer"/> path. It uses the same
/// <see cref="JsonSerializerOptions"/> configuration (WriteIndented = true,
/// no converters) and the same <see cref="JsonSerializer.Serialize(object,
/// JsonSerializerOptions)"/> overload as the original implementation. It does
/// NOT call the optimized serializer, so it serves as an independent oracle for
/// exact-output (string and UTF-8) compatibility.
/// </summary>
public static class ReferenceSupportSnapshotSerializer
{
    private static readonly JsonSerializerOptions s_options =
        new() { WriteIndented = true };

    public static string Serialize(SupportSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return JsonSerializer.Serialize(snapshot, s_options);
    }

    public static byte[] SerializeToUtf8Bytes(SupportSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return JsonSerializer.SerializeToUtf8Bytes(snapshot, s_options);
    }
}
