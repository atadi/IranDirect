using PathVeer.Core.Cloud;
using PathVeer.Core.Configuration;
using PathVeer.Core.CustomRoutes;
using PathVeer.Core.Persistence;

namespace PathVeer.Core.Tests.Persistence;

/// <summary>
/// Boundary guarantee from the persistence hardening: stores classified as
/// authoritative configuration, mutation journals, or security/credential
/// state MUST remain fail-closed. The generic JsonStore hardening introduced a
/// ".bak" mechanism, but it is opt-in per store. These tests prove a planted
/// ".bak" next to a fail-closed store is NEVER rolled back into place, so the
/// generic change cannot silently resurrect stale authoritative/security state.
///
/// Note: RouteMutationJournalStore is NOT a JsonStore&lt;T&gt; (it has its own
/// fail-closed implementation) and is therefore out of scope for this test; its
/// contract is unchanged by this change.
/// </summary>
public sealed class JsonStoreFailClosedBoundaryTests
{
    [Fact]
    public async Task DesiredConfigurationStore_CorruptPrimaryPlusPlantedBackup_DoesNotRollBack()
    {
        string path = CreatePath("desired-configuration.json");

        DesiredConfigurationStore store =
            new(path, new DesiredConfigurationValidator());
        // Plant a plausible stale backup that must NOT be used.
        await File.WriteAllTextAsync(
            path + ".bak",
            "{\"SchemaVersion\":1,\"Enabled\":true,\"Name\":\"planted\"}");
        // Corrupt the active primary.
        await File.WriteAllBytesAsync(path, new byte[80]);

        await Assert.ThrowsAsync<DesiredConfigurationCorruptException>(
            () => store.LoadAsync());
    }

    [Fact]
    public async Task CloudRegistrationStore_CorruptPrimaryPlusPlantedBackup_DoesNotRollBack()
    {
        string path = CreatePath("cloud-registration.json");

        CloudRegistrationStore store =
            new(path, new InMemoryCloudSecretProtector());

        // A planted stale backup of a DIFFERENT (revoked) registration must
        // never be honored.
        await File.WriteAllTextAsync(
            path + ".bak",
            "{\"State\":2,\"DeviceId\":\"planted\",\"CredentialRevoked\":true}");
        await File.WriteAllBytesAsync(path, new byte[80]);

        // The store's base JsonStore is fail-closed, so a corrupt primary
        // surfaces as a JsonException (not a rollback).
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(
            () => store.LoadRegistrationAsync());
    }

    [Fact]
    public async Task CustomRouteStore_CorruptPrimaryPlusPlantedBackup_DoesNotRollBack()
    {
        string path = CreatePath("custom-routes.json");

        CustomRouteStore store = new(path);

        await File.WriteAllTextAsync(
            path + ".bak",
            "{\"SchemaVersion\":1,\"Routes\":[{\"Domain\":\"planted\"}]}");
        await File.WriteAllBytesAsync(path, new byte[80]);

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(
            () => store.LoadAsync());
    }

    private static string CreatePath(string fileName)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"),
            fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }
}
