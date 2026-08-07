using IranDirect.Core.Configuration;
using System.Net;
using System.Net.Http.Headers;
using IranDirect.Core.Prefixes;
using IranDirect.Core.Testing.FaultInjection;

namespace IranDirect.Core.Tests.Prefixes;

public sealed class CountryPrefixUpdateCheckerFaultInjectionTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CheckAsync_HeadFault_ReturnsFailed()
    {
        RecordingHandler handler = new(
            _ => OkResponse());
        CountryPrefixUpdateChecker checker = CreateChecker(
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
        CountryPrefixUpdateChecker checker = CreateChecker(
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
        CountryPrefixUpdateChecker checker = CreateChecker(
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
        CountryPrefixUpdateChecker checker = CreateChecker(
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
        CountryPrefixUpdateChecker checker = CreateChecker(
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
        CountryPrefixUpdateChecker faulted = CreateChecker(
            handler,
            faultPolicy: FaultInjectionPolicy.For(
                [FaultInjectionPoint.HttpRequest]));

        await faulted.CheckAsync(CancellationToken.None);

        CountryPrefixUpdateChecker checker = CreateChecker(handler);

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
        CountryPrefixUpdateChecker checker = CreateChecker(
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
        CountryPrefixUpdateChecker checker = CreateChecker(
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
        CountryPrefixUpdateChecker checker = CreateChecker(handler);

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
        CountryPrefixUpdateChecker checker =
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
        CountryPrefixUpdateChecker checker =
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
        CountryPrefixUpdateChecker checker = CreateChecker(
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

    private static CountryPrefixUpdateChecker CreateChecker(
        RecordingHandler handler,
        StubMetadataService? metadata = null,
        IFaultInjectionPolicy? faultPolicy = null)
    {
        return new CountryPrefixUpdateChecker(
            new StubCountryPrefixSource(),
            metadata ?? new StubMetadataService
            {
                Current = CreateMetadata()
            },
            new PrefixUpdateCheckOptions
            {
                Timeout = TimeSpan.FromSeconds(5)
            },
            new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            },
            () => DirectCountryCode.IR,
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
            DirectCountryCode country,
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
                DirectCountryCode country,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RecordSuccessAsync(
            DirectCountryCode country,
            PrefixSourceFetchResult result,
            IReadOnlyList<string>? previousPrefixes = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RecordNotModifiedAsync(
            DirectCountryCode country,
            PrefixSourceFetchResult result,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RecordFailureAsync(
            DirectCountryCode country,
            PrefixSourceDescriptor source,
            string error,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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
