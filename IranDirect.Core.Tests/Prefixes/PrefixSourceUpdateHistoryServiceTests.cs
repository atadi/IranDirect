using IranDirect.Core.Prefixes;

namespace IranDirect.Core.Tests.Prefixes;

public sealed class PrefixSourceUpdateHistoryServiceTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetRecentAsync_NoPriorState_ReturnsEmpty()
    {
        PrefixSourceUpdateHistoryService service =
            CreateService(out _);

        Assert.Empty(await service.GetRecentAsync());
    }

    [Fact]
    public async Task GetRecentAsync_DefaultLimitUsesRetention()
    {
        FakeTimeProvider clock = new();
        PrefixSourceUpdateHistoryService service =
            CreateService(out _, retention: 5, timeProvider: clock);
        clock.Now = BaseTime;

        for (int attempt = 0; attempt < 8; attempt++)
        {
            await service.RecordSuccessAsync(
                CreateFetchResult(attempt: attempt));
        }

        IReadOnlyList<PrefixSourceUpdateHistoryEntry> recent =
            await service.GetRecentAsync();

        Assert.Equal(5, recent.Count);
        Assert.Equal(
            7,
            recent[0].StartedAt.Minute - BaseTime.Minute);
        Assert.Equal(
            3,
            recent[^1].StartedAt.Minute - BaseTime.Minute);
    }

    [Fact]
    public async Task GetRecentAsync_LimitClampedToRetention()
    {
        PrefixSourceUpdateHistoryService service =
            CreateService(out _, retention: 3);

        for (int attempt = 0; attempt < 5; attempt++)
        {
            await service.RecordSuccessAsync(
                CreateFetchResult(attempt: attempt));
        }

        IReadOnlyList<PrefixSourceUpdateHistoryEntry> recent =
            await service.GetRecentAsync(100);

        Assert.Equal(3, recent.Count);
    }

    [Fact]
    public async Task RecordSuccessAsync_AppendsSucceededEntry()
    {
        FakeTimeProvider clock = new();
        PrefixSourceUpdateHistoryService service =
            CreateService(out _, timeProvider: clock);
        clock.Now = BaseTime;

        PrefixSourceChangeSummary summary = CreateChangeSummary();

        await service.RecordSuccessAsync(
            CreateFetchResult(),
            summary);

        IReadOnlyList<PrefixSourceUpdateHistoryEntry> recent =
            await service.GetRecentAsync();

        PrefixSourceUpdateHistoryEntry entry =
            Assert.Single(recent);

        Assert.Equal(
            PrefixSourceUpdateStatus.Succeeded,
            entry.Status);
        Assert.Equal("test-source", entry.SourceId);
        Assert.Equal("Test Source", entry.SourceDisplayName);
        Assert.Equal(2, entry.PrefixCount);
        Assert.Equal(1, entry.AddedCount);
        Assert.Equal(0, entry.RemovedCount);
        Assert.Equal(1, entry.UnchangedCount);
        Assert.True(entry.HasChanges);
        Assert.Equal(BaseTime, entry.AttemptedAt);
        Assert.Equal(
            CreateFetchResult().StartedAt,
            entry.StartedAt);
        Assert.Equal(
            CreateFetchResult().CompletedAt,
            entry.CompletedAt);
        Assert.Equal(
            TimeSpan.FromSeconds(4),
            entry.Duration);
        Assert.Null(entry.Error);
    }

    [Fact]
    public async Task RecordSuccessAsync_FirstImportWithoutSummary_BuildsFallback()
    {
        PrefixSourceUpdateHistoryService service =
            CreateService(out _, timeProvider: new FakeTimeProvider());

        await service.RecordSuccessAsync(
            CreateFetchResult(),
            changeSummary: null,
            previousPrefixes: null);

        PrefixSourceUpdateHistoryEntry entry =
            Assert.Single(await service.GetRecentAsync());

        Assert.Equal(2, entry.AddedCount);
        Assert.Equal(0, entry.RemovedCount);
        Assert.Equal(0, entry.UnchangedCount);
        Assert.True(entry.HasChanges);
        Assert.Equal(
            PrefixContentHasher.ComputeHash(
                CreateFetchResult().Prefixes),
            entry.CurrentContentHash);
    }

    [Fact]
    public async Task RecordSuccessAsync_FallbackWithPreviousPrefixes_ComputesDiff()
    {
        PrefixSourceUpdateHistoryService service =
            CreateService(out _, timeProvider: new FakeTimeProvider());

        string[] previous =
        [
            "1.2.3.0/24",
            "10.0.0.0/8"
        ];
        string[] current =
        [
            "1.2.3.0/24",
            "5.6.7.0/24"
        ];

        await service.RecordSuccessAsync(
            CreateFetchResult(prefixes: current),
            changeSummary: null,
            previousPrefixes: previous);

        PrefixSourceUpdateHistoryEntry entry =
            Assert.Single(await service.GetRecentAsync());

        Assert.Equal(1, entry.AddedCount);
        Assert.Equal(1, entry.RemovedCount);
        Assert.Equal(1, entry.UnchangedCount);
        Assert.True(entry.HasChanges);
        Assert.Equal(
            PrefixContentHasher.ComputeHash(previous),
            entry.PreviousContentHash);
    }

    [Fact]
    public async Task RecordNotModifiedAsync_AppendsNotModifiedEntry()
    {
        FakeTimeProvider clock = new();
        PrefixSourceUpdateHistoryService service =
            CreateService(out _, timeProvider: clock);
        clock.Now = BaseTime;

        string hash =
            PrefixContentHasher.ComputeHash(
                CreateFetchResult().Prefixes);

        await service.RecordNotModifiedAsync(
            CreateFetchResult(notModified: true),
            currentPrefixCount: 2,
            currentContentHash: hash);

        PrefixSourceUpdateHistoryEntry entry =
            Assert.Single(await service.GetRecentAsync());

        Assert.Equal(
            PrefixSourceUpdateStatus.NotModified,
            entry.Status);
        Assert.Equal(2, entry.PrefixCount);
        Assert.Equal(2, entry.UnchangedCount);
        Assert.Equal(0, entry.AddedCount);
        Assert.Equal(0, entry.RemovedCount);
        Assert.False(entry.HasChanges);
        Assert.Equal(hash, entry.CurrentContentHash);
        Assert.Equal(hash, entry.PreviousContentHash);
        Assert.Null(entry.Error);
    }

    [Fact]
    public async Task RecordFailureAsync_AppendsFailedEntry()
    {
        FakeTimeProvider clock = new();
        PrefixSourceUpdateHistoryService service =
            CreateService(out _, timeProvider: clock);
        clock.Now = BaseTime;

        await service.RecordFailureAsync(
            CreateDescriptor(),
            "network down");

        PrefixSourceUpdateHistoryEntry entry =
            Assert.Single(await service.GetRecentAsync());

        Assert.Equal(
            PrefixSourceUpdateStatus.Failed,
            entry.Status);
        Assert.Equal("network down", entry.Error);
        Assert.Equal(BaseTime, entry.AttemptedAt);
        Assert.Equal(BaseTime, entry.StartedAt);
        Assert.Equal(BaseTime, entry.CompletedAt);
        Assert.Equal(TimeSpan.Zero, entry.Duration);
        Assert.Equal(0, entry.PrefixCount);
        Assert.False(entry.HasChanges);
    }

    [Fact]
    public async Task RecordFailureAsync_SanitizesAndTruncatesError()
    {
        PrefixSourceUpdateHistoryService service =
            CreateService(out _, timeProvider: new FakeTimeProvider());

        string error =
            "line1\r\n  line2\tline3 " + new string('x', 600);

        await service.RecordFailureAsync(
            CreateDescriptor(),
            error);

        PrefixSourceUpdateHistoryEntry entry =
            Assert.Single(await service.GetRecentAsync());

        Assert.Equal(512, entry.Error!.Length);
        Assert.DoesNotContain('\r', entry.Error);
        Assert.DoesNotContain('\n', entry.Error);
        Assert.DoesNotContain('\t', entry.Error);
    }

    [Fact]
    public async Task RecordFailureAsync_EmptyError_Throws()
    {
        PrefixSourceUpdateHistoryService service =
            CreateService(out _);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.RecordFailureAsync(
                CreateDescriptor(),
                "   "));

        Assert.Empty(await service.GetRecentAsync());
    }

    [Fact]
    public async Task RecordSuccessAsync_InvalidSource_ThrowsAndPersistsNothing()
    {
        PrefixSourceUpdateHistoryService service =
            CreateService(out _);

        PrefixSourceFetchResult result = CreateFetchResult()
            with { Source = CreateDescriptor() with { Id = "" } };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RecordSuccessAsync(result));

        Assert.Empty(await service.GetRecentAsync());
    }

    [Fact]
    public async Task RecordNotModifiedAsync_InvalidSource_ThrowsAndPersistsNothing()
    {
        PrefixSourceUpdateHistoryService service =
            CreateService(out _);

        PrefixSourceFetchResult result = CreateFetchResult(
            notModified: true)
            with { Source = CreateDescriptor() with { Id = "" } };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RecordNotModifiedAsync(result));

        Assert.Empty(await service.GetRecentAsync());
    }

    [Fact]
    public async Task RecordFailureAsync_InvalidSource_ThrowsAndPersistsNothing()
    {
        PrefixSourceUpdateHistoryService service =
            CreateService(out _);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RecordFailureAsync(
                CreateDescriptor() with { DisplayName = "" },
                "boom"));

        Assert.Empty(await service.GetRecentAsync());
    }

    [Fact]
    public async Task ClearAsync_RemovesAllEntries()
    {
        PrefixSourceUpdateHistoryService service =
            CreateService(out _);

        await service.RecordSuccessAsync(CreateFetchResult());
        await service.RecordNotModifiedAsync(
            CreateFetchResult(notModified: true));
        await service.RecordFailureAsync(
            CreateDescriptor(),
            "boom");

        await service.ClearAsync();

        Assert.Empty(await service.GetRecentAsync());
    }

    [Fact]
    public void RetentionExceedingMaximum_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => PrefixSourceHistoryOptions.Validate(
                new PrefixSourceHistoryOptions
                {
                    RetentionCount = 10_001
                }));
    }

    private static PrefixSourceUpdateHistoryService CreateService(
        out PrefixSourceUpdateHistoryRepository repository,
        int retention = 100,
        TimeProvider? timeProvider = null)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"),
            "prefix-source-update-history.json");

        PrefixSourceHistoryOptions options = new()
        {
            RetentionCount = retention
        };

        repository = new PrefixSourceUpdateHistoryRepository(
            new PrefixSourceUpdateHistoryStore(path),
            new PrefixSourceUpdateHistoryValidator(),
            options);

        return new PrefixSourceUpdateHistoryService(
            repository,
            options,
            timeProvider);
    }

    private static PrefixSourceFetchResult CreateFetchResult(
        bool notModified = false,
        int attempt = 0,
        IReadOnlyList<string>? prefixes = null)
    {
        DateTimeOffset startedAt =
            BaseTime.AddMinutes(attempt);

        string[] defaultPrefixes =
        [
            "1.2.3.0/24",
            "10.0.0.0/8"
        ];

        if (notModified)
        {
            return new PrefixSourceFetchResult
            {
                Source = CreateDescriptor(),
                Prefixes = [],
                StartedAt = startedAt,
                CompletedAt = startedAt.AddSeconds(1),
                Duration = TimeSpan.FromSeconds(1),
                ETag = "\"etag1\"",
                NotModified = true
            };
        }

        string[] effective = prefixes is null
            ? defaultPrefixes
            : prefixes.ToArray();

        return new PrefixSourceFetchResult
        {
            Source = CreateDescriptor(),
            Prefixes = effective,
            StartedAt = startedAt,
            CompletedAt = startedAt.AddSeconds(4),
            Duration = TimeSpan.FromSeconds(4),
            ETag = "\"etag1\"",
            ContentHash =
                PrefixContentHasher.ComputeHash(effective),
            ContentLength = 512,
            LastModified = BaseTime
        };
    }

    private static PrefixSourceChangeSummary CreateChangeSummary()
    {
        string hash = PrefixContentHasher.ComputeHash(
            CreateFetchResult().Prefixes);

        return new PrefixSourceChangeSummary
        {
            PreviousContentHash = null,
            CurrentContentHash = hash,
            AddedCount = 1,
            RemovedCount = 0,
            UnchangedCount = 1,
            HasChanges = true,
            ComparedAt = BaseTime
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
