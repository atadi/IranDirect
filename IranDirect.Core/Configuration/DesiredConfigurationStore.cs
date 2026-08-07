using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using IranDirect.Core.Persistence;

namespace IranDirect.Core.Configuration;

public sealed class DesiredConfigurationStore :
    JsonStore<DesiredConfiguration>
{
    private readonly DesiredConfigurationValidator _validator;
    private readonly string _path;

    public DesiredConfigurationStore(
        string configurationPath,
        DesiredConfigurationValidator validator)
        : base(
            configurationPath,
            CreateJsonOptions())
    {
        _validator = validator;
        _path = configurationPath;
    }

    public override async Task<DesiredConfiguration> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        // A MISSING authoritative configuration must fail closed and be
        // distinguishable from an intentionally saved disabled configuration.
        // We must never silently substitute new DesiredConfiguration() (which
        // is itself a valid disabled config) from file absence.
        if (!File.Exists(_path))
        {
            throw new DesiredConfigurationMissingException(_path);
        }

        try
        {
            DesiredConfiguration configuration =
                await base.LoadAsync(cancellationToken);

            _validator.ValidateAndThrow(configuration);

            return configuration;
        }
        catch (DesiredConfigurationMissingException)
        {
            throw;
        }
        catch (DesiredConfigurationCorruptException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is JsonException or InvalidOperationException)
        {
            // Preserve the original file; do not overwrite or reset to defaults.
            throw new DesiredConfigurationCorruptException(_path, exception);
        }
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
