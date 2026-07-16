using IranDirect.Core.Configuration;

namespace IranDirect.Core.Tests.Configuration;

public sealed class DesiredConfigurationStoreTests
{
    [Fact]
    public async Task LoadAsync_WhenFileDoesNotExist_ReturnsDefaults()
    {
        DesiredConfigurationStore store =
            CreateStore();

        DesiredConfiguration configuration =
            await store.LoadAsync();

        Assert.False(configuration.Enabled);
        Assert.Equal(
            VpnProviderType.OpenVpn,
            configuration.VpnProvider);
        Assert.True(configuration.AutoRepair);
        Assert.True(configuration.AutoUpdatePrefixes);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsConfiguration()
    {
        DesiredConfigurationStore store =
            CreateStore();

        DesiredConfiguration expected =
            ConfigurationDefaults.Create() with
            {
                Enabled = true,
                VpnProfilePath =
                    @"C:\VPN\work.ovpn",
                RepairInterval =
                    TimeSpan.FromMinutes(1),
                PrefixUpdateInterval =
                    TimeSpan.FromHours(12)
            };

        await store.SaveAsync(expected);

        DesiredConfiguration actual =
            await store.LoadAsync();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task SaveAsync_InvalidConfiguration_Throws()
    {
        DesiredConfigurationStore store =
            CreateStore();

        DesiredConfiguration invalid =
            ConfigurationDefaults.Create() with
            {
                VpnProfilePath = ""
            };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.SaveAsync(invalid));
    }

    private static DesiredConfigurationStore CreateStore()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"),
            "desired-configuration.json");

        return new DesiredConfigurationStore(
            path,
            new DesiredConfigurationValidator());
    }
}