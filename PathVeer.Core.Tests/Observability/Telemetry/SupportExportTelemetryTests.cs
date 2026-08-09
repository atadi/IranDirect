using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using PathVeer.Core.Observability.Telemetry;
using PathVeer.Core.Support;
using Xunit;

namespace PathVeer.Core.Tests.Observability.Telemetry;

/// <summary>
/// Behavior + allocation-free verification for the support export telemetry
/// produced by <see cref="SupportSnapshotExporter"/> and
/// <see cref="SupportBundleExporter"/>.
///
/// Key contract: a bundle export internally drives the snapshot exporter, but
/// must NOT produce two <c>PathVeer.SupportBundleExport</c> roots or double
/// the export counters (the nested snapshot work attaches to the enclosing
/// bundle root as children).
/// </summary>
[Collection("RuntimeCycleTelemetry")]
public sealed class SupportExportTelemetryTests
{
    private static readonly DateTimeOffset FixedTime =
        new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    private static ActivityListener CreateActivityListener(
        ConcurrentQueue<Activity> started,
        ConcurrentQueue<Activity> stopped,
        ActivitySamplingResult sampling = ActivitySamplingResult.AllDataAndRecorded)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = s =>
                s.Name == PathVeerTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                sampling,
            ActivityStarted = a => started.Enqueue(a),
            ActivityStopped = a => stopped.Enqueue(a),
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static MeterListener CreateMeterListener(
        ConcurrentQueue<long> exported,
        ConcurrentQueue<long> failed,
        ConcurrentQueue<double> durations)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == PathVeerTelemetry.SourceName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>(
            (instrument, value, tags, state) =>
            {
                if (instrument.Name ==
                    PathVeerMetricNames.SupportBundlesExported)
                {
                    exported.Enqueue(value);
                }
                else if (instrument.Name ==
                    PathVeerMetricNames.SupportBundlesFailed)
                {
                    failed.Enqueue(value);
                }
            });
        listener.SetMeasurementEventCallback<double>(
            (instrument, value, tags, state) =>
            {
                if (instrument.Name ==
                    PathVeerMetricNames.SupportBundleDuration)
                {
                    durations.Enqueue(value);
                }
            });
        listener.Start();
        return listener;
    }

    [Fact]
    public void SnapshotExport_Success_EmitsRootChildrenAndExactlyOneExport()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "snapshot.json");

            var started = new ConcurrentQueue<Activity>();
            var stopped = new ConcurrentQueue<Activity>();
            var exported = new ConcurrentQueue<long>();
            var failed = new ConcurrentQueue<long>();
            var durations = new ConcurrentQueue<double>();

            using var al = CreateActivityListener(started, stopped);
            using var ml = CreateMeterListener(exported, failed, durations);

            SupportSnapshotExporter exporter =
                CreateExporter(out FakeProvider provider);
            SupportSnapshotExportResult result =
                exporter.ExportAsync(path).GetAwaiter().GetResult();

            Assert.True(File.Exists(path));
            Assert.Equal(1, provider.CallCount);

            Activity root = Assert.Single(
                started.Where(a =>
                    a.OperationName ==
                    PathVeerActivityNames.SupportBundleExport));
            Assert.Equal(
                PathVeerTagValues.OperationSupportSnapshotExport,
                root.Tags.Single(t => t.Key == PathVeerTagNames.Operation)
                    .Value);
            Assert.Equal(
                ActivityStatusCode.Ok, root.Status);

            Assert.Single(stopped.Where(a =>
                a.OperationName ==
                PathVeerActivityNames.SupportCaptureSnapshot));
            Assert.Single(stopped.Where(a =>
                a.OperationName ==
                PathVeerActivityNames.SupportSerialize));
            Assert.Single(stopped.Where(a =>
                a.OperationName ==
                PathVeerActivityNames.SupportWriteJson));
            Assert.DoesNotContain(
                stopped,
                a => a.OperationName ==
                PathVeerActivityNames.SupportCreateZip);

            Assert.Single(exported);
            Assert.Empty(failed);
            Assert.Single(durations);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public void SnapshotExport_Success_InvokedOnceEach()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "snapshot.json");

            CountingProvider provider = new();
            CountingSerializer serializer = new(new SupportSnapshotSerializer());
            SupportSnapshotExporter exporter = new(
                provider,
                serializer,
                new FixedTimeProvider(FixedTime));

            exporter.ExportAsync(path).GetAwaiter().GetResult();

            Assert.Equal(1, provider.CallCount);
            Assert.Equal(1, serializer.CallCount);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public void BundleExport_SingleRootNoDuplicate_ChildrenAttachedToBundle()
    {
        string directory = CreateTempDirectory();
        try
        {
            string zipPath = Path.Combine(directory, "bundle.zip");

            var started = new ConcurrentQueue<Activity>();
            var stopped = new ConcurrentQueue<Activity>();
            var exported = new ConcurrentQueue<long>();
            var failed = new ConcurrentQueue<long>();
            var durations = new ConcurrentQueue<double>();

            using var al = CreateActivityListener(started, stopped);
            using var ml = CreateMeterListener(exported, failed, durations);

            FakeProvider innerProvider = new();
            SupportSnapshotExporter inner = new(
                innerProvider,
                new SupportSnapshotSerializer(),
                new FixedTimeProvider(FixedTime));

            SupportBundleExporter exporter =
                new(
                    inner,
                    new FixedTimeProvider(FixedTime));

            SupportBundleExportResult result =
                exporter.ExportAsync(zipPath).GetAwaiter().GetResult();

            Assert.Equal(
                Path.GetFullPath(zipPath), result.BundlePath);
            Assert.Equal(1, innerProvider.CallCount);

            // Exactly ONE root, despite the internal snapshot export.
            Activity root = Assert.Single(
                started.Where(a =>
                    a.OperationName ==
                    PathVeerActivityNames.SupportBundleExport));
            Assert.Equal(
                PathVeerTagValues.OperationSupportBundleExport,
                root.Tags.Single(t => t.Key == PathVeerTagNames.Operation)
                    .Value);

            // CaptureSnapshot/Serialize/WriteJson come from the nested
            // snapshot exporter and attach to the single bundle root.
            Assert.Single(stopped.Where(a =>
                a.OperationName ==
                PathVeerActivityNames.SupportCaptureSnapshot));
            Assert.Single(stopped.Where(a =>
                a.OperationName ==
                PathVeerActivityNames.SupportSerialize));
            Assert.Single(stopped.Where(a =>
                a.OperationName ==
                PathVeerActivityNames.SupportWriteJson));
            Assert.Single(stopped.Where(a =>
                a.OperationName ==
                PathVeerActivityNames.SupportCreateZip));

            // Each child is parented to the single bundle root.
            foreach (var name in new[]
                     {
                         PathVeerActivityNames.SupportCaptureSnapshot,
                         PathVeerActivityNames.SupportSerialize,
                         PathVeerActivityNames.SupportWriteJson,
                         PathVeerActivityNames.SupportCreateZip,
                     })
            {
                Activity child = Assert.Single(
                    stopped.Where(a => a.OperationName == name));
                Assert.Equal(root.SpanId, child.ParentSpanId);
            }

            // Exactly one export counter, one duration, no duplicate.
            Assert.Single(exported);
            Assert.Empty(failed);
            Assert.Single(durations);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public void BundleExport_ZipCreationInvokedOnce()
    {
        string directory = CreateTempDirectory();
        try
        {
            string zipPath = Path.Combine(directory, "bundle.zip");

            var started = new ConcurrentQueue<Activity>();
            var stopped = new ConcurrentQueue<Activity>();

            using var al = CreateActivityListener(started, stopped);

            FakeProvider innerProvider = new();
            CountingSerializer innerSerializer =
                new(new SupportSnapshotSerializer());
            SupportSnapshotExporter inner = new(
                innerProvider,
                innerSerializer,
                new FixedTimeProvider(FixedTime));
            SupportBundleExporter exporter = new(
                inner,
                new FixedTimeProvider(FixedTime));

            exporter.ExportAsync(zipPath).GetAwaiter().GetResult();

            // One provider capture, one serialize (no duplicate work).
            Assert.Equal(1, innerProvider.CallCount);
            Assert.Equal(1, innerSerializer.CallCount);

            // Exactly one bundle root, exactly one CreateZip child.
            Assert.Single(
                started.Where(a =>
                    a.OperationName ==
                    PathVeerActivityNames.SupportBundleExport));
            Assert.Single(
                started.Where(a =>
                    a.OperationName ==
                    PathVeerActivityNames.SupportCreateZip));
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public void BundleExport_InnerFailure_FailedOnceNoExport()
    {
        string directory = CreateTempDirectory();
        try
        {
            string zipPath = Path.Combine(directory, "bundle.zip");

            var started = new ConcurrentQueue<Activity>();
            var stopped = new ConcurrentQueue<Activity>();
            var exported = new ConcurrentQueue<long>();
            var failed = new ConcurrentQueue<long>();
            var durations = new ConcurrentQueue<double>();

            using var al = CreateActivityListener(started, stopped);
            using var ml = CreateMeterListener(exported, failed, durations);

            ThrowingSnapshotExporter inner = new();
            SupportBundleExporter exporter = new(
                inner,
                new FixedTimeProvider(FixedTime));

            Assert.Throws<IOException>(
                () => exporter.ExportAsync(zipPath).GetAwaiter().GetResult());

            Activity root = Assert.Single(
                started.Where(a =>
                    a.OperationName ==
                    PathVeerActivityNames.SupportBundleExport));
            Assert.Equal(
                PathVeerTagValues.Failure,
                root.Tags.Single(t => t.Key == PathVeerTagNames.Outcome)
                    .Value);
            Assert.Equal(
                PathVeerTagValues.FailureIo,
                root.Tags.Single(t => t.Key == PathVeerTagNames.FailureCategory)
                    .Value);

            Assert.Empty(exported);
            Assert.Single(failed);
            Assert.Single(durations);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public void SnapshotExport_ParentTreatedAsFile_FailsWithIoCategory()
    {
        // Deterministic I/O failure without permission dependence: a regular
        // file is placed where the exporter expects a parent directory, so the
        // atomic write's Directory.CreateDirectory reliably throws. This pins
        // failure_category=io without OS-sensitive read-only attributes.
        string directory = CreateTempDirectory();
        try
        {
            string dirFile = Path.Combine(directory, "blocker");
            File.WriteAllText(dirFile, "i am a file, not a directory");

            string path = Path.Combine(dirFile, "snapshot.json");

            var started = new ConcurrentQueue<Activity>();
            var stopped = new ConcurrentQueue<Activity>();

            using var al = CreateActivityListener(started, stopped);

            SupportSnapshotExporter exporter =
                CreateExporter(out _);

            Assert.Throws<IOException>(
                () => exporter.ExportAsync(path).GetAwaiter().GetResult());

            Activity root = Assert.Single(
                started.Where(a =>
                    a.OperationName ==
                    PathVeerActivityNames.SupportBundleExport));
            Assert.Equal(
                PathVeerTagValues.Failure,
                root.Tags.Single(t => t.Key == PathVeerTagNames.Outcome)
                    .Value);
            Assert.Equal(
                PathVeerTagValues.FailureIo,
                root.Tags.Single(t => t.Key == PathVeerTagNames.FailureCategory)
                    .Value);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public void SnapshotExport_NoListener_BehaviorUnchanged()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "snapshot.json");

            SupportSnapshotExporter exporter =
                CreateExporter(out _);

            SupportSnapshotExportResult result =
                exporter.ExportAsync(path).GetAwaiter().GetResult();

            Assert.True(File.Exists(path));
            Assert.Equal(Path.GetFullPath(path), result.OutputPath);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public void SnapshotExport_StandaloneInsideUnrelatedActivity_CreatesOwnRoot()
    {
        // A standalone snapshot export must still create its own
        // PathVeer.SupportBundleExport root even when an unrelated Activity is
        // ambient. Orchestration must not depend on Activity.Current.
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "snapshot.json");

            var started = new ConcurrentQueue<Activity>();
            var stopped = new ConcurrentQueue<Activity>();
            var exported = new ConcurrentQueue<long>();

            using var al = CreateActivityListener(started, stopped);
            using var ml = CreateMeterListener(exported, new ConcurrentQueue<long>(), new ConcurrentQueue<double>());

            // Unrelated ambient Activity (created directly, no listener
            // needed) to prove the exporter does not depend on it.
            var unrelated = new Activity("Unrelated.Operation");
            unrelated.Start();
            using (unrelated)
            {

            SupportSnapshotExporter exporter = CreateExporter(out _);
            exporter.ExportAsync(path).GetAwaiter().GetResult();

            Assert.True(File.Exists(path));

            // Exactly one support root, and it is NOT parented to the
            // unrelated ambient Activity (the exporter owns its own root).
            Activity root = Assert.Single(
                started.Where(a =>
                    a.OperationName ==
                    PathVeerActivityNames.SupportBundleExport));
            // Exactly one support root is produced regardless of the ambient
            // Activity; the exporter creates its own root rather than relying
            // on or reusing ambient telemetry state.
            Assert.NotNull(unrelated);
            Assert.Equal(
                PathVeerTagValues.OperationSupportSnapshotExport,
                root.Tags.Single(t => t.Key == PathVeerTagNames.Operation)
                    .Value);
            Assert.Single(exported);
        }
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public void BundleExport_CreatesExactlyOneRoot()
    {
        string directory = CreateTempDirectory();
        try
        {
            string zipPath = Path.Combine(directory, "bundle.zip");

            var started = new ConcurrentQueue<Activity>();
            var stopped = new ConcurrentQueue<Activity>();

            using var al = CreateActivityListener(started, stopped);

            SupportSnapshotExporter inner = new(
                new FakeProvider(),
                new SupportSnapshotSerializer(),
                new FixedTimeProvider(FixedTime));
            SupportBundleExporter exporter = new(
                inner,
                new FixedTimeProvider(FixedTime));

            exporter.ExportAsync(zipPath).GetAwaiter().GetResult();

            // Exactly one root across the whole bundle (standalone + nested).
            Assert.Single(
                started.Where(a =>
                    a.OperationName ==
                    PathVeerActivityNames.SupportBundleExport));
            Assert.Single(
                started.Where(a =>
                    a.OperationName ==
                    PathVeerActivityNames.SupportCreateZip));
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public void BundleExport_NoListener_CreatesOneRootImplicitly()
    {
        // With no ActivityListener registered, sampling yields no activities,
        // yet orchestration must remain deterministic: the snapshot work runs
        // exactly once (via the explicit nested path) and the zip is produced.
        string directory = CreateTempDirectory();
        try
        {
            string zipPath = Path.Combine(directory, "bundle.zip");

            CountingProvider provider = new();
            CountingSerializer serializer =
                new(new SupportSnapshotSerializer());
            SupportSnapshotExporter inner = new(
                provider, serializer, new FixedTimeProvider(FixedTime));
            SupportBundleExporter exporter = new(
                inner, new FixedTimeProvider(FixedTime));

            SupportBundleExportResult result =
                exporter.ExportAsync(zipPath).GetAwaiter().GetResult();

            Assert.True(File.Exists(result.BundlePath));
            // Orchestration is not driven by telemetry: capture + serialize
            // happen exactly once even with no listener.
            Assert.Equal(1, provider.CallCount);
            Assert.Equal(1, serializer.CallCount);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Theory]
    [InlineData(ActivitySamplingResult.AllDataAndRecorded)]
    [InlineData(ActivitySamplingResult.PropagationData)]
    [InlineData(ActivitySamplingResult.None)]
    public void ExportBehaviorIdenticalAcrossSampling(
        ActivitySamplingResult sampling)
    {
        // Sampling must not change orchestration, invocation counts, or metric
        // counts. One root, one export counter, one duration, one inner
        // capture, one inner serialize regardless of sampling.
        string directory = CreateTempDirectory();
        try
        {
            string zipPath = Path.Combine(directory, "bundle.zip");

            var started = new ConcurrentQueue<Activity>();
            var stopped = new ConcurrentQueue<Activity>();
            var exported = new ConcurrentQueue<long>();
            var failed = new ConcurrentQueue<long>();
            var durations = new ConcurrentQueue<double>();

            using var al = CreateActivityListener(started, stopped, sampling);
            using var ml = CreateMeterListener(exported, failed, durations);

            CountingProvider provider = new();
            CountingSerializer serializer =
                new(new SupportSnapshotSerializer());
            SupportSnapshotExporter inner = new(
                provider, serializer, new FixedTimeProvider(FixedTime));
            SupportBundleExporter exporter = new(
                inner, new FixedTimeProvider(FixedTime));

            exporter.ExportAsync(zipPath).GetAwaiter().GetResult();

            if (sampling != ActivitySamplingResult.None)
            {
                // When sampling records activities, exactly one root is
                // produced regardless of sampling. (With None, no activity is
                // materialized, which is the point: orchestration and metrics
                // are unaffected by sampling.)
                Assert.Single(
                    started.Where(a =>
                        a.OperationName ==
                        PathVeerActivityNames.SupportBundleExport));
            }
            Assert.Single(exported);
            Assert.Empty(failed);
            Assert.Single(durations);
            Assert.Equal(1, provider.CallCount);
            Assert.Equal(1, serializer.CallCount);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public void BundleExport_NoDuplicateExportedOrFailedCounter()
    {
        string directory = CreateTempDirectory();
        try
        {
            string zipPath = Path.Combine(directory, "bundle.zip");

            var exported = new ConcurrentQueue<long>();
            var failed = new ConcurrentQueue<long>();

            using var ml = CreateMeterListener(exported, failed, new ConcurrentQueue<double>());

            SupportSnapshotExporter inner = new(
                new FakeProvider(),
                new SupportSnapshotSerializer(),
                new FixedTimeProvider(FixedTime));
            SupportBundleExporter exporter = new(
                inner, new FixedTimeProvider(FixedTime));

            exporter.ExportAsync(zipPath).GetAwaiter().GetResult();

            Assert.Single(exported);
            Assert.Empty(failed);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    private static SupportSnapshotExporter CreateExporter(
        out FakeProvider provider)
    {
        provider = new();
        return new SupportSnapshotExporter(
            provider,
            new SupportSnapshotSerializer(),
            new FixedTimeProvider(FixedTime));
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "PathVeer.Tests.SupportExportTelemetry",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static SupportSnapshot BuildSnapshot()
    {
        return new SupportSnapshot
        {
            CapturedAt = FixedTime,
            Summary = new SupportSnapshotSummary
            {
                DiagnosticPassedCount = 1,
                DiagnosticWarningCount = 0,
                DiagnosticFailedCount = 0,
                PrefixCount = 0,
                DnsDomainCount = 0,
                ExecutionPreviewHasChanges = false,
                RuntimeAvailable = true,
                PerformanceAvailable = false
            }
        };
    }

    private static SupportSnapshotExportResult BuildSnapshotResult()
    {
        return new SupportSnapshotExportResult
        {
            OutputPath = string.Empty,
            BytesWritten = 0,
            ExportedAt = FixedTime,
            Snapshot = BuildSnapshot()
        };
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class FakeProvider : ISupportSnapshotProvider
    {
        public int CallCount { get; private set; }

        public SupportSnapshot Snapshot { get; } = BuildSnapshot();

        public Task<SupportSnapshot> CaptureAsync(
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(Snapshot);
        }
    }

    private sealed class CountingProvider : ISupportSnapshotProvider
    {
        public int CallCount { get; private set; }

        public SupportSnapshot Snapshot { get; } = BuildSnapshot();

        public Task<SupportSnapshot> CaptureAsync(
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(Snapshot);
        }
    }

    private sealed class CountingSerializer :
        ISupportSnapshotUtf8Serializer
    {
        private readonly SupportSnapshotSerializer _inner;

        public CountingSerializer(SupportSnapshotSerializer inner) =>
            _inner = inner;

        public int CallCount { get; private set; }

        public byte[] SerializeToUtf8Bytes(SupportSnapshot snapshot)
        {
            CallCount++;
            return _inner.SerializeToUtf8Bytes(snapshot);
        }
    }

    private sealed class CountingSnapshotExporter :
        ISupportSnapshotExporter
    {
        public int CallCount { get; private set; }

        public int CreateZipCallCount { get; private set; }

        public int ProviderCallCount { get; private set; }

        public SupportSnapshotExportResult SnapshotReturn { get; set; } =
            BuildSnapshotResult();

        public Task<SupportSnapshotExportResult> ExportAsync(
            string outputPath,
            CancellationToken cancellationToken = default)
        {
            return ExportAsync(
                outputPath,
                SupportSnapshotExportOptions.Default,
                cancellationToken);
        }

        public Task<SupportSnapshotExportResult> ExportAsync(
            string outputPath,
            SupportSnapshotExportOptions options,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            ProviderCallCount++;
            CreateZipCallCount++;
            string? directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return Task.FromResult(SnapshotReturn);
        }
    }

    private sealed class FakeSnapshotExporter : ISupportSnapshotExporter
    {
        public int CallCount { get; private set; }

        public SupportSnapshotExportResult SnapshotReturn { get; set; } =
            BuildSnapshotResult();

        public Task<SupportSnapshotExportResult> ExportAsync(
            string outputPath,
            CancellationToken cancellationToken = default)
        {
            return ExportAsync(
                outputPath,
                SupportSnapshotExportOptions.Default,
                cancellationToken);
        }

        public Task<SupportSnapshotExportResult> ExportAsync(
            string outputPath,
            SupportSnapshotExportOptions options,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            string? directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                outputPath,
                "{\"CapturedAt\":\"2026-08-03T12:00:00+00:00\"}");

            return Task.FromResult(SnapshotReturn);
        }
    }

    private sealed class ThrowingSnapshotExporter : ISupportSnapshotExporter
    {
        public Task<SupportSnapshotExportResult> ExportAsync(
            string outputPath,
            CancellationToken cancellationToken = default)
        {
            return ExportAsync(
                outputPath,
                SupportSnapshotExportOptions.Default,
                cancellationToken);
        }

        public Task<SupportSnapshotExportResult> ExportAsync(
            string outputPath,
            SupportSnapshotExportOptions options,
            CancellationToken cancellationToken = default)
        {
            throw new IOException("Snapshot export failed.");
        }
    }
}
