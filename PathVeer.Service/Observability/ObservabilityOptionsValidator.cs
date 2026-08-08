namespace PathVeer.Service.Observability;

/// <summary>
/// Startup validation for <see cref="ObservabilityOptions"/>. Fails fast with a
/// concise <see cref="ArgumentException"/> whose message never contains a
/// secret (endpoint URL is reported only as a validity state, not its value;
/// headers are referenced by environment-variable name only).
/// </summary>
public static class ObservabilityOptionsValidator
{
    private const string DevelopmentEnvironment = "Development";

    // Bounded configuration ranges. These cap resource use and prevent
    // unbounded waits during export or shutdown.
    private const int MaxExportTimeoutSeconds = 60;
    private const int MaxShutdownFlushTimeoutSeconds = 30;

    /// <summary>
    /// Validates the options. When <see cref="ObservabilityOptions.Enabled"/> is
    /// false only structural sanity is checked; exporters are not required.
    /// </summary>
    /// <param name="isDevelopment">
    /// Whether the host is running in the Development environment. Drives the
    /// console-exporter restriction.
    /// </param>
    public static void Validate(
        ObservabilityOptions options,
        bool isDevelopment = false)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.SamplingRatio < 0.0 || options.SamplingRatio > 1.0)
        {
            throw new ArgumentException(
                "SamplingRatio must be between 0.0 and 1.0.",
                nameof(options));
        }

        if (options.ExportTimeoutSeconds <= 0 ||
            options.ExportTimeoutSeconds > MaxExportTimeoutSeconds)
        {
            throw new ArgumentException(
                $"ExportTimeoutSeconds must be between 1 and " +
                $"{MaxExportTimeoutSeconds}.",
                nameof(options));
        }

        if (options.ShutdownFlushTimeoutSeconds <= 0 ||
            options.ShutdownFlushTimeoutSeconds >
                MaxShutdownFlushTimeoutSeconds)
        {
            throw new ArgumentException(
                $"ShutdownFlushTimeoutSeconds must be between 1 and " +
                $"{MaxShutdownFlushTimeoutSeconds}.",
                nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.ServiceName))
        {
            throw new ArgumentException(
                "ServiceName must be non-empty.",
                nameof(options));
        }

        // Console exporter is development-only and must be explicitly enabled.
        if (options.Console.Enabled && !isDevelopment)
        {
            throw new ArgumentException(
                "Console exporter is only allowed in the Development " +
                "environment.",
                nameof(options));
        }

        ValidateOtlp(options.Otlp);
    }

    private static void ValidateOtlp(OtlpExporterOptions otlp)
    {
        ArgumentNullException.ThrowIfNull(otlp);

        if (!otlp.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(otlp.Endpoint))
        {
            throw new ArgumentException(
                "Otlp.Endpoint must be a non-empty absolute URI when " +
                "Otlp.Enabled is true.",
                nameof(otlp));
        }

        if (!Uri.TryCreate(
                otlp.Endpoint,
                UriKind.Absolute,
                out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                "Otlp.Endpoint must be an absolute http or https URI when " +
                "Otlp.Enabled is true.",
                nameof(otlp));
        }

        if (!string.IsNullOrWhiteSpace(otlp.HeadersEnvironmentVariable))
        {
            string? headerValue = Environment.GetEnvironmentVariable(
                otlp.HeadersEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(headerValue))
            {
                throw new ArgumentException(
                    "Otlp.HeadersEnvironmentVariable is set but the named " +
                    "environment variable is missing or empty.",
                    nameof(otlp));
            }
        }
    }
}
