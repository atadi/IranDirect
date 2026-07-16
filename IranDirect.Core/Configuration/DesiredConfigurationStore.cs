using IranDirect.Core.Persistence;

namespace IranDirect.Core.Configuration;

public sealed class DesiredConfigurationStore :
    JsonStore<DesiredConfiguration>
{
    private readonly DesiredConfigurationValidator _validator;

    public DesiredConfigurationStore(
        string configurationPath,
        DesiredConfigurationValidator validator)
        : base(configurationPath)
    {
        _validator = validator;
    }

    public override async Task<DesiredConfiguration> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        DesiredConfiguration configuration =
            await base.LoadAsync(cancellationToken);

        _validator.ValidateAndThrow(configuration);

        return configuration;
    }

    public override Task SaveAsync(
        DesiredConfiguration value,
        CancellationToken cancellationToken = default)
    {
        _validator.ValidateAndThrow(value);

        return base.SaveAsync(
            value,
            cancellationToken);
    }
}