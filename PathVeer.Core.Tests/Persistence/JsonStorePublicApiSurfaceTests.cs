using System.Reflection;
using PathVeer.Core.Persistence;

namespace PathVeer.Core.Tests.Persistence;

/// <summary>
/// Audit finding 6 / 9: the persistence implementation/testing seam must NOT be
/// widened into the public API merely so Core.Tests can build fault seams.
/// InternalsVisibleTo already exists for that. This test locks the contract so
/// a future change cannot accidentally flip an internal persistence type public.
/// </summary>
public sealed class JsonStorePublicApiSurfaceTests
{
    private static readonly Assembly Core = typeof(JsonStore<>).Assembly;

    [Theory]
    [InlineData("JsonStoreRecoveryMode")]
    [InlineData("IJsonStoreFileOperations")]
    [InlineData("JsonStoreCorruptionException")]
    [InlineData("DocumentReadResult")]
    public void ImplementationSeamTypes_AreNotPublic(string typeName)
    {
        Type? type = Core.GetType(
            "PathVeer.Core.Persistence." + typeName);
        Assert.NotNull(type);
        Assert.False(
            type!.IsPublic,
            $"{typeName} must remain internal, not public.");
    }

    [Theory]
    [InlineData("JsonStoreRecoveryOptions")]
    [InlineData("JsonStoreRecoveryEventArgs")]
    [InlineData("PersistenceCorruptException")]
    public void DiagnosticsContractTypes_RemainPublic(string typeName)
    {
        Type? type = Core.GetType(
            "PathVeer.Core.Persistence." + typeName);
        Assert.NotNull(type);
        Assert.True(
            type!.IsPublic,
            $"{typeName} is part of the public recovery/diagnostics contract.");
    }

    [Fact]
    public void JsonStore_OrdinaryPublicConstructor_Unchanged()
    {
        // The public ordinary constructor (path only) must keep accepting a
        // path and remain FailClosed-compatible; it must NOT expose the
        // recovery mode or file-operations seam publicly.
        ConstructorInfo? ctor =
            typeof(JsonStore<>)
                .MakeGenericType(typeof(PublicApiProbe))
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(c =>
                    c.GetParameters() is var p
                    && p.Length >= 1
                    && p[0].ParameterType == typeof(string));

        Assert.NotNull(ctor);
    }

    private sealed record PublicApiProbe
    {
        public int Id { get; init; }
    }
}
