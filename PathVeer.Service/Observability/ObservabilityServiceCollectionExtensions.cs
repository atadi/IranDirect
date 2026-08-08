using System.Diagnostics;
using PathVeer.Core.Observability.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Exporter.OpenTelemetryProtocol;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OtlpOptions = OpenTelemetry.Exporter.OtlpExporterOptions;

namespace PathVeer.Service.Observability;

/// <summary>
/// Composition-root registration for optional OpenTelemetry hosting.
///
/// This is the ONLY place (outside tests) where OpenTelemetry packages are
/// referenced. It attaches exporters to the existing, BCL-only
/// <see cref="IranDirectTelemetry"/> ActivitySource and Meter. No workflow
/// instrumentation is added, and no Core business model references telemetry.
///
/// Behavior:
/// <list type="bullet">
///   <item>When <c>Observability:Enabled</c> is false, nothing is registered
///   and existing Core telemetry stays a harmless no-op.</item>
///   <item>When enabled, tracing and/or metrics providers are registered for
///   the single <c>IranDirect.Core</c> source/meter only.</item>
///   <item>OTLP is attached only when explicitly enabled with a valid
///   endpoint.</item>
///   <item>The console exporter is attached only in Development AND when
///   explicitly enabled.</item>
///   <item>Trace sampling is parent-based with a ratio applied to root spans,
///   clamped to [0,1].</item>
/// </list>
///
/// Exporter unavailability, queue backpressure, and export failures are handled
/// by the OpenTelemetry SDK and never propagate into Core business calls.
/// Provider disposal (and thus bounded shutdown flush) is driven by framework
/// hosting on host stop.
/// </summary>
public static class ObservabilityServiceCollectionExtensions
{
    private const string SectionName = "Observability";
    private const string DevelopmentEnvironment = "Development";

    /// <summary>
    /// Adds IranDirect OpenTelemetry hosting from the
    /// <c>Observability</c> configuration section. The host environment name
    /// is used as the deployment environment fallback and to gate the console
    /// exporter.
    /// </summary>
    public static IServiceCollection AddIranDirectObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        bool isDevelopment = string.Equals(
            environmentName,
            DevelopmentEnvironment,
            StringComparison.OrdinalIgnoreCase);

        IConfigurationSection section = configuration.GetSection(SectionName);
        if (!section.Exists())
        {
            // No configuration present -> disabled. Safe no-op.
            return services;
        }

        ObservabilityOptions options =
            ObservabilityOptions.FromConfiguration(configuration);

        if (!options.Enabled)
        {
            return services;
        }

        ObservabilityOptionsValidator.Validate(options, isDevelopment);

        string resolvedEnvironment = string.IsNullOrWhiteSpace(
            options.Environment)
            ? (environmentName ?? "unknown")
            : options.Environment;

        Resource resource = ObservabilityResourceBuilder.Create(
            options.ServiceName,
            IranDirectTelemetry.Version,
            resolvedEnvironment);

        if (options.TracingEnabled)
        {
            services.AddOpenTelemetry().WithTracing(builder =>
            {
                builder
                    .SetResourceBuilder(ResourceBuilder.CreateEmpty())
                    .AddSource(IranDirectTelemetry.SourceName);

                ConfigureSampling(builder, options.SamplingRatio);

                if (options.Otlp.Enabled)
                {
                    builder.AddOtlpExporter(o =>
                        ConfigureOtlp(o, options.Otlp,
                            options.ExportTimeoutSeconds));
                }

                if (options.Console.Enabled && isDevelopment)
                {
                    builder.AddConsoleExporter();
                }
            });
        }

        if (options.MetricsEnabled)
        {
            services.AddOpenTelemetry().WithMetrics(builder =>
            {
                builder
                    .SetResourceBuilder(ResourceBuilder.CreateEmpty())
                    .AddMeter(IranDirectTelemetry.SourceName);

                if (options.Otlp.Enabled)
                {
                    builder.AddOtlpExporter(o =>
                        ConfigureOtlp(o, options.Otlp,
                            options.ExportTimeoutSeconds));
                }

                if (options.Console.Enabled && isDevelopment)
                {
                    builder.AddConsoleExporter();
                }
            });
        }

        // Note: when no exporter is enabled but observability is on, providers
        // are still registered (spans/metrics are created in-process and
        // dropped). This is an approved "in-process, no-export" mode.

        return services;
    }

    private static void ConfigureSampling(
        TracerProviderBuilder builder,
        double samplingRatio)
    {
        double ratio = Math.Clamp(samplingRatio, 0.0, 1.0);

        // ParentBased: respects a parent's sampling decision when present
        // (span context propagated from a remote caller), otherwise applies
        // the local ratio to root spans. A ratio of 0 always drops new roots.
        Sampler sampler = new ParentBasedSampler(
            new TraceIdRatioBasedSampler(ratio));

        builder.SetSampler(sampler);
    }

    private static void ConfigureOtlp(
        OtlpOptions options,
        OtlpExporterOptions config,
        int exportTimeoutSeconds)
    {
        if (!string.IsNullOrWhiteSpace(config.Endpoint))
        {
            options.Endpoint = new Uri(config.Endpoint);
        }

        if (string.Equals(
                config.Protocol,
                "http",
                StringComparison.OrdinalIgnoreCase))
        {
            options.Protocol = OtlpExportProtocol.HttpProtobuf;
        }
        else
        {
            options.Protocol = OtlpExportProtocol.Grpc;
        }

        options.TimeoutMilliseconds =
            (int)TimeSpan.FromSeconds(exportTimeoutSeconds)
                .TotalMilliseconds;

        // Headers are loaded from the named environment variable only. The
        // value never appears in configuration, logs, or exception messages.
        if (!string.IsNullOrWhiteSpace(config.HeadersEnvironmentVariable))
        {
            string? headerValue = Environment.GetEnvironmentVariable(
                config.HeadersEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(headerValue))
            {
                options.Headers = headerValue;
            }
        }
    }
}
