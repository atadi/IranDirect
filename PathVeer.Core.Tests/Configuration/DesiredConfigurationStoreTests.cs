using PathVeer.Core.Configuration;

namespace PathVeer.Core.Tests.Configuration;

public sealed class DesiredConfigurationStoreTests
{
    [Fact]
    public async Task LoadAsync_WhenFileDoesNotExist_ThrowsMissing()
    {
        TempConfig config = new();
        DesiredConfigurationStore store = config.CreateStore();

        // A missing authoritative configuration must fail closed and be
        // distinguishable from an intentionally saved disabled configuration.
        // It must NOT be substituted with new DesiredConfiguration() (which is
        // itself a valid disabled config).
        DesiredConfigurationMissingException exception =
            await Assert.ThrowsAsync<DesiredConfigurationMissingException>(
                () => store.LoadAsync());

        Assert.False(string.IsNullOrEmpty(exception.Message));
    }

    [Fact]
    public async Task LoadAsync_WhenFileIsCorrupt_ThrowsCorrupt()
    {
        TempConfig config = new();
        DesiredConfigurationStore store = config.CreateStore();

        System.IO.File.WriteAllText(
            config.Path, "this is not valid json {{{");

        DesiredConfigurationCorruptException exception =
            await Assert.ThrowsAsync<DesiredConfigurationCorruptException>(
                () => store.LoadAsync());

        Assert.False(string.IsNullOrEmpty(exception.Message));
        // The original corrupt file must be preserved (not overwritten).
        Assert.True(System.IO.File.Exists(config.Path));
    }

    [Fact]
    public async Task LoadAsync_DoesNotCreateFileWhenMissing()
    {
        TempConfig config = new();
        DesiredConfigurationStore store = config.CreateStore();

        await Assert.ThrowsAsync<DesiredConfigurationMissingException>(
            () => store.LoadAsync());

        Assert.False(
            System.IO.File.Exists(config.Path),
            "Load must not silently create a default configuration file.");
    }

    [Fact]
    public async Task LoadAsync_ValidDisabled_IsDistinguishableFromMissing()
    {
        TempConfig config = new();
        DesiredConfigurationStore store = config.CreateStore();

        DesiredConfiguration disabled = ConfigurationDefaults.Create() with
        {
            Enabled = false,
            VpnProfilePath = @"C:\VPN\work.ovpn"
        };

        await store.SaveAsync(disabled);

        DesiredConfiguration loaded = await store.LoadAsync();

        Assert.False(loaded.Enabled);
        Assert.Equal(disabled, loaded);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsConfiguration()
    {
        TempConfig config = new();
        DesiredConfigurationStore store = config.CreateStore();

        DesiredConfiguration expected = ConfigurationDefaults.Create() with
        {
            Enabled = true,
            VpnProfilePath = @"C:\VPN\work.ovpn",
            RepairInterval = TimeSpan.FromMinutes(1),
            PrefixUpdateInterval = TimeSpan.FromHours(12)
        };

        await store.SaveAsync(expected);

        DesiredConfiguration actual = await store.LoadAsync();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task SaveAsync_InvalidConfiguration_Throws()
    {
        TempConfig config = new();
        DesiredConfigurationStore store = config.CreateStore();

        DesiredConfiguration invalid = ConfigurationDefaults.Create() with
        {
            VpnProfilePath = ""
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.SaveAsync(invalid));
    }

    [Fact]
    public async Task LoadAsync_LegacyConfigWithoutCountryField_MeansIR()
    {
        TempConfig config = new();
        DesiredConfigurationStore store = config.CreateStore();

        // A legacy persisted file has no DirectCountryCode field. It must be
        // interpreted as IR with no user action and no route behavior change.
        System.IO.File.WriteAllText(
            config.Path,
            "{" +
            "\"SchemaVersion\":1," +
            "\"Enabled\":true," +
            "\"VpnProfilePath\":\"C:\\\\VPN\\\\work.ovpn\"," +
            "\"AutoRepair\":true," +
            "\"RepairInterval\":\"00:00:30\"," +
            "\"AutoUpdatePrefixes\":true," +
            "\"PrefixUpdateInterval\":\"1.00:00:00\"" +
            "}");

        DesiredConfiguration loaded = await store.LoadAsync();

        Assert.NotNull(loaded.DirectCountryCode);
        Assert.Equal("IR", loaded.DirectCountryCode!.Code);
    }

    [Fact]
    public async Task LoadAsync_ExplicitIR_Preserved()
    {
        TempConfig config = new();
        DesiredConfigurationStore store = config.CreateStore();

        DesiredConfiguration saved =
            ConfigurationDefaults.Create() with
            {
                Enabled = true,
                VpnProfilePath = @"C:\VPN\work.ovpn",
                DirectCountryCode = DirectCountryCode.IR
            };

        await store.SaveAsync(saved);

        DesiredConfiguration loaded = await store.LoadAsync();

        Assert.Equal("IR", loaded.DirectCountryCode!.Code);
    }

    [Theory]
    [InlineData("IQ")]
    [InlineData("RO")]
    public async Task LoadAsync_NonIRCountry_IsRepresentable(
        string code)
    {
        TempConfig config = new();
        DesiredConfigurationStore store = config.CreateStore();

        DesiredConfiguration saved =
            ConfigurationDefaults.Create() with
            {
                Enabled = true,
                VpnProfilePath = @"C:\VPN\work.ovpn",
                DirectCountryCode = DirectCountryCode.Parse(code)
            };

        await store.SaveAsync(saved);

        DesiredConfiguration loaded = await store.LoadAsync();

        Assert.Equal(code, loaded.DirectCountryCode!.Code);
    }

    [Fact]
    public async Task LoadAsync_InvalidCountryCode_FailsClosedAsCorrupt()
    {
        TempConfig config = new();
        DesiredConfigurationStore store = config.CreateStore();

        System.IO.File.WriteAllText(
            config.Path,
            "{" +
            "\"SchemaVersion\":1," +
            "\"Enabled\":true," +
            "\"VpnProfilePath\":\"C:\\\\VPN\\\\work.ovpn\"," +
            "\"DirectCountryCode\":\"IRQ\"" +
            "}");

        await Assert.ThrowsAsync<DesiredConfigurationCorruptException>(
            () => store.LoadAsync());
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsCountry()
    {
        TempConfig config = new();
        DesiredConfigurationStore store = config.CreateStore();

        DesiredConfiguration saved =
            ConfigurationDefaults.Create() with
            {
                Enabled = true,
                VpnProfilePath = @"C:\VPN\work.ovpn",
                DirectCountryCode = DirectCountryCode.Parse("RO")
            };

        await store.SaveAsync(saved);

        DesiredConfiguration loaded = await store.LoadAsync();

        Assert.Equal("RO", loaded.DirectCountryCode!.Code);
    }

    [Fact]
    public async Task SetEnabledAsync_PreservesCountry()
    {
        TempConfig config = new();
        DesiredConfigurationStore store = config.CreateStore();

        DesiredConfiguration seeded =
            ConfigurationDefaults.Create() with
            {
                Enabled = false,
                VpnProfilePath = @"C:\VPN\work.ovpn",
                DirectCountryCode = DirectCountryCode.Parse("RO")
            };

        await store.SaveAsync(seeded);

        DesiredConfigurationService service = new(store);
        DesiredConfiguration updated =
            await service.SetEnabledAsync(true, CancellationToken.None);

        Assert.Equal("RO", updated.DirectCountryCode!.Code);
        Assert.True(updated.Enabled);
    }

    [Fact]
    public async Task SetProfilePathAsync_PreservesCountry()
    {
        TempConfig config = new();
        DesiredConfigurationStore store = config.CreateStore();

        DesiredConfiguration seeded =
            ConfigurationDefaults.Create() with
            {
                Enabled = true,
                VpnProfilePath = @"C:\VPN\work.ovpn",
                DirectCountryCode = DirectCountryCode.Parse("IQ")
            };

        await store.SaveAsync(seeded);

        DesiredConfigurationService service = new(store);
        DesiredConfiguration updated =
            await service.SetProfilePathAsync(
                @"C:\VPN\other.ovpn",
                CancellationToken.None);

        Assert.Equal("IQ", updated.DirectCountryCode!.Code);
    }

    private sealed class TempConfig : IDisposable
    {
        public string Path { get; }

        public TempConfig()
        {
            string dir = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "IranDirect.Tests",
                Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            Path = System.IO.Path.Combine(dir, "desired-configuration.json");
        }

        public DesiredConfigurationStore CreateStore() =>
            new(Path, new DesiredConfigurationValidator());

        public void Dispose()
        {
            try
            {
                System.IO.Directory.Delete(
                    System.IO.Path.GetDirectoryName(Path)!, recursive: true);
            }
            catch { }
        }
    }
}
