using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using PathVeer.Core.Observability.Telemetry;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace PathVeer.Service.Tests.Observability;

/// <summary>
/// A minimal test exporter (no real collector, no network) that records every
/// exported item. Used to prove existing Core telemetry is exportable and that
/// exporter failures never propagate into business code.
/// </summary>
internal sealed class CollectingExporter<T> : BaseExporter<T>
    where T : class
{
    private readonly bool _throwOnExport;
    public ConcurrentQueue<T> Exported { get; } = new();

    public CollectingExporter(bool throwOnExport = false)
    {
        _throwOnExport = throwOnExport;
    }

    public override ExportResult Export(
        in Batch<T> batch)
    {
        if (_throwOnExport)
        {
            throw new InvalidOperationException(
                "Simulated exporter failure");
        }

        foreach (T item in batch)
        {
            Exported.Enqueue(item);
        }

        return ExportResult.Success;
    }
}

public sealed class ObservabilityIntegrationTests
{
    private static readonly HashSet<string> ForbiddenTagKeys = new()
    {
        "destination", "gateway", "domain", "url", "path",
        "machine.name", "host.name", "user.name", "ip",
    };

    [Fact]
    public void ExistingRuntimeCycleActivity_IsExported()
    {
        var exporter = new CollectingExporter<Activity>();

        using TracerProvider provider = Sdk.CreateTracerProviderBuilder()
            .AddSource(PathVeerTelemetry.SourceName)
            .AddProcessor(new BatchActivityExportProcessor(exporter))
            .Build();

        Activity? exported = null;
        using (Activity? activity =
                   PathVeerTelemetry.ActivitySource.StartActivity(
                       PathVeerActivityNames.RuntimeCycle))
        {
            activity?.SetTag("operation", "unknown");
            activity?.SetTag("outcome", "success");
        }

        provider.ForceFlush();
        exporter.Exported.TryDequeue(out exported);

        Assert.NotNull(exported);
        Assert.Equal(PathVeerActivityNames.RuntimeCycle, exported!.OperationName);
        // Source name is exact and unchanged.
        Assert.Equal(
            PathVeerTelemetry.SourceName, exported.Source?.Name);
        Assert.Equal(
            PathVeerTelemetry.Version, exported.Source?.Version);
    }

    [Fact]
    public void ExistingMetric_IsCollected()
    {
        var exporter = new CollectingExporter<Metric>();

        using MeterProvider provider = Sdk.CreateMeterProviderBuilder()
            .AddMeter(PathVeerTelemetry.SourceName)
            .AddReader(new PeriodicExportingMetricReader(exporter))
            .Build();

        Counter<long> counter = PathVeerTelemetry.Meter
            .CreateCounter<long>(PathVeerMetricNames.RuntimeCyclesStarted);
        counter.Add(1, new KeyValuePair<string, object?>("outcome", "success"));

        provider.ForceFlush();
        Assert.True(exporter.Exported.Count >= 1);
        Assert.Contains(
            exporter.Exported,
            m => m.Name == PathVeerMetricNames.RuntimeCyclesStarted);
    }

    [Fact]
    public void DisabledMode_NoMatchingSource_ExportsNothing()
    {
        // A provider that listens to a different source must not capture the
        // PathVeer.Core activity. This mirrors the disabled (no provider)
        // behavior: export is gated by source registration.
        var exporter = new CollectingExporter<Activity>();

        using TracerProvider provider = Sdk.CreateTracerProviderBuilder()
            .AddSource("Some.Other.Source")
            .AddProcessor(new BatchActivityExportProcessor(exporter))
            .Build();

        using (PathVeerTelemetry.ActivitySource.StartActivity(
                   PathVeerActivityNames.RuntimeCycle))
        {
        }

        provider.ForceFlush();
        Assert.Empty(exporter.Exported);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void SamplingRatio_ControlsRootSpanExport(double ratio)
    {
        var exporter = new CollectingExporter<Activity>();
        var sampler = new ParentBasedSampler(
            new TraceIdRatioBasedSampler(ratio));

        using TracerProvider provider = Sdk.CreateTracerProviderBuilder()
            .AddSource(PathVeerTelemetry.SourceName)
            .SetSampler(sampler)
            .AddProcessor(new BatchActivityExportProcessor(exporter))
            .Build();

        for (int i = 0; i < 50; i++)
        {
            using Activity? activity =
                PathVeerTelemetry.ActivitySource.StartActivity(
                    PathVeerActivityNames.RuntimeCycle);
        }

        provider.ForceFlush();

        if (ratio == 0.0)
        {
            Assert.Empty(exporter.Exported);
        }
        else
        {
            Assert.NotEmpty(exporter.Exported);
        }
    }

    [Fact]
    public void MetricsUnaffectedByTraceSampling()
    {
        // Trace sampling ratio of 0 must not suppress metrics.
        var traceExporter = new CollectingExporter<Activity>();
        var metricExporter = new CollectingExporter<Metric>();

        using TracerProvider tracer = Sdk.CreateTracerProviderBuilder()
            .AddSource(PathVeerTelemetry.SourceName)
            .SetSampler(new ParentBasedSampler(
                new TraceIdRatioBasedSampler(0.0)))
            .AddProcessor(new BatchActivityExportProcessor(traceExporter))
            .Build();

        using MeterProvider meter = Sdk.CreateMeterProviderBuilder()
            .AddMeter(PathVeerTelemetry.SourceName)
            .AddReader(new PeriodicExportingMetricReader(metricExporter))
            .Build();

        Counter<long> counter = PathVeerTelemetry.Meter
            .CreateCounter<long>(PathVeerMetricNames.IpcRequests);
        counter.Add(1);

        meter.ForceFlush();
        Assert.Contains(
            metricExporter.Exported,
            m => m.Name == PathVeerMetricNames.IpcRequests);
    }

    [Fact]
    public void ExporterFailure_DoesNotFailBusinessOperation()
    {
        // A throwing exporter must not propagate into the instrumented work.
        // SimpleActivityExportProcessor runs export synchronously on the
        // calling thread, so any SDK isolation is exercised directly.
        var exporter = new CollectingExporter<Activity>(throwOnExport: true);

        using TracerProvider provider = Sdk.CreateTracerProviderBuilder()
            .AddSource(PathVeerTelemetry.SourceName)
            .AddProcessor(new SimpleActivityExportProcessor(exporter))
            .Build();

        // The business operation: start an activity and do work.
        bool completed = false;
        Exception? thrown = null;
        try
        {
            using Activity? activity =
                PathVeerTelemetry.ActivitySource.StartActivity(
                    PathVeerActivityNames.RuntimeCycle);
            completed = true;
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        Assert.True(completed);
        Assert.Null(thrown);
    }

    [Fact]
    public void ExportedActivity_HasNoForbiddenTags()
    {
        var exporter = new CollectingExporter<Activity>();

        using TracerProvider provider = Sdk.CreateTracerProviderBuilder()
            .AddSource(PathVeerTelemetry.SourceName)
            .AddProcessor(new BatchActivityExportProcessor(exporter))
            .Build();

        using (Activity? activity =
                   PathVeerTelemetry.ActivitySource.StartActivity(
                       PathVeerActivityNames.RuntimeCycle))
        {
            activity?.SetTag("operation", "unknown");
            activity?.SetTag("outcome", "success");
        }

        provider.ForceFlush();
        Assert.True(exporter.Exported.TryDequeue(out Activity? exported));
        Assert.NotNull(exported);

        foreach (var tag in exported!.TagObjects)
        {
            Assert.DoesNotContain(
                ForbiddenTagKeys, k => tag.Key == k);
        }
    }
}
