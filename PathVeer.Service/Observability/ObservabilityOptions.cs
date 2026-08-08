namespace PathVeer.Service.Observability;

/// <summary>
/// Configuration contract for optional OpenTelemetry hosting in the
/// IranDirect Service. Telemetry is disabled by default; enabling it only
/// attaches exporters to the existing <c>IranDirect.Core</c> ActivitySource and
/// Meter. No workflow instrumentation is added here (that lives in Core).
///
/// Secrets (OTLP headers/tokens) are never stored in configuration; they are
/// loaded from an environment variable named by
/// <see cref="OtlpExporterOptions.HeadersEnvironmentVariable"/>.
/// </summary>
public sealed record ObservabilityOptions
{
    /// <summary>
    /// Master switch. When <c>false</c> no tracer or meter provider is
    /// registered and existing Core telemetry is unaffected.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Emit traces when <see cref="Enabled"/> is true.
    /// </summary>
    public bool TracingEnabled { get; init; } = true;

    /// <summary>
    /// Emit metrics when <see cref="Enabled"/> is true.
    /// </summary>
    public bool MetricsEnabled { get; init; } = true;

    /// <summary>
    /// Parent-based ratio sampling for traces. Clamped to [0.0, 1.0].
    /// </summary>
    public double SamplingRatio { get; init; } = 1.0;

    /// <summary>
    /// Per-export timeout in seconds (bounded, positive, &lt;= 60).
    /// </summary>
    public int ExportTimeoutSeconds { get; init; } = 5;

    /// <summary>
    /// Bounded host-shutdown flush window in seconds (positive, &lt;= 30).
    /// Provider disposal is driven by framework hosting; this bounds the
    /// exporter flush during shutdown.
    /// </summary>
    public int ShutdownFlushTimeoutSeconds { get; init; } = 5;

    /// <summary>
    /// OpenTelemetry <c>service.name</c> resource attribute. Defaults to
    /// <c>IranDirect.Service</c> when blank.
    /// </summary>
    public string ServiceName { get; init; } =
        "IranDirect.Service";

    /// <summary>
    /// Deployment environment name (<c>deployment.environment.name</c>). When
    /// blank the host environment name is used as a fallback.
    /// </summary>
    public string Environment { get; init; } = string.Empty;

    /// <summary>OTLP exporter configuration.</summary>
    public OtlpExporterOptions Otlp { get; init; } = new();

    /// <summary>Console exporter configuration.</summary>
    public ConsoleExporterOptions Console { get; init; } = new();

    /// <summary>
    /// Binds and validates the <c>Observability</c> configuration section.
    /// Throws a concise <see cref="ArgumentException"/> on invalid configuration
    /// so the host fails fast before runtime work begins. No secret values are
    /// included in the exception message.
    /// </summary>
    public static ObservabilityOptions FromConfiguration(
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        ObservabilityOptions options = new();
        configuration.GetSection("Observability").Bind(options);
        ObservabilityOptionsValidator.Validate(options);
        return options;
    }
}

/// <summary>OTLP exporter options. Disabled by default.</summary>
public sealed record OtlpExporterOptions
{
    /// <summary>Enable the OTLP exporter. Disabled by default.</summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Absolute <c>http(s)://</c> collector endpoint. Must be set when enabled.
    /// </summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>Transport protocol: <c>grpc</c> (default) or <c>http</c>.</summary>
    public string Protocol { get; init; } = "grpc";

    /// <summary>
    /// Name of the environment variable that holds OTLP headers/token. Never
    /// stored in configuration. When set, the variable must resolve to a
    /// non-empty value or configuration fails.
    /// </summary>
    public string HeadersEnvironmentVariable { get; init; } =
        string.Empty;
}

/// <summary>Console exporter options. Disabled by default.</summary>
public sealed record ConsoleExporterOptions
{
    /// <summary>
    /// Enable the console exporter. Allowed only in Development and requires
    /// explicit opt-in; never enabled automatically.
    /// </summary>
    public bool Enabled { get; init; }
}
