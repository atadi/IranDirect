using IranDirect.Core.Configuration;
using IranDirect.Core.Diagnostics;

namespace IranDirect.Core.Diagnostics.Configuration;

public sealed class DesiredConfigurationDiagnosticCheck :
    IDiagnosticCheck
{
    private readonly DesiredConfigurationService _configurationService;

    public DesiredConfigurationDiagnosticCheck(
        DesiredConfigurationService configurationService)
    {
        _configurationService = configurationService;
    }

    public string Id => "desired-configuration";
    public string Title => "Desired configuration";

    public async Task<DiagnosticResult> CheckAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            DesiredConfiguration config =
                await _configurationService.GetAsync(
                    cancellationToken);

            if (config.SchemaVersion != 1)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message:
                        $"Unsupported schema version: " +
                        $"{config.SchemaVersion}.",
                    SuggestedAction:
                        "Update the configuration file to " +
                        "schema version 1.");
            }

            if (!Enum.IsDefined(
                typeof(VpnProviderType),
                config.VpnProvider))
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message:
                        $"Invalid VPN provider: " +
                        $"{config.VpnProvider}.",
                    SuggestedAction:
                        "Set the VPN provider to a valid value.");
            }

            if (string.IsNullOrWhiteSpace(
                config.VpnProfilePath))
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message: "VPN profile path is empty.",
                    SuggestedAction:
                        "Set the VPN profile path in the " +
                        "configuration.");
            }

            if (config.RepairInterval <= TimeSpan.Zero)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message:
                        $"Repair interval must be greater " +
                        $"than zero. Got: " +
                        $"{config.RepairInterval}.",
                    SuggestedAction:
                        "Set the repair interval to a " +
                        "positive duration.");
            }

            if (config.PrefixUpdateInterval <= TimeSpan.Zero)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message:
                        $"Prefix update interval must be " +
                        $"greater than zero. Got: " +
                        $"{config.PrefixUpdateInterval}.",
                    SuggestedAction:
                        "Set the prefix update interval to " +
                        "a positive duration.");
            }

            return new DiagnosticResult(
                Id: Id,
                Title: Title,
                Status: DiagnosticStatus.Passed,
                Severity: DiagnosticSeverity.Info,
                Message: "Configuration is valid.",
                SuggestedAction: null);
        }
        catch (Exception exception)
        {
            return new DiagnosticResult(
                Id: Id,
                Title: Title,
                Status: DiagnosticStatus.Failed,
                Severity: DiagnosticSeverity.Error,
                Message:
                    $"Could not read configuration: " +
                    $"{exception.Message}",
                SuggestedAction:
                    "Ensure the configuration file exists " +
                    "and is readable.");
        }
    }
}