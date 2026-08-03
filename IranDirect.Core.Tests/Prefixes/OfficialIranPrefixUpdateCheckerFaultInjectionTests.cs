using System.Net;
using System.Net.Http.Headers;
using IranDirect.Core.Prefixes;
using IranDirect.Core.Testing.FaultInjection;

namespace IranDirect.Core.Tests.Prefixes;

public sealed class OfficialIranPrefixUpdateCheckerFaultInjectionTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CheckAsync_HeadFault_ReturnsFailed()
    {
        RecordingHandler handler = new(
            _ => OkResponse());
        OfficialIranPrefixUpdateChecker checker = CreateChecker(
            handler,
            faultPolicy: FaultInjectionPolicy.For(
                [FaultInjectionPoint.HttpRequest]));

        PrefixUpdateCheckResult result =
            await checker.CheckAsync(CancellationToken.None);

        Assert.Equal(
            PrefixUpdateCheckStatus.Failed,
            result.Status);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task CheckAsync_HeadFault_ZeroHandlerRequests()
    {
        RecordingHandler handler = new(
            _ => OkResponse());
        OfficialIranPrefixUpdateChecker checker = CreateChecker(
            handler,
            faultPolicy: FaultInjectionPolicy.For(
                [FaultInjectionPoint.HttpRequest]));

        await checker.CheckAsync(CancellationToken.None);

        Assert.Equal(0, handler.RequestCount);
        Assert.Empty(handler.Methods);
    }

    [Fact]
    public async Task CheckAsync_GetFault_AfterHeadUnsupported_ReturnsFailed()
    {
        RecordingHandler handler = new(
            request =>
            {
                if (request.Method == HttpMethod.Head)
                {
                    return new HttpResponseMessage(
                        HttpStatusCode.MethodNotAllowed);
                }

                return OkResponse();
            });
        OfficialIranPrefixUpdateChecker checker = CreateChecker(
            handler,
            faultPolicy: new FailsOnSecondCheckPolicy());

        PrefixUpdateCheckResult result =
            await checker.CheckAsync(CancellationToken.None);

        Assert.Equal(
            PrefixUpdateCheckStatus.Failed,
            result.Status);
        Assert.Equal([HttpMethod.Head], handler.Methods);
    }

    [Fact]
    public async Task CheckAsync_GetFault_PreventsGetHandlerRequest()
    {
        RecordingHandler handler = new(
            request =>
            {
                if (request.Method == HttpMethod.Head)
                {
                    return new HttpResponseMessage(
                        HttpStatusCode.MethodNotAllowed);
                }

                return OkResponse();
            });
        OfficialIranPrefixUpdateChecker checker = CreateChecker(
            handler,
            faultPolicy: new FailsOnSecondCheckPolicy());

        await checker.CheckAsync(CancellationToken.None);

        Assert.Equal(
            [HttpMethod.Head],
            handler.Methods);
        Assert.DoesNotContain(HttpMethod.Get, handler.Methods);
    }

    [Fact]
    public async Task CheckAsync_HeadFault_IsNotRetried()
    {
        RecordingHandler handler = new(
            _ => OkResponse());
        OfficialIranPrefixUpdateChecker checker = CreateChecker(
            handler,
            faultPolicy: FaultInjectionPolicy.For(
                [FaultInjectionPoint.HttpRequest]));

        await checker.CheckAsync(CancellationToken.None);

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task CheckAsync_NextCheck_SucceedsAfterFault()
    {
        RecordingHandler handler = new(
            _ => OkResponse());
        OfficialIranPrefixUpdateChecker faulted = CreateChecker(
            handler,
            faultPolicy: FaultInjectionPolicy.For(
                [FaultInjectionPoint.HttpRequest]));

        await faulted.CheckAsync(CancellationToken.None);

        OfficialIranPrefixUpdateChecker checker = CreateChecker(handler);

        PrefixUpdateCheckResult result =
            await checker.CheckAsync(CancellationToken.None);

        Assert.Equal(
            PrefixUpdateCheckStatus.Current,
            result.Status);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task CheckAsync_Cancellation_StillPropagates()
    {
        StubMetadataService metadata = new()
        {
            ExceptionToThrow =
                new OperationCanceledException()
        };
        OfficialIranPrefixUpdateChecker checker = CreateChecker(
            new RecordingHandler(_ => OkResponse()),
            metadata);

        using CancellationTokenSource source = new();
        source.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => checker.CheckAsync(source.Token));
    }

    [Fact]
    public async Task CheckAsync_FailedReason_IsConciseWithoutStackTrace()
    {
        RecordingHandler handler = new(
            _ => OkResponse());
        OfficialIranPrefixUpdateChecker checker = CreateChecker(
            handler,
            faultPolicy: FaultInjectionPolicy.For(
                [FaultInjectionPoint.HttpRequest]));

        PrefixUpdateCheckResult result =
            await checker.CheckAsync(CancellationToken.None);

        Assert.NotNull(result.Reason);
        Assert.Contains("Remote check failed", result.Reason);
        Assert.DoesNotContain("at IranDirect", result.Reason);
        Assert.DoesNotContain("Stack trace", result.Reason);
    }

    [Fact]
    public async Task CheckAsync_WithoutFaults_CurrentBehaviorUnchanged()
    {
        RecordingHandler handler = new(
            _ => OkResponse());
        OfficialIranPrefixUpdateChecker checker = CreateChecker(handler);

        PrefixUpdateCheckResult result =
            await checker.CheckAsync(CancellationToken.None);

        Assert.Equal(
            PrefixUpdateCheckStatus.Current,
            result.Status);
        Assert.Equal([HttpMethod.Head], handler.Methods);
        Assert.NotNull(result.RemoteMetadata);
    }

    [Fact]
    public async Task CheckAsync_WithoutFaults_UpdateAvailableUnchanged()
    {
        RecordingHandler handler = new(
            _ => OkResponse(etag: "\"etag2\""));
        StubMetadataService metadata = new()
        {
            Current = CreateMetadata(etag: "\"etag1\"")
        };
        OfficialIranPrefixUpdateChecker checker =
            CreateChecker(handler, metadata);

        PrefixUpdateCheckResult result =
            await checker.CheckAsync(CancellationToken.None);

        Assert.Equal(
            PrefixUpdateCheckStatus.UpdateAvailable,
            result.Status);
    }

    [Fact]
    public async Task CheckAsync_WithoutFaults_UnknownUnchanged()
    {
        StubMetadataService metadata = new()
        {
            Current = null
        };
        OfficialIranPrefixUpdateChecker checker =
            CreateChecker(new RecordingHandler(_ => OkResponse()), metadata);

        PrefixUpdateCheckResult result =
            await checker.CheckAsync(CancellationToken.None);

        Assert.Equal(
            PrefixUpdateCheckStatus.Unknown,
            result.Status);
    }

    [Fact]
    public async Task CheckAsync_AmbientScope_TakesPrecedenceOverInjectedPolicy()
    {
        RecordingHandler handler = new(
            _ => OkResponse());
        OfficialIranPrefixUpdateChecker checker = CreateChecker(
            handler,
            faultPolicy: FaultInjectionPolicy.For(
                [FaultInjectionPoint.FileRead]));

        using (FaultInjectionScope.Fail(FaultInjectionPoint.HttpRequest))
        {
            PrefixUpdateCheckResult result =
                await checker.CheckAsync(CancellationToken.None);

            Assert.Equal(
                PrefixUpdateCheckStatus.Failed,
                result.Status);
        }

        Assert.Equal(0, handler.RequestCount);
    }

    private static OfficialIranPrefixUpdateChecker CreateChecker(
        RecordingHandler handler,
        StubMetadataService? metadata = null,
        IFaultInjectionPolicy? faultPolicy = null)
    {
        return new OfficialIranPrefixUpdateChecker(
            new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            },
            metadata ?? new StubMetadataService
            {
                Current = CreateMetadata()
            },
            new PrefixUpdateCheckOptions
            {
                Timeout = TimeSpan.FromSeconds(5)
            },
            faultPolicy: faultPolicy);
    }

    private static PrefixSourceMetadata CreateMetadata(string? etag = null) =>
        new()
        {
            SourceId = "ripe-stat-country-resource-list-ipv4",
            SourceDisplayName = "RIPEstat Iran IPv4 country resource list",
            ETag = etag ?? "\"etag1\"",
            SourceLastModified = BaseTime,
            ContentLength = 512
        };

    private static HttpResponseMessage OkResponse(string? etag = null)
    {
        HttpResponseMessage response = new(HttpStatusCode.OK);

        if (etag is not null)
        {
            response.Headers.ETag =
                new EntityTagHeaderValue(etag);
        }

        response.Content.Headers.ContentLength = 512;
        response.Content.Headers.LastModified = BaseTime;

        return response;
    }

    private sealed class FailsOnSecondCheckPolicy :
        IFaultInjectionPolicy
    {
        private int _checks;

        public bool ShouldFail(FaultInjectionPoint point)
        {
            _checks++;

            return _checks == 2;
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage,
            HttpResponseMessage> _responder;

        public RecordingHandler(
            Func<HttpRequestMessage,
                HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public int RequestCount { get; private set; }

        public List<HttpMethod> Methods { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            Methods.Add(request.Method);

            return Task.FromResult(
                _responder(request));
        }
    }

    private sealed class StubMetadataService :
        IPrefixSourceMetadataService
    {
        public PrefixSourceMetadata? Current { get; set; }
        public Exception? ExceptionToThrow { get; set; }

        public Task<PrefixSourceMetadata?> GetCurrentAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(Current);
        }

        public Task<PrefixSourceChangeSummary?>
            GetLatestChangeSummaryAsync(
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RecordSuccessAsync(
            PrefixSourceFetchResult result,
            IReadOnlyList<string>? previousPrefixes = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RecordNotModifiedAsync(
            PrefixSourceFetchResult result,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RecordFailureAsync(
            PrefixSourceDescriptor source,
            string error,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
