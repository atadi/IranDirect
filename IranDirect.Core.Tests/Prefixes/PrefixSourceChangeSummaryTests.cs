using IranDirect.Core.Prefixes;

namespace IranDirect.Core.Tests.Prefixes;

public sealed class PrefixSourceChangeSummaryTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetLatestChangeSummaryAsync_NoPriorState_ReturnsNull()
    {
        PrefixSourceMetadataService service =
            CreateService(out _);

        Assert.Null(
            await service.GetLatestChangeSummaryAsync());
    }

    [Fact]
    public async Task RecordSuccessAsync_FirstImport_HasChanges()
    {
        FakeTimeProvider clock = new();
        PrefixSourceMetadataService service =
            CreateService(out _, clock);
        clock.Now = BaseTime;

        string[] prefixes =
        [
            "1.2.3.0/24",
            "10.0.0.0/8"
        ];

        await service.RecordSuccessAsync(
            CreateFetchResult(prefixes));

        PrefixSourceChangeSummary? summary =
            await service.GetLatestChangeSummaryAsync();

        Assert.NotNull(summary);
        Assert.True(summary.HasChanges);
        Assert.Equal(2, summary.AddedCount);
        Assert.Equal(0, summary.RemovedCount);
        Assert.Equal(0, summary.UnchangedCount);
        Assert.Null(summary.PreviousContentHash);
        Assert.Equal(
            PrefixContentHasher.ComputeHash(prefixes),
            summary.CurrentContentHash);
        Assert.Equal(BaseTime, summary.ComparedAt);
    }

    [Fact]
    public async Task RecordSuccessAsync_IdenticalHash_UsesHashShortcut()
    {
        FakeTimeProvider clock = new();
        PrefixSourceMetadataService service =
            CreateService(out _, clock);

        string[] prefixes =
        [
            "1.2.3.0/24",
            "10.0.0.0/8"
        ];

        clock.Now = BaseTime;
        await service.RecordSuccessAsync(
            CreateFetchResult(prefixes),
            previousPrefixes: []);

        clock.Now = BaseTime.AddHours(1);
        await service.RecordSuccessAsync(
            CreateFetchResult(prefixes),
            previousPrefixes: null);

        PrefixSourceChangeSummary? summary =
            await service.GetLatestChangeSummaryAsync();

        Assert.NotNull(summary);
        Assert.False(summary.HasChanges);
        Assert.Equal(0, summary.AddedCount);
        Assert.Equal(0, summary.RemovedCount);
        Assert.Equal(2, summary.UnchangedCount);
        Assert.Equal(
            PrefixContentHasher.ComputeHash(prefixes),
            summary.PreviousContentHash);
        Assert.Equal(
            PrefixContentHasher.ComputeHash(prefixes),
            summary.CurrentContentHash);
    }

    [Fact]
    public async Task RecordSuccessAsync_ChangedHash_UsesDatasetComparison()
    {
        FakeTimeProvider clock = new();
        PrefixSourceMetadataService service =
            CreateService(out _, clock);

        string[] first =
        [
            "1.2.3.0/24",
            "10.0.0.0/8"
        ];
        string[] second =
        [
            "10.0.0.0/8",
            "192.0.2.0/24"
        ];

        clock.Now = BaseTime;
        await service.RecordSuccessAsync(
            CreateFetchResult(first),
            previousPrefixes: []);

        clock.Now = BaseTime.AddHours(1);
        await service.RecordSuccessAsync(
            CreateFetchResult(second),
            previousPrefixes: first);

        PrefixSourceChangeSummary? summary =
            await service.GetLatestChangeSummaryAsync();

        Assert.NotNull(summary);
        Assert.True(summary.HasChanges);
        Assert.Equal(1, summary.AddedCount);
        Assert.Equal(1, summary.RemovedCount);
        Assert.Equal(1, summary.UnchangedCount);
        Assert.Equal(
            PrefixContentHasher.ComputeHash(first),
            summary.PreviousContentHash);
        Assert.Equal(
            PrefixContentHasher.ComputeHash(second),
            summary.CurrentContentHash);
    }

    [Fact]
    public async Task RecordSuccessAsync_ChangedHashNoPreviousPrefixes_TreatsOldAsEmpty()
    {
        FakeTimeProvider clock = new();
        PrefixSourceMetadataService service =
            CreateService(out _, clock);

        clock.Now = BaseTime;
        await service.RecordSuccessAsync(
            CreateFetchResult(["1.2.3.0/24"]),
            previousPrefixes: null);

        clock.Now = BaseTime.AddHours(1);
        await service.RecordSuccessAsync(
            CreateFetchResult(
                ["1.2.3.0/24", "10.0.0.0/8"]),
            previousPrefixes: null);

        PrefixSourceChangeSummary? summary =
            await service.GetLatestChangeSummaryAsync();

        Assert.NotNull(summary);
        Assert.True(summary.HasChanges);
        Assert.Equal(2, summary.AddedCount);
        Assert.Equal(0, summary.RemovedCount);
        Assert.Equal(0, summary.UnchangedCount);
    }

    [Fact]
    public async Task RecordNotModifiedAsync_PreservesPriorSummary()
    {
        FakeTimeProvider clock = new();
        PrefixSourceMetadataService service =
            CreateService(out _, clock);

        clock.Now = BaseTime;
        await service.RecordSuccessAsync(
            CreateFetchResult(
                ["1.2.3.0/24", "10.0.0.0/8"]));

        clock.Now = BaseTime.AddHours(1);
        await service.RecordNotModifiedAsync(
            CreateFetchResult(notModified: true));

        PrefixSourceChangeSummary? summary =
            await service.GetLatestChangeSummaryAsync();

        Assert.NotNull(summary);
        Assert.True(summary.HasChanges);
        Assert.Equal(2, summary.AddedCount);
        Assert.Equal(
            BaseTime,
            summary.ComparedAt);
    }

    [Fact]
    public async Task Summary_PersistsAcrossServiceInstances()
    {
        string path = CreatePath();

        PrefixSourceMetadataService first =
            CreateServiceAt(path, out _);
        await first.RecordSuccessAsync(
            CreateFetchResult(
                ["1.2.3.0/24", "10.0.0.0/8"]));

        PrefixSourceMetadataService second =
            CreateServiceAt(path, out _);

        PrefixSourceChangeSummary? summary =
            await second.GetLatestChangeSummaryAsync();

        Assert.NotNull(summary);
        Assert.True(summary.HasChanges);
        Assert.Equal(2, summary.AddedCount);
    }

    [Fact]
    public async Task ConcurrentRecordSuccess_DoesNotCorruptDocument()
    {
        PrefixSourceMetadataService service =
            CreateService(out PrefixSourceMetadataRepository repository);

        int[] sizes = [1, 2, 3, 4, 5];

        Task[] tasks = sizes
            .Select(size => service.RecordSuccessAsync(
                CreateFetchResult(
                    Enumerable.Range(0, size)
                        .Select(i => $"10.{i}.0.0/16")
                        .ToArray())))
            .ToArray();

        await Task.WhenAll(tasks);

        PrefixSourceMetadataDocument document =
            await repository.LoadAsync();

        Assert.NotNull(document.Current);
        Assert.NotNull(document.Current.ChangeSummary);
        Assert.Contains(
            sizes,
            size => document.Current.ChangeSummary.AddedCount == size);
    }

    [Fact]
    public void FormatChangeSummary_FormatsVariants()
    {
        Assert.Null(
            PrefixSourceMetadataService.FormatChangeSummary(null));

        Assert.Equal(
            "+2 -1 unchanged 5",
            PrefixSourceMetadataService.FormatChangeSummary(
                new PrefixSourceChangeSummary
                {
                    AddedCount = 2,
                    RemovedCount = 1,
                    UnchangedCount = 5,
                    HasChanges = true,
                    ComparedAt = BaseTime
                }));

        Assert.Equal(
            "No changes (5 prefixes).",
            PrefixSourceMetadataService.FormatChangeSummary(
                new PrefixSourceChangeSummary
                {
                    UnchangedCount = 5,
                    HasChanges = false,
                    ComparedAt = BaseTime
                }));
    }

    [Fact]
    public async Task SaveAsync_ChangeSummary_NegativeCounts_Throws()
    {
        PrefixSourceMetadataRepository repository =
            CreateRepository();

        PrefixSourceMetadataDocument invalid =
            CreateDocument(
                CreateMetadata() with
                {
                    ChangeSummary = new PrefixSourceChangeSummary
                    {
                        AddedCount = -1,
                        ComparedAt = BaseTime
                    }
                });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SaveAsync(invalid));
    }

    [Fact]
    public async Task SaveAsync_ChangeSummary_DefaultComparedAt_Throws()
    {
        PrefixSourceMetadataRepository repository =
            CreateRepository();

        PrefixSourceMetadataDocument invalid =
            CreateDocument(
                CreateMetadata() with
                {
                    ChangeSummary = new PrefixSourceChangeSummary()
                });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SaveAsync(invalid));
    }

    [Fact]
    public async Task SaveAsync_ChangeSummary_InvalidHash_Throws()
    {
        PrefixSourceMetadataRepository repository =
            CreateRepository();

        PrefixSourceMetadataDocument invalid =
            CreateDocument(
                CreateMetadata() with
                {
                    ChangeSummary = new PrefixSourceChangeSummary
                    {
                        PreviousContentHash = "not-a-hash",
                        ComparedAt = BaseTime
                    }
                });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SaveAsync(invalid));
    }

    private static PrefixSourceMetadata CreateMetadata()
    {
        return new PrefixSourceMetadata
        {
            SourceId = "test-source",
            SourceDisplayName = "Test Source",
            Format = "example-json",
            ParserVersion = "1",
            LastAttemptedAt = BaseTime,
            LastSucceededAt = BaseTime,
            LastStatus = PrefixSourceUpdateStatus.Succeeded
        };
    }

    private static PrefixSourceMetadataDocument CreateDocument(
        PrefixSourceMetadata metadata) =>
        new()
        {
            SchemaVersion = 1,
            Current = metadata
        };

    private static PrefixSourceFetchResult CreateFetchResult(
        string[]? prefixes = null,
        bool notModified = false)
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
                NotModified = true
            };
        }

        prefixes ??= [];

        return new PrefixSourceFetchResult
        {
            Source = CreateDescriptor(),
            Prefixes = prefixes,
            StartedAt = BaseTime,
            CompletedAt = BaseTime.AddSeconds(3),
            Duration = TimeSpan.FromSeconds(3),
            ContentHash =
                PrefixContentHasher.ComputeHash(prefixes),
            ContentLength = 100
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

    private static string CreatePath() =>
        Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"),
            "prefix-source-metadata.json");

    private static PrefixSourceMetadataService CreateService(
        out PrefixSourceMetadataRepository repository,
        TimeProvider? timeProvider = null)
    {
        string path = CreatePath();
        return CreateServiceAt(path, out repository, timeProvider);
    }

    private static PrefixSourceMetadataService CreateServiceAt(
        string path,
        out PrefixSourceMetadataRepository repository,
        TimeProvider? timeProvider = null)
    {
        repository = new PrefixSourceMetadataRepository(
            new PrefixSourceMetadataStore(path),
            new PrefixSourceMetadataValidator());

        return new PrefixSourceMetadataService(
            repository,
            timeProvider);
    }

    private static PrefixSourceMetadataRepository CreateRepository() =>
        new(
            new PrefixSourceMetadataStore(CreatePath()),
            new PrefixSourceMetadataValidator());

    private sealed class FakeTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = BaseTime;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
