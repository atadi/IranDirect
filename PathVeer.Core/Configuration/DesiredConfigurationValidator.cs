namespace PathVeer.Core.Configuration;

public sealed class DesiredConfigurationValidator
{
    private static readonly TimeSpan MinimumRepairInterval =
        TimeSpan.FromSeconds(10);

    private static readonly TimeSpan MinimumUpdateInterval =
        TimeSpan.FromMinutes(15);

    public ConfigurationValidationResult Validate(
        DesiredConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        List<string> errors = [];

        if (configuration.SchemaVersion != 1)
        {
            errors.Add(
                $"Unsupported configuration schema version: " +
                $"{configuration.SchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(
                configuration.VpnProfilePath))
        {
            errors.Add(
                "VPN profile path is required.");
        }

        if (configuration.RepairInterval <
            MinimumRepairInterval)
        {
            errors.Add(
                "Repair interval must be at least 10 seconds.");
        }

        if (configuration.PrefixUpdateInterval <
            MinimumUpdateInterval)
        {
            errors.Add(
                "Prefix update interval must be at least 15 minutes.");
        }

        if (configuration.DirectCountryCode is not null &&
            !DirectCountryCode.TryParse(
                configuration.DirectCountryCode.Code,
                out _))
        {
            // The converter already rejects invalid codes at load time, so a
            // non-null value here is always canonical; this is defensive.
            errors.Add(
                "DirectCountryCode must be a recognized ISO 3166-1 " +
                "alpha-2 country code.");
        }

        return new ConfigurationValidationResult
        {
            Errors = errors
        };
    }

    public void ValidateAndThrow(
        DesiredConfiguration configuration)
    {
        ConfigurationValidationResult result =
            Validate(configuration);

        if (result.IsValid)
        {
            return;
        }

        throw new InvalidOperationException(
            "Desired configuration is invalid: " +
            string.Join(" ", result.Errors));
    }
}