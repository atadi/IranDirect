using IranDirect.Core.Prefixes;

namespace IranDirect.Core.Tests.Prefixes;

public sealed class PrefixSourceMetadataServiceTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetCurrentAsync_NoPriorState_ReturnsNull()
    {
        PrefixSourceMetadataService service =
            CreateService(out _);

        PrefixSourceMetadata? metadata =
            await service.GetCurrentAsync();

        Assert.Null(metadata);
    }

    [Fact]
    public async Task RecordSuccessAsync_StoresSucceededMetadata()
    {
        FakeTimeProvider clock = new();
        PrefixSourceMetadataService service =
            CreateService(out _, clock);
        clock.Now = BaseTime;

        await service.RecordSuccessAsync(CreateFetchResult());

        PrefixSourceMetadata? metadata =
            await service.GetCurrentAsync();

        Assert.NotNull(metadata);
        Assert.Equal(
            PrefixSourceUpdateStatus.Succeeded,
            metadata.LastStatus);
        Assert.Equal(BaseTime, metadata.LastAttemptedAt);
        Assert.Equal(BaseTime, metadata.LastSucceededAt);
        Assert.Equal(2, metadata.PrefixCount);
        Assert.Equal(64, metadata.ContentHash.Length);
        Assert.Equal("\"etag1\"", metadata.ETag);
        Assert.Equal(512, metadata.ContentLength);
        Assert.Equal(
            TimeSpan.FromSeconds(4),
            metadata.DownloadDuration);
        Assert.Null(metadata.LastError);
    }

    [Fact]
    public async Task RecordSuccessAsync_ReplacesPriorFailure()
    {
        FakeTimeProvider clock = new();
        PrefixSourceMetadataService service =
            CreateService(out _, clock);

        clock.Now = BaseTime;
        await service.RecordFailureAsync(
            CreateDescriptor(),
            "first failure");
        clock.Now = BaseTime.AddHours(1);
        await service.RecordSuccessAsync(CreateFetchResult());

        PrefixSourceMetadata? metadata =
            await service.GetCurrentAsync();

        Assert.NotNull(metadata);
        Assert.Equal(
            PrefixSourceUpdateStatus.Succeeded,
            metadata.LastStatus);
        Assert.Equal(2, metadata.PrefixCount);
        Assert.Null(metadata.LastError);
        Assert.Equal(
            BaseTime.AddHours(1),
            metadata.LastSucceededAt);
    }

    [Fact]
    public async Task RecordSuccessAsync_AllTimestampsUseInjectedClock()
    {
        FakeTimeProvider clock = new();
        PrefixSourceMetadataService service =
            CreateService(out _, clock);
        clock.Now = BaseTime.AddHours(2);

        await service.RecordSuccessAsync(CreateFetchResult());

        PrefixSourceMetadata? metadata =
            await service.GetCurrentAsync();

        Assert.Equal(
            BaseTime.AddHours(2),
            metadata.LastAttemptedAt);
        Assert.Equal(
            BaseTime.AddHours(2),
            metadata.LastSucceededAt);
    }

    [Fact]
    public async Task RecordNotModifiedAsync_PreservesSuccessfulDataset()
    {
        FakeTimeProvider clock = new();
        PrefixSourceMetadataService service =
            CreateService(out _, clock);

        clock.Now = BaseTime;
        await service.RecordSuccessAsync(CreateFetchResult());

        clock.Now = BaseTime.AddDays(1);
        await service.RecordNotModifiedAsync(
            CreateFetchResult(
                notModified: true,
                etag: "\"etag2\""));

        PrefixSourceMetadata? metadata =
            await service.GetCurrentAsync();

        Assert.NotNull(metadata);
        Assert.Equal(
            PrefixSourceUpdateStatus.NotModified,
            metadata.LastStatus);
        Assert.Equal(2, metadata.PrefixCount);
        Assert.Equal(
            64,
            metadata.ContentHash.Length);
        Assert.Equal(
            BaseTime,
            metadata.LastSucceededAt);
        Assert.Equal(
            BaseTime.AddDays(1),
            metadata.LastAttemptedAt);
        Assert.Equal("\"etag2\"", metadata.ETag);
        Assert.Null(metadata.LastError);
    }

    [Fact]
    public async Task RecordNotModifiedAsync_NoPriorState_CreatesNotModified()
    {
        PrefixSourceMetadataService service =
            CreateService(out _, new FakeTimeProvider());

        await service.RecordNotModifiedAsync(
            CreateFetchResult(notModified: true));

        PrefixSourceMetadata? metadata =
            await service.GetCurrentAsync();

        Assert.NotNull(metadata);
        Assert.Equal(
            PrefixSourceUpdateStatus.NotModified,
            metadata.LastStatus);
        Assert.Equal(0, metadata.PrefixCount);
        Assert.Null(metadata.LastSucceededAt);
    }

    [Fact]
    public async Task RecordFailureAsync_PreservesLastSuccessfulFields()
    {
        FakeTimeProvider clock = new();
        PrefixSourceMetadataService service =
            CreateService(out _, clock);

        clock.Now = BaseTime;
        await service.RecordSuccessAsync(CreateFetchResult());

        clock.Now = BaseTime.AddDays(1);
        await service.RecordFailureAsync(
            CreateDescriptor(),
            "download failed");

        PrefixSourceMetadata? metadata =
            await service.GetCurrentAsync();

        Assert.NotNull(metadata);
        Assert.Equal(
            PrefixSourceUpdateStatus.Failed,
            metadata.LastStatus);
        Assert.Equal(2, metadata.PrefixCount);
        Assert.Equal(
            BaseTime,
            metadata.LastSucceededAt);
        Assert.Equal(
            BaseTime.AddDays(1),
            metadata.LastAttemptedAt);
        Assert.Equal("download failed", metadata.LastError);
    }

    [Fact]
    public async Task RecordFailureAsync_NoPriorState_CreatesFailed()
    {
        PrefixSourceMetadataService service =
            CreateService(out _, new FakeTimeProvider());

        await service.RecordFailureAsync(
            CreateDescriptor(),
            "boom");

        PrefixSourceMetadata? metadata =
            await service.GetCurrentAsync();

        Assert.NotNull(metadata);
        Assert.Equal(
            PrefixSourceUpdateStatus.Failed,
            metadata.LastStatus);
        Assert.Equal("boom", metadata.LastError);
        Assert.Null(metadata.LastSucceededAt);
    }

    [Fact]
    public async Task RecordFailureAsync_TruncatesLongError()
    {
        PrefixSourceMetadataService service =
            CreateService(out _, new FakeTimeProvider());

        string longError = new('x', 1000);

        await service.RecordFailureAsync(
            CreateDescriptor(),
            longError);

        PrefixSourceMetadata? metadata =
            await service.GetCurrentAsync();

        Assert.NotNull(metadata);
        Assert.Equal(500, metadata.LastError.Length);
    }

    [Fact]
    public async Task RecordFailureAsync_EmptyError_StoresNull()
    {
        PrefixSourceMetadataService service =
            CreateService(out _, new FakeTimeProvider());

        await service.RecordFailureAsync(
            CreateDescriptor(),
            "   ");

        PrefixSourceMetadata? metadata =
            await service.GetCurrentAsync();

        Assert.NotNull(metadata);
        Assert.Equal(
            PrefixSourceUpdateStatus.Failed,
            metadata.LastStatus);
        Assert.Null(metadata.LastError);
    }

    [Fact]
    public async Task RecordSuccessAsync_InvalidSource_ThrowsAndPersistsNothing()
    {
        PrefixSourceMetadataService service =
            CreateService(out _);

        PrefixSourceFetchResult result = CreateFetchResult()
            with { Source = CreateDescriptor() with { Id = "" } };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RecordSuccessAsync(result));

        Assert.Null(await service.GetCurrentAsync());
    }

    [Fact]
    public async Task RecordFailureAsync_InvalidSource_ThrowsAndPersistsNothing()
    {
        PrefixSourceMetadataService service =
            CreateService(out _);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RecordFailureAsync(
                CreateDescriptor() with { DisplayName = "" },
                "boom"));

        Assert.Null(await service.GetCurrentAsync());
    }

    [Fact]
    public async Task RecordSuccessAsync_NegativeContentLength_Throws()
    {
        PrefixSourceMetadataService service =
            CreateService(out _);

        PrefixSourceFetchResult result = CreateFetchResult()
            with { ContentLength = -1 };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RecordSuccessAsync(result));
    }

    private static PrefixSourceMetadataService CreateService(
        out PrefixSourceMetadataRepository repository,
        TimeProvider? timeProvider = null)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"),
            "prefix-source-metadata.json");

        repository = new PrefixSourceMetadataRepository(
            new PrefixSourceMetadataStore(path),
            new PrefixSourceMetadataValidator());

        return new PrefixSourceMetadataService(
            repository,
            timeProvider);
    }

    private static PrefixSourceFetchResult CreateFetchResult(
        bool notModified = false,
        string? etag = "\"etag1\"")
    {
        if (notModified)
        {
            return new PrefixSourceFetchResult
            {
                Source = CreateDescriptor(),
                Prefixes = [],
                StartedAt = BaseTime,
                CompletedAt = BaseTime.AddSeconds(1),
                Duration = TimeSpan.FromSeconds(1),
                ETag = etag,
                NotModified = true
            };
        }

        string[] prefixes =
        [
            "1.2.3.0/24",
            "10.0.0.0/8"
        ];

        return new PrefixSourceFetchResult
        {
            Source = CreateDescriptor(),
            Prefixes = prefixes,
            StartedAt = BaseTime,
            CompletedAt = BaseTime.AddSeconds(4),
            Duration = TimeSpan.FromSeconds(4),
            ETag = etag,
            ContentHash =
                PrefixContentHasher.ComputeHash(prefixes),
            ContentLength = 512,
            LastModified = BaseTime
        };
    }

    private static PrefixSourceDescriptor CreateDescriptor() =>
        new()
        {
            Id = "test-source",
            DisplayName = "Test Source",
            Uri = "https://example.test/data.json",
            Format = "example-json",
            ParserVersion = "1"
        };

    private sealed class FakeTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = BaseTime;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
