using IranDirect.Service.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace IranDirect.Service.Tests.Observability;

public sealed class ObservabilityRegistrationTests
{
    private static IConfiguration BuildConfig(
        Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static Dictionary<string, string?> Enabled(bool tracing, bool metrics)
        => new()
        {
            ["Observability:Enabled"] = "true",
            ["Observability:TracingEnabled"] = tracing ? "true" : "false",
            ["Observability:MetricsEnabled"] = metrics ? "true" : "false",
        };

    [Fact]
    public void Disabled_RegistersNoProviders()
    {
        ServiceCollection services = new();
        IConfiguration config = BuildConfig(
            new() { ["Observability:Enabled"] = "false" });

        services.AddIranDirectObservability(config, "Production");

        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<TracerProvider>());
        Assert.Null(provider.GetService<MeterProvider>());
    }

    [Fact]
    public void Disabled_WhenSectionAbsent_RegistersNoProviders()
    {
        ServiceCollection services = new();
        IConfiguration config = BuildConfig(new Dictionary<string, string?>());

        services.AddIranDirectObservability(config, "Production");

        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<TracerProvider>());
        Assert.Null(provider.GetService<MeterProvider>());
    }

    [Fact]
    public void TracingOnly_RegistersTracerProviderOnly()
    {
        ServiceCollection services = new();
        IConfiguration config = BuildConfig(Enabled(tracing: true, metrics: false));

        services.AddIranDirectObservability(config, "Production");

        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<TracerProvider>());
        Assert.Null(provider.GetService<MeterProvider>());
    }

    [Fact]
    public void MetricsOnly_RegistersMeterProviderOnly()
    {
        ServiceCollection services = new();
        IConfiguration config = BuildConfig(Enabled(tracing: false, metrics: true));

        services.AddIranDirectObservability(config, "Production");

        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<MeterProvider>());
        Assert.Null(provider.GetService<TracerProvider>());
    }

    [Fact]
    public void BothEnabled_RegistersBothProviders()
    {
        ServiceCollection services = new();
        IConfiguration config = BuildConfig(Enabled(tracing: true, metrics: true));

        services.AddIranDirectObservability(config, "Production");

        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<TracerProvider>());
        Assert.NotNull(provider.GetService<MeterProvider>());
    }

    [Fact]
    public void BothEnabled_ProvidersResolveWithoutExporter()
    {
        // Approved in-process, no-export mode: providers build fine with no
        // exporter configured. No network access, no exception.
        ServiceCollection services = new();
        IConfiguration config = BuildConfig(Enabled(tracing: true, metrics: true));

        services.AddIranDirectObservability(config, "Production");

        using ServiceProvider provider = services.BuildServiceProvider();

        // Resolution triggers provider construction; must not throw.
        TracerProvider tracer = provider.GetRequiredService<TracerProvider>();
        MeterProvider meter = provider.GetRequiredService<MeterProvider>();
        Assert.NotNull(tracer);
        Assert.NotNull(meter);
    }

    [Fact]
    public void InvalidConfiguration_FailsDuringRegistration()
    {
        // Similar to a bad endpoint: sampling ratio out of range should fail
        // startup (registration) rather than runtime.
        ServiceCollection services = new();
        IConfiguration config = BuildConfig(
            new()
            {
                ["Observability:Enabled"] = "true",
                ["Observability:SamplingRatio"] = "5",
            });

        Assert.Throws<ArgumentException>(() =>
            services.AddIranDirectObservability(config, "Production"));
    }
}
