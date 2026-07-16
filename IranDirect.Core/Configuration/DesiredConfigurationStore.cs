using System.Text.Json;
using System.Text.Json.Serialization;
using IranDirect.Core.Persistence;

namespace IranDirect.Core.Configuration;

public sealed class DesiredConfigurationStore :
    JsonStore<DesiredConfiguration>
{
    private readonly DesiredConfigurationValidator _validator;

    public DesiredConfigurationStore(
        string configurationPath,
        DesiredConfigurationValidator validator)
        : base(
            configurationPath,
            CreateJsonOptions())
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

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new()
        {
            WriteIndented = true
        };

        options.Converters.Add(
            new JsonStringEnumConverter());

        return options;
    }
}