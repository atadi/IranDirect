using PathVeer.Core.Configuration;

namespace PathVeer.Core.Tests.Configuration;

public sealed class DesiredConfigurationValidatorTests
{
    private readonly DesiredConfigurationValidator _validator =
        new();

    [Fact]
    public void Validate_DefaultConfiguration_IsValid()
    {
        ConfigurationValidationResult result =
            _validator.Validate(
                ConfigurationDefaults.Create());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_EmptyProfilePath_IsInvalid()
    {
        DesiredConfiguration configuration =
            ConfigurationDefaults.Create() with
            {
                VpnProfilePath = ""
            };

        ConfigurationValidationResult result =
            _validator.Validate(configuration);

        Assert.False(result.IsValid);
        Assert.Contains(
            "VPN profile path is required.",
            result.Errors);
    }

    [Fact]
    public void Validate_RepairIntervalBelowMinimum_IsInvalid()
    {
        DesiredConfiguration configuration =
            ConfigurationDefaults.Create() with
            {
                RepairInterval =
                    TimeSpan.FromSeconds(5)
            };

        ConfigurationValidationResult result =
            _validator.Validate(configuration);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Contains(
                "at least 10 seconds",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_UpdateIntervalBelowMinimum_IsInvalid()
    {
        DesiredConfiguration configuration =
            ConfigurationDefaults.Create() with
            {
                PrefixUpdateInterval =
                    TimeSpan.FromMinutes(5)
            };

        ConfigurationValidationResult result =
            _validator.Validate(configuration);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Contains(
                "at least 15 minutes",
                StringComparison.OrdinalIgnoreCase));
    }
}