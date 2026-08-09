using PathVeer.Core.Configuration;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using PathVeer.Core.Observability.Telemetry;
using PathVeer.Core.Prefixes;
using PathVeer.Core.Testing.FaultInjection;
using Xunit;

namespace PathVeer.Core.Tests.Observability.Telemetry;

[Collection("RuntimeCycleTelemetry")]
public sealed class PrefixUpdateTelemetryTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // ---- helpers --------------------------------------------------------

    private static ActivityListener CreateActivityListener(
        ConcurrentQueue<Activity> started,
        ConcurrentQueue<Activity> stopped)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = s =>
                s.Name == PathVeerTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = a => started.Enqueue(a),
            ActivityStopped = a => stopped.Enqueue(a),
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static MeterListener CreateMeterListener(
        ConcurrentQueue<long> checks,
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
                    PathVeerMetricNames.PrefixChecks)
                {
                    checks.Enqueue(value);
                }
            });
        listener.SetMeasurementEventCallback<double>(
            (instrument, value, tags, state) =>
            {
                if (instrument.Name ==
                    PathVeerMetricNames.PrefixCheckDuration)
                {
                    durations.Enqueue(value);
                }
            });
        listener.Start();
        return listener;
    }

    private static CountryPrefixUpdateChecker CreateChecker(
        HttpMessageHandler handler,
        IPrefixSourceMetadataService metadataService,
        TimeSpan? timeout = null,
        TimeProvider? timeProvider = null,
        IFaultInjectionPolicy? faultPolicy = null) =>
        new(
            new StubCountryPrefixSource(),
            metadataService,
            new PrefixUpdateCheckOptions
            {
                Timeout = timeout ?? TimeSpan.FromSeconds(5)
            },
            new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            },
            () => DirectCountryCode.IR,
            timeProvider,
            faultPolicy);

    private static PrefixSourceMetadata CreateMetadata() =>
        new()
        {
            SourceId = "ripe-stat-country-resource-list-ipv4",
            SourceDisplayName =
                "RIPEstat Iran IPv4 country resource list",
            ETag = "\"etag1\"",
            SourceLastModified = BaseTime,
            ContentLength = 512
        };

    private static HttpResponseMessage CreateResponse(
        HttpStatusCode status,
        string? etag = null,
        DateTimeOffset? lastModified = null,
        long? contentLength = null)
    {
        HttpResponseMessage response = new(status);
        if (etag is not null)
        {
            response.Headers.ETag = new EntityTagHeaderValue(etag);
        }
        if (lastModified is not null)
        {
            response.Content.Headers.LastModified = lastModified;
        }
        response.Content.Headers.ContentLength =
            contentLength ?? 0;
        return response;
    }

    private sealed class StubMetadataService :
        IPrefixSourceMetadataService
    {
        public PrefixSourceMetadata? Current { get; set; }

        public Task<PrefixSourceMetadata?> GetCurrentAsync(
            DirectCountryCode country,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Current);

        public Task<PrefixSourceChangeSummary?>
            GetLatestChangeSummaryAsync(
                DirectCountryCode country,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<PrefixSourceChangeSummary?>(null);

        public Task RecordSuccessAsync(
            DirectCountryCode country,
            PrefixSourceFetchResult result,
            IReadOnlyList<string>? previousPrefixes = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RecordNotModifiedAsync(
            DirectCountryCode country,
            PrefixSourceFetchResult result,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RecordFailureAsync(
            DirectCountryCode country,
            PrefixSourceDescriptor source,
            string error,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage>
            _responder;

        public StubHttpMessageHandler(
            Func<HttpRequestMessage, HttpResponseMessage> responder) =>
            _responder = responder;

        public List<HttpMethod> Methods { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            return Task.FromResult(_responder(request));
        }
    }

    // ---- tests ----------------------------------------------------------

    [Fact]
    public void CheckAsync_HeadSuccess_EmitsRootHeadChildAndSuccess()
    {
        var started = new ConcurrentQueue<Activity>();
        var stopped = new ConcurrentQueue<Activity>();
        var checks = new ConcurrentQueue<long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(started, stopped);
        using var ml = CreateMeterListener(checks, durations);

        StubHttpMessageHandler handler = new(
            _ => CreateResponse(
                HttpStatusCode.OK,
                etag: "\"etag1\"",
                lastModified: BaseTime,
                contentLength: 512));
        StubMetadataService metadata = new() { Current = CreateMetadata() };

        CountryPrefixUpdateChecker checker =
            CreateChecker(handler, metadata);

        PrefixUpdateCheckResult result =
            checker.CheckAsync(CancellationToken.None).GetAwaiter()
                .GetResult();

        Assert.Equal(PrefixUpdateCheckStatus.Current, result.Status);

        Activity root = Assert.Single(
            started.Where(a =>
                a.OperationName ==
                PathVeerActivityNames.PrefixUpdateCheck));
        Assert.Equal(
            PathVeerTagValues.OperationPrefixUpdateCheck,
            root.Tags.Single(t =>
                t.Key == PathVeerTagNames.Operation).Value);
        Assert.Equal(
            PathVeerTagValues.SourceOfficial,
            root.Tags.Single(t =>
                t.Key == PathVeerTagNames.Source).Value);
        Assert.Equal(
            PathVeerTagValues.TriggerUnknown,
            root.Tags.Single(t =>
                t.Key == PathVeerTagNames.Trigger).Value);

        Activity head = Assert.Single(
            stopped.Where(a =>
                a.OperationName ==
                PathVeerActivityNames.PrefixHttpHead));
        Assert.Equal(
            PathVeerTagValues.OperationPrefixHttpHead,
            head.Tags.Single(t =>
                t.Key == PathVeerTagNames.Operation).Value);

        Assert.DoesNotContain(
            stopped,
            a => a.OperationName ==
                PathVeerActivityNames.PrefixHttpGet);

        // The 200 HEAD response exposes comparison metadata, so the Compare
        // child is emitted.
        Activity compare = Assert.Single(
            stopped.Where(a =>
                a.OperationName ==
                PathVeerActivityNames.PrefixCompare));
        Assert.Equal(
            PathVeerTagValues.OperationPrefixCompare,
            compare.Tags.Single(t =>
                t.Key == PathVeerTagNames.Operation).Value);
        Assert.Equal(
            PathVeerTagValues.Success,
            compare.Tags.Single(t =>
                t.Key == PathVeerTagNames.Outcome).Value);

        Assert.Single(checks);
        Assert.Single(durations);
        Assert.Equal(
            ActivityStatusCode.Ok, root.Status);
    }

    [Fact]
    public void CheckAsync_HeadUnsupported_FallsBackToGet()
    {
        var started = new ConcurrentQueue<Activity>();
        var stopped = new ConcurrentQueue<Activity>();
        var checks = new ConcurrentQueue<long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(started, stopped);
        using var ml = CreateMeterListener(checks, durations);

        StubHttpMessageHandler handler = new(
            request =>
            {
                if (request.Method == HttpMethod.Head)
                {
                    return CreateResponse(HttpStatusCode.MethodNotAllowed);
                }
                return CreateResponse(
                    HttpStatusCode.OK,
                    etag: "\"etag1\"",
                    lastModified: BaseTime,
                    contentLength: 512);
            });
        StubMetadataService metadata = new() { Current = CreateMetadata() };

        CountryPrefixUpdateChecker checker =
            CreateChecker(handler, metadata);

        checker.CheckAsync(CancellationToken.None).GetAwaiter()
            .GetResult();

        Assert.Contains(
            stopped,
            a => a.OperationName ==
                PathVeerActivityNames.PrefixHttpHead);
        Activity get = Assert.Single(
            stopped.Where(a =>
                a.OperationName ==
                PathVeerActivityNames.PrefixHttpGet));
        Assert.Equal(
            PathVeerTagValues.OperationPrefixHttpGet,
            get.Tags.Single(t =>
                t.Key == PathVeerTagNames.Operation).Value);
        Assert.Single(checks);
    }

    [Fact]
    public void CheckAsync_ChangedEtag_EmitsCompareAndSuccess()
    {
        var stopped = new ConcurrentQueue<Activity>();
        var started = new ConcurrentQueue<Activity>();
        var checks = new ConcurrentQueue<long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(started, stopped);
        using var ml = CreateMeterListener(checks, durations);

        StubHttpMessageHandler handler = new(
            _ => CreateResponse(
                HttpStatusCode.OK,
                etag: "\"etag2\"",
                lastModified: BaseTime,
                contentLength: 512));
        StubMetadataService metadata = new() { Current = CreateMetadata() };

        CountryPrefixUpdateChecker checker =
            CreateChecker(handler, metadata);

        PrefixUpdateCheckResult result =
            checker.CheckAsync(CancellationToken.None).GetAwaiter()
                .GetResult();

        Assert.Equal(
            PrefixUpdateCheckStatus.UpdateAvailable, result.Status);

        Activity compare = Assert.Single(
            stopped.Where(a =>
                a.OperationName ==
                PathVeerActivityNames.PrefixCompare));
        Assert.Equal(
            PathVeerTagValues.OperationPrefixCompare,
            compare.Tags.Single(t =>
                t.Key == PathVeerTagNames.Operation).Value);
        Assert.Equal(
            PathVeerTagValues.Success,
            compare.Tags.Single(t =>
                t.Key == PathVeerTagNames.Outcome).Value);

        Activity root = Assert.Single(
            started.Where(a =>
                a.OperationName ==
                PathVeerActivityNames.PrefixUpdateCheck));
        Assert.Equal(
            PathVeerTagValues.Success,
            root.Tags.Single(t =>
                t.Key == PathVeerTagNames.Outcome).Value);
    }

    [Fact]
    public void CheckAsync_NoLocalMetadata_UnknownOutcomeNoHttp()
    {
        var stopped = new ConcurrentQueue<Activity>();
        var started = new ConcurrentQueue<Activity>();
        var checks = new ConcurrentQueue<long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(started, stopped);
        using var ml = CreateMeterListener(checks, durations);

        StubHttpMessageHandler handler = new(
            _ => throw new InvalidOperationException("must not run"));
        StubMetadataService metadata = new() { Current = null };

        CountryPrefixUpdateChecker checker =
            CreateChecker(handler, metadata);

        PrefixUpdateCheckResult result =
            checker.CheckAsync(CancellationToken.None).GetAwaiter()
                .GetResult();

        Assert.Equal(PrefixUpdateCheckStatus.Unknown, result.Status);
        Assert.Empty(handler.Methods);

        Activity root = Assert.Single(
            started.Where(a =>
                a.OperationName ==
                PathVeerActivityNames.PrefixUpdateCheck));
        Assert.Equal(
            PathVeerTagValues.OutcomeUnknown,
            root.Tags.Single(t =>
                t.Key == PathVeerTagNames.Outcome).Value);

        Assert.DoesNotContain(
            stopped,
            a => a.OperationName ==
                PathVeerActivityNames.PrefixHttpHead);
        Assert.Single(checks);
        Assert.Single(durations);
    }

    [Fact]
    public void CheckAsync_HttpFailure_EmitsFailedOutcomeAndHttpCategory()
    {
        var stopped = new ConcurrentQueue<Activity>();
        var started = new ConcurrentQueue<Activity>();
        var checks = new ConcurrentQueue<long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(started, stopped);
        using var ml = CreateMeterListener(checks, durations);

        StubHttpMessageHandler handler = new(
            _ => throw new HttpRequestException("connection refused"));
        StubMetadataService metadata = new() { Current = CreateMetadata() };

        CountryPrefixUpdateChecker checker =
            CreateChecker(handler, metadata);

        PrefixUpdateCheckResult result =
            checker.CheckAsync(CancellationToken.None).GetAwaiter()
                .GetResult();

        Assert.Equal(PrefixUpdateCheckStatus.Failed, result.Status);

        Activity root = Assert.Single(
            started.Where(a =>
                a.OperationName ==
                PathVeerActivityNames.PrefixUpdateCheck));
        Assert.Equal(
            PathVeerTagValues.Failure,
            root.Tags.Single(t =>
                t.Key == PathVeerTagNames.Outcome).Value);

        Activity head = Assert.Single(
            stopped.Where(a =>
                a.OperationName ==
                PathVeerActivityNames.PrefixHttpHead));
        Assert.Equal(
            PathVeerTagValues.Failure,
            head.Tags.Single(t =>
                t.Key == PathVeerTagNames.Outcome).Value);
        Assert.Equal(
            PathVeerTagValues.FailureHttp,
            head.Tags.Single(t =>
                t.Key == PathVeerTagNames.FailureCategory).Value);
    }

    [Fact]
    public void CheckAsync_FaultInjectionHttpRequest_RecordsFailedNoRealCall()
    {
        var stopped = new ConcurrentQueue<Activity>();
        var started = new ConcurrentQueue<Activity>();
        var checks = new ConcurrentQueue<long>();
        var durations = new ConcurrentQueue<double>();

        using var al = CreateActivityListener(started, stopped);
        using var ml = CreateMeterListener(checks, durations);

        StubHttpMessageHandler handler = new(
            _ => throw new InvalidOperationException("must not run"));
        StubMetadataService metadata = new() { Current = CreateMetadata() };

        CountryPrefixUpdateChecker checker = CreateChecker(
            handler,
            metadata,
            faultPolicy: FaultInjectionPolicy.For(
                new[] { FaultInjectionPoint.HttpRequest }));

        PrefixUpdateCheckResult result =
            checker.CheckAsync(CancellationToken.None).GetAwaiter()
                .GetResult();

        Assert.Equal(PrefixUpdateCheckStatus.Failed, result.Status);
        Assert.Empty(handler.Methods);

        Activity root = Assert.Single(
            started.Where(a =>
                a.OperationName ==
                PathVeerActivityNames.PrefixUpdateCheck));
        Assert.Equal(
            PathVeerTagValues.Failure,
            root.Tags.Single(t =>
                t.Key == PathVeerTagNames.Outcome).Value);

        // The fault is raised before any real HTTP request is attempted.
        Assert.DoesNotContain(
            stopped,
            a => a.OperationName ==
                PathVeerActivityNames.PrefixHttpHead);
    }

    [Fact]
    public void NoListener_BehaviorUnchangedAndNoException()
    {
        StubHttpMessageHandler handler = new(
            _ => CreateResponse(
                HttpStatusCode.OK,
                etag: "\"etag1\"",
                lastModified: BaseTime,
                contentLength: 512));
        StubMetadataService metadata = new() { Current = CreateMetadata() };

        CountryPrefixUpdateChecker checker =
            CreateChecker(handler, metadata);

        PrefixUpdateCheckResult result =
            checker.CheckAsync(CancellationToken.None).GetAwaiter()
                .GetResult();

        Assert.Equal(PrefixUpdateCheckStatus.Current, result.Status);
        Assert.Equal([HttpMethod.Head], handler.Methods);
    }

    [Fact]
    public void CheckAsync_NoSensitiveTags()
    {
        var stopped = new ConcurrentQueue<Activity>();
        var started = new ConcurrentQueue<Activity>();

        using var al = CreateActivityListener(started, stopped);

        StubHttpMessageHandler handler = new(
            _ => CreateResponse(
                HttpStatusCode.OK,
                etag: "\"etag1\"",
                lastModified: BaseTime,
                contentLength: 512));
        StubMetadataService metadata = new() { Current = CreateMetadata() };

        CountryPrefixUpdateChecker checker =
            CreateChecker(handler, metadata);

        checker.CheckAsync(CancellationToken.None).GetAwaiter()
            .GetResult();

        foreach (Activity a in stopped.Concat(started))
        {
            foreach (KeyValuePair<string, object?> tag in a.TagObjects)
            {
                Assert.DoesNotContain(
                    "url",
                    tag.Key,
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(
                    "domain",
                    tag.Key,
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(
                    "prefix",
                    tag.Key,
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(
                    "etag",
                    tag.Key,
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(
                    "file",
                    tag.Key,
                    StringComparison.OrdinalIgnoreCase);
                                Assert.DoesNotContain(
                    "exception",
                    tag.Key,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private sealed class StubCountryPrefixSource : ICountryPrefixSource
    {
        public PrefixSourceDescriptor GetDescriptor(
            DirectCountryCode country) =>
            new()
            {
                Id = "stub",
                DisplayName = "Stub",
                Format = "ipv4-prefix-list",
                ParserVersion = "1.0",
                Uri = $"https://example.test/{country.Code}"
            };

        public Task<PrefixSourceFetchResult> FetchAsync(
            DirectCountryCode country,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
