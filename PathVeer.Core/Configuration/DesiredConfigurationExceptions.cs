namespace PathVeer.Core.Configuration;

/// <summary>
/// Base type for failures that prevent an authoritative DesiredConfiguration
/// from being loaded. Callers may catch this to distinguish a configuration
/// availability problem from any other error without parsing messages.
/// </summary>
public abstract class DesiredConfigurationException : Exception
{
    protected DesiredConfigurationException(string message)
        : base(message)
    {
    }

    protected DesiredConfigurationException(
        string message,
        Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>
/// The authoritative DesiredConfiguration file is absent (first run, deleted,
/// or never created). This is distinct from an intentionally saved disabled
/// configuration and must never be reinterpreted as one.
/// </summary>
public sealed class DesiredConfigurationMissingException
    : DesiredConfigurationException
{
    public string? ConfigurationPath { get; }

    public DesiredConfigurationMissingException(string? configurationPath)
        : base(
            "Desired configuration is missing. The service cannot infer " +
            "user intent from file absence; provide a valid configuration " +
            "before routes are managed.")
    {
        ConfigurationPath = configurationPath;
    }
}

/// <summary>
/// The DesiredConfiguration file exists but could not be parsed or failed
/// validation. The original file is preserved; it is never overwritten with
/// defaults. Routing must not be mutated based on a corrupt configuration.
/// </summary>
public sealed class DesiredConfigurationCorruptException
    : DesiredConfigurationException
{
    public string? ConfigurationPath { get; }

    public DesiredConfigurationCorruptException(
        string? configurationPath,
        Exception inner)
        : base(
            "Desired configuration could not be loaded because it is corrupt " +
            "or invalid. The original file is preserved for diagnostics.",
            inner)
    {
        ConfigurationPath = configurationPath;
    }
}
