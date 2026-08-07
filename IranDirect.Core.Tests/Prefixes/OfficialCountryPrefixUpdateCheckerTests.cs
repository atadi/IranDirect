using IranDirect.Core.Configuration;
using System.Net;
using System.Net.Http.Headers;
using IranDirect.Core.Prefixes;

namespace IranDirect.Core.Tests.Prefixes;

public sealed class CountryPrefixUpdateCheckerTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CheckAsync_HeadSuccess_ReadsRemoteHeaders()
    {
        StubHttpMessageHandler handler = new(
            _ => CreateResponse(
                HttpStatusCode.OK,
                etag: "\"etag1\"",
                lastModified: BaseTime,
                contentLength: 512));
        StubMetadataService metadata = new()
        {
            Current = CreateMetadata()
        };
        CountryPrefixUpdateChecker checker =
            CreateChecker(
                handler,
                metadata,
                timeProvider: new FakeTimeProvider());

        PrefixUpdateCheckResult result =
            await checker.CheckAsync(CancellationToken.None);

        Assert.Equal(
            PrefixUpdateCheckStatus.Current,
            result.Status);
        Assert.Equal(
            [HttpMethod.Head],
            handler.Methods);
        Assert.Equal("\"etag1\"", result.RemoteMetadata!.ETag);
        Assert.Equal(
            BaseTime,
            result.RemoteMetadata.LastModified);
        Assert.Equal(512, result.RemoteMetadata.ContentLength);
        Assert.Equal(BaseTime, result.CheckedAt);
    }

    [Fact]
    public async Task CheckAsync_HeadUnsupported_FallsBackToGet()
    {
        StubHttpMessageHandler handler = new(
            request =>
            {
                if (request.Method == HttpMethod.Head)
                {
                    return CreateResponse(
                        HttpStatusCode.MethodNotAllowed);
                }

                return CreateResponse(
                    HttpStatusCode.OK,
                    etag: "\"etag1\"",
                    lastModified: BaseTime,
                    contentLength: 512);
            });
        StubMetadataService metadata = new()
        {
            Current = CreateMetadata()
        };
        CountryPrefixUpdateChecker checker =
            CreateChecker(handler, metadata);

        PrefixUpdateCheckResult result =
            await checker.CheckAsync(CancellationToken.None);

        Assert.Equal(
            PrefixUpdateCheckStatus.Current,
            result.Status);
        Assert.Equal(
            [HttpMethod.Head, HttpMethod.Get],
            handler.Methods);
        Assert.Equal("\"etag1\"", result.RemoteMetadata!.ETag);
    }

    [Fact]
    public async Task CheckAsync_Current_WhenAllFieldsMatch()
    {
        StubHttpMessageHandler handler = new(
            _ => CreateResponse(
                HttpStatusCode.OK,
                etag: "\"etag1\"",
                lastModified: BaseTime,
                contentLength: 512));
        StubMetadataService metadata = new()
        {
            Current = CreateMetadata()
        };

        PrefixUpdateCheckResult result =
            await CreateChecker(handler, metadata)
                .CheckAsync(CancellationToken.None);

        Assert.Equal(
            PrefixUpdateCheckStatus.Current,
            result.Status);
        Assert.NotNull(result.RemoteMetadata);
        Assert.NotNull(result.CurrentMetadata);
    }

    [Fact]
    public async Task CheckAsync_ChangedEtag_ReportsUpdateAvailable()
    {
        StubHttpMessageHandler handler = new(
            _ => CreateResponse(
                HttpStatusCode.OK,
                etag: "\"etag2\"",
                lastModified: BaseTime,
                contentLength: 512));
        StubMetadataService metadata = new()
        {
            Current = CreateMetadata()
        };

        PrefixUpdateCheckResult result =
            await CreateChecker(handler, metadata)
                .CheckAsync(CancellationToken.None);

        Assert.Equal(
            PrefixUpdateCheckStatus.UpdateAvailable,
            result.Status);
        Assert.Equal("\"etag2\"", result.RemoteMetadata!.ETag);
    }

    [Fact]
    public async Task CheckAsync_ChangedEtag_TakesPrecedenceOverLastModified()
    {
        StubHttpMessageHandler handler = new(
            _ => CreateResponse(
                HttpStatusCode.OK,
                etag: "\"etag2\"",
                lastModified: BaseTime.AddDays(-1),
                contentLength: 512));
        StubMetadataService metadata = new()
        {
            Current = CreateMetadata()
        };

        PrefixUpdateCheckResult result =
            await CreateChecker(handler, metadata)
                .CheckAsync(CancellationToken.None);

        Assert.Equal(
            PrefixUpdateCheckStatus.UpdateAvailable,
            result.Status);
    }

    [Fact]
    public async Task CheckAsync_NewerLastModified_ReportsUpdateAvailable()
    {
        StubHttpMessageHandler handler = new(
            _ => CreateResponse(
                HttpStatusCode.OK,
                etag: "\"etag1\"",
                lastModified: BaseTime.AddHours(1),
                contentLength: 512));
        StubMetadataService metadata = new()
        {
            Current = CreateMetadata()
        };

        PrefixUpdateCheckResult result =
            await CreateChecker(handler, metadata)
                .CheckAsync(CancellationToken.None);

        Assert.Equal(
            PrefixUpdateCheckStatus.UpdateAvailable,
            result.Status);
    }

    [Fact]
    public async Task CheckAsync_EqualLastModified_IsCurrent()
    {
        StubHttpMessageHandler handler = new(
            _ => CreateResponse(
                HttpStatusCode.OK,
                etag: "\"etag1\"",
                lastModified: BaseTime,
                contentLength: 512));
        StubMetadataService metadata = new()
        {
            Current = CreateMetadata()
        };

        PrefixUpdateCheckResult result =
            await CreateChecker(handler, metadata)
                .CheckAsync(CancellationToken.None);

        Assert.Equal(
            PrefixUpdateCheckStatus.Current,
            result.Status);
    }

    [Fact]
    public async Task CheckAsync_OlderLastModified_IsCurrent()
    {
        StubHttpMessageHandler handler = new(
            _ => CreateResponse(
                HttpStatusCode.OK,
                etag: "\"etag1\"",
                lastModified: BaseTime.AddDays(-1),
                contentLength: 512));
        StubMetadataService metadata = new()
        {
            Current = CreateMetadata()
        };

        PrefixUpdateCheckResult result =
            await CreateChecker(handler, metadata)
                .CheckAsync(CancellationToken.None);

        Assert.Equal(
            PrefixUpdateCheckStatus.Current,
            result.Status);
    }

    [Fact]
    public async Task CheckAsync_ChangedContentLength_ReportsUpdateAvailable()
    {
        StubHttpMessageHandler handler = new(
            _ => CreateResponse(
                HttpStatusCode.OK,
                etag: "\"etag1\"",
                lastModified: BaseTime,
                contentLength: 1024));
        StubMetadataService metadata = new()
        {
            Current = CreateMetadata()
        };

        PrefixUpdateCheckResult result =
            await CreateChecker(handler, metadata)
                .CheckAsync(CancellationToken.None);

        Assert.Equal(
            PrefixUpdateCheckStatus.UpdateAvailable,
            result.Status);
    }

    [Fact]
    public async Task CheckAsync_NoLocalMetadata_ReportsUnknownWithoutHttp()
    {
        StubHttpMessageHandler handler = new(
            _ => throw new InvalidOperationException(
                "must not be invoked"));
        StubMetadataService metadata = new()
        {
            Current = null
        };
        CountryPrefixUpdateChecker checker =
            CreateChecker(handler, metadata);

        PrefixUpdateCheckResult result =
            await checker.CheckAsync(CancellationToken.None);

        Assert.Equal(
            PrefixUpdateCheckStatus.Unknown,
            result.Status);
        Assert.Empty(handler.Methods);
        Assert.Equal(
            "No local metadata available.",
            result.Reason);
        Assert.Null(result.CurrentMetadata);
    }

    [Fact]
    public async Task CheckAsync_RemoteExposesNoHeaders_ReportsUnknown()
    {
        StubHttpMessageHandler handler = new(
            _ => CreateResponse(HttpStatusCode.OK));
        StubMetadataService metadata = new()
        {
            Current = CreateMetadata()
        };

        PrefixUpdateCheckResult result =
            await CreateChecker(handler, metadata)
                .CheckAsync(CancellationToken.None);

        Assert.Equal(
            PrefixUpdateCheckStatus.Unknown,
            result.Status);
        Assert.NotNull(result.RemoteMetadata);
        Assert.Null(result.RemoteMetadata.ETag);
        Assert.Null(result.RemoteMetadata.LastModified);
        Assert.Null(result.RemoteMetadata.ContentLength);
    }

    [Fact]
    public async Task CheckAsync_HeadNotFound_ReportsFailed()
    {
        StubHttpMessageHandler handler = new(
            _ => CreateResponse(HttpStatusCode.NotFound));
        StubMetadataService metadata = new()
        {
            Current = CreateMetadata()
        };

        PrefixUpdateCheckResult result =
            await CreateChecker(handler, metadata)
                .CheckAsync(CancellationToken.None);

        Assert.Equal(
            PrefixUpdateCheckStatus.Failed,
            result.Status);
        Assert.NotNull(result.Reason);
        Assert.Single(handler.Methods);
        Assert.Equal(HttpMethod.Head, handler.Methods[0]);
    }

    [Fact]
    public async Task CheckAsync_HeadServerError_ReportsFailed()
    {
        StubHttpMessageHandler handler = new(
            _ => CreateResponse(HttpStatusCode.InternalServerError));
        StubMetadataService metadata = new()
        {
            Current = CreateMetadata()
        };

        PrefixUpdateCheckResult result =
            await CreateChecker(handler, metadata)
                .CheckAsync(CancellationToken.None);

        Assert.Equal(
            PrefixUpdateCheckStatus.Failed,
            result.Status);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public async Task CheckAsync_Timeout_ReportsFailed()
    {
        StubHttpMessageHandler handler = new(
            _ => CreateResponse(HttpStatusCode.OK),
            delay: TimeSpan.FromSeconds(30));
        StubMetadataService metadata = new()
        {
            Current = CreateMetadata()
        };

        PrefixUpdateCheckResult result =
            await CreateChecker(
                    handler,
                    metadata,
                    timeout: TimeSpan.FromMilliseconds(100))
                .CheckAsync(CancellationToken.None);

        Assert.Equal(
            PrefixUpdateCheckStatus.Failed,
            result.Status);
        Assert.NotNull(result.Reason);
        Assert.Contains("timed out", result.Reason);
    }

    [Fact]
    public async Task CheckAsync_Cancellation_Rethrows()
    {
        StubHttpMessageHandler handler = new(
            _ => CreateResponse(HttpStatusCode.OK));
        StubMetadataService metadata = new()
        {
            Current = CreateMetadata()
        };
        CountryPrefixUpdateChecker checker =
            CreateChecker(handler, metadata);

        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => checker.CheckAsync(cts.Token));
    }

    [Fact]
    public async Task CheckAsync_NetworkException_ReportsFailed()
    {
        StubHttpMessageHandler handler = new(
            _ => throw new HttpRequestException(
                "connection refused"));
        StubMetadataService metadata = new()
        {
            Current = CreateMetadata()
        };

        PrefixUpdateCheckResult result =
            await CreateChecker(handler, metadata)
                .CheckAsync(CancellationToken.None);

        Assert.Equal(
            PrefixUpdateCheckStatus.Failed,
            result.Status);
        Assert.Contains(
            "connection refused",
            result.Reason);
        Assert.NotNull(result.CurrentMetadata);
    }

    [Fact]
    public async Task CheckAsync_MetadataUnavailable_ReportsUnknown()
    {
        StubHttpMessageHandler handler = new(
            _ => throw new InvalidOperationException(
                "must not be invoked"));
        StubMetadataService metadata = new()
        {
            ExceptionToThrow = new IOException(
                "metadata disk unreadable")
        };

        PrefixUpdateCheckResult result =
            await CreateChecker(handler, metadata)
                .CheckAsync(CancellationToken.None);

        Assert.Equal(
            PrefixUpdateCheckStatus.Unknown,
            result.Status);
        Assert.Contains(
            "metadata disk unreadable",
            result.Reason);
        Assert.Empty(handler.Methods);
    }

    [Fact]
    public async Task CheckAsync_DoesNotWriteAnyFiles()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            string metadataPath = Path.Combine(
                tempDir,
                "prefixes",
                "IR",
                "metadata.json");
            string prefixPath = Path.Combine(
                tempDir,
                "prefixes",
                "IR",
                "ipv4-prefixes.txt");

            FakeTimeProvider clock = new();
            PrefixSourceMetadataService metadataService =
                new(
                    new CountryPrefixStore(tempDir),
                    clock);

            clock.Now = BaseTime;
            await metadataService.RecordSuccessAsync(
                DirectCountryCode.IR,
                CreateFetchResult());

            byte[] metadataBefore =
                await File.ReadAllBytesAsync(metadataPath);

            StubHttpMessageHandler handler = new(
                _ => CreateResponse(
                    HttpStatusCode.OK,
                    etag: "\"etag1\"",
                    lastModified: BaseTime,
                    contentLength: 512));

            CountryPrefixUpdateChecker checker =
                CreateChecker(handler, metadataService);

            PrefixUpdateCheckResult result =
                await checker.CheckAsync(CancellationToken.None);

            Assert.Equal(
                PrefixUpdateCheckStatus.Current,
                result.Status);

            byte[] metadataAfter =
                await File.ReadAllBytesAsync(metadataPath);

            Assert.Equal(metadataBefore, metadataAfter);
            Assert.False(File.Exists(prefixPath));
            Assert.Single(
                Directory.EnumerateFiles(tempDir, "*", SearchOption.AllDirectories));
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // best effort cleanup
            }
        }
    }

    private static CountryPrefixUpdateChecker CreateChecker(
        HttpMessageHandler handler,
        IPrefixSourceMetadataService metadataService,
        TimeSpan? timeout = null,
        TimeProvider? timeProvider = null,
        ICountryPrefixSource? source = null) =>
        new(
            source ?? new StubCountryPrefixSource(),
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
            timeProvider);

    private static PrefixSourceMetadata CreateMetadata() =>
        new()
        {
            SourceId = "ripe-stat-country-resource-list-ipv4",
            SourceDisplayName = "RIPEstat Iran IPv4 country resource list",
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
            response.Headers.ETag =
                new EntityTagHeaderValue(etag);
        }

        if (lastModified is not null)
        {
            response.Content.Headers.LastModified =
                lastModified;
        }

        if (contentLength is not null)
        {
            response.Content.Headers.ContentLength =
                contentLength;
        }
        else
        {
            response.Content.Headers.ContentLength = null;
        }

        return response;
    }

    private static PrefixSourceFetchResult CreateFetchResult()
    {
        string[] prefixes =
        [
            "1.2.3.0/24",
            "10.0.0.0/8"
        ];

        return new PrefixSourceFetchResult
        {
            Source = new PrefixSourceDescriptor
            {
                Id = "ripe-stat-country-resource-list-ipv4",
                DisplayName =
                    "RIPEstat Iran IPv4 country resource list",
                Uri =
                    "https://stat.ripe.net/data/country-resource-list/data.json?resource=IR",
                Format = "ripestat-country-resource-list-json",
                ParserVersion = "1"
            },
            Prefixes = prefixes,
            StartedAt = BaseTime,
            CompletedAt = BaseTime.AddSeconds(4),
            Duration = TimeSpan.FromSeconds(4),
            ETag = "\"etag1\"",
            ContentHash =
                PrefixContentHasher.ComputeHash(prefixes),
            ContentLength = 512,
            LastModified = BaseTime
        };
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

    private sealed class StubHttpMessageHandler :
        HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage,
            HttpResponseMessage> _responder;
        private readonly TimeSpan? _delay;

        public StubHttpMessageHandler(
            Func<HttpRequestMessage,
                HttpResponseMessage> responder,
            TimeSpan? delay = null)
        {
            _responder = responder;
            _delay = delay;
        }

        public List<HttpMethod> Methods { get; } = [];

        protected override async Task<HttpResponseMessage>
            SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_delay is { } delay)
            {
                await Task.Delay(delay, cancellationToken);
            }

            Methods.Add(request.Method);

            return _responder(request);
        }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = BaseTime;

        public override DateTimeOffset GetUtcNow() => Now;
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
