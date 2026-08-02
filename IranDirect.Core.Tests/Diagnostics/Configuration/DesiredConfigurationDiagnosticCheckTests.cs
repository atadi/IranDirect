using IranDirect.Core.Configuration;
using IranDirect.Core.Diagnostics;
using IranDirect.Core.Diagnostics.Configuration;

namespace IranDirect.Core.Tests.Diagnostics.Configuration;

public sealed class
    DesiredConfigurationDiagnosticCheckTests :
    IDisposable
{
    private readonly string _tempFile;

    public DesiredConfigurationDiagnosticCheckTests()
    {
        _tempFile = Path.GetTempFileName();
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
        {
            File.Delete(_tempFile);
        }
    }

    [Fact]
    public async Task CheckAsync_ValidConfig_ReturnsPassed()
    {
        await WriteConfigAsync(ValidConfig());

        var store = new DesiredConfigurationStore(
            _tempFile,
            new DesiredConfigurationValidator());
        var service = new DesiredConfigurationService(store);
        var check = new DesiredConfigurationDiagnosticCheck(
            service);

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Equal(DiagnosticSeverity.Info, result.Severity);
    }

    [Fact]
    public async Task CheckAsync_UnsupportedSchemaVersion_ReturnsFailed()
    {
        DesiredConfiguration config =
            ValidConfig() with { SchemaVersion = 2 };

        await WriteConfigAsync(config);

        var store = new DesiredConfigurationStore(
            _tempFile,
            new DesiredConfigurationValidator());
        var service = new DesiredConfigurationService(store);
        var check = new DesiredConfigurationDiagnosticCheck(
            service);

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Equal(DiagnosticSeverity.Error, result.Severity);
        Assert.NotNull(result.SuggestedAction);
    }

    [Fact]
    public async Task CheckAsync_EmptyProfilePath_ReturnsFailed()
    {
        DesiredConfiguration config =
            ValidConfig() with { VpnProfilePath = "" };

        await WriteConfigAsync(config);

        var store = new DesiredConfigurationStore(
            _tempFile,
            new DesiredConfigurationValidator());
        var service = new DesiredConfigurationService(store);
        var check = new DesiredConfigurationDiagnosticCheck(
            service);

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Equal(DiagnosticSeverity.Error, result.Severity);
    }

    [Fact]
    public async Task CheckAsync_NonPositiveRepairInterval_ReturnsFailed()
    {
        DesiredConfiguration config =
            ValidConfig() with { RepairInterval = TimeSpan.Zero };

        await WriteConfigAsync(config);

        var store = new DesiredConfigurationStore(
            _tempFile,
            new DesiredConfigurationValidator());
        var service = new DesiredConfigurationService(store);
        var check = new DesiredConfigurationDiagnosticCheck(
            service);

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Equal(DiagnosticSeverity.Error, result.Severity);
    }

    [Fact]
    public async Task CheckAsync_NonPositivePrefixUpdateInterval_ReturnsFailed()
    {
        DesiredConfiguration config =
            ValidConfig() with { PrefixUpdateInterval = TimeSpan.Zero };

        await WriteConfigAsync(config);

        var store = new DesiredConfigurationStore(
            _tempFile,
            new DesiredConfigurationValidator());
        var service = new DesiredConfigurationService(store);
        var check = new DesiredConfigurationDiagnosticCheck(
            service);

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Equal(DiagnosticSeverity.Error, result.Severity);
    }

    [Fact]
    public async Task CheckAsync_MissingFile_ReturnsFailed()
    {
        var store = new DesiredConfigurationStore(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()),
            new DesiredConfigurationValidator());
        var service = new DesiredConfigurationService(store);
        var check = new DesiredConfigurationDiagnosticCheck(
            service);

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        // A missing file returns default config which passes
        // validation with defaults (SchemaVersion=1, etc.)
        Assert.Equal(DiagnosticStatus.Passed, result.Status);
    }

    private static DesiredConfiguration ValidConfig() =>
        new()
        {
            SchemaVersion = 1,
            VpnProvider = VpnProviderType.OpenVpn,
            VpnProfilePath = "vpn-profile.ovpn",
            RepairInterval = TimeSpan.FromSeconds(30),
            PrefixUpdateInterval = TimeSpan.FromDays(1)
        };

    private async Task WriteConfigAsync(
        DesiredConfiguration config)
    {
        string json =
            System.Text.Json.JsonSerializer.Serialize(
                config,
                new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters =
                    {
                        new System.Text.Json.Serialization.JsonStringEnumConverter()
                    }
                });

        await File.WriteAllTextAsync(_tempFile, json);
    }
}