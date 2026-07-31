namespace IranDirect.Core.Tests.Runtime.Profiling;

using IranDirect.Core.Runtime.Profiling;

public sealed class RuntimePerfReportStoreTests
{
    [Fact]
    public void Write_CreatesDirectoryAndTimestampedFile()
    {
        using TempDirectory temp = new();
        string directory = Path.Combine(temp.Path, "perf");
        RuntimePerfReportStore store = new(directory);

        string path = store.Write(SampleReport(
            new DateTimeOffset(2026, 7, 30, 14, 45, 30, 123,
                TimeSpan.Zero)));

        Assert.True(File.Exists(path));
        string fileName = Path.GetFileName(path);
        Assert.StartsWith("enable-20260730-144530-123", fileName);
        Assert.EndsWith(".json", fileName);
    }

    [Fact]
    public void WriteThenReadLatest_RoundTripsAllFields()
    {
        using TempDirectory temp = new();
        RuntimePerfReportStore store = new(temp.Path);
        RuntimeCyclePerfReport original = SampleReport(
            new DateTimeOffset(2026, 7, 30, 14, 45, 35,
                TimeSpan.Zero));

        store.Write(original);

        RuntimeCyclePerfReport? read = store.ReadLatest();

        Assert.NotNull(read);
        Assert.Equal(original.Trigger, read.Trigger);
        Assert.Equal(original.StartedAt, read.StartedAt);
        Assert.Equal(original.CompletedAt, read.CompletedAt);
        Assert.Equal(original.TotalMs, read.TotalMs);
        Assert.Equal(
            original.CompletionStatus, read.CompletionStatus);
        Assert.Equal(
            original.ErrorSummary, read.ErrorSummary);
        Assert.Equal(original.PlannedSteps, read.PlannedSteps);
        Assert.Equal(original.CompletedSteps, read.CompletedSteps);
        Assert.Equal(
            original.Categories.Count, read.Categories.Count);

        for (int i = 0; i < original.Categories.Count; i++)
        {
            Assert.Equal(
                original.Categories[i], read.Categories[i]);
        }
    }

    [Fact]
    public void Write_SerializesCategoryAndStatusAsStrings()
    {
        using TempDirectory temp = new();
        RuntimePerfReportStore store = new(temp.Path);

        string path = store.Write(SampleReport(
            new DateTimeOffset(2026, 7, 30, 14, 45, 35,
                TimeSpan.Zero),
            status: CycleCompletionStatus.Failed));

        string json = File.ReadAllText(path);

        Assert.Contains("ExecutionRouteCreate", json);
        Assert.Contains("PersistenceStateSave", json);
        Assert.Contains("Failed", json);
        Assert.Contains("ErrorSummary", json);
        Assert.Contains("PlannedSteps", json);
    }

    [Fact]
    public void ReadLatest_NoDirectory_ReturnsNull()
    {
        using TempDirectory temp = new();
        RuntimePerfReportStore store = new(
            Path.Combine(temp.Path, "does-not-exist"));

        Assert.Null(store.ReadLatest());
        Assert.Null(store.GetLatestReportPath());
    }

    [Fact]
    public void ReadLatest_EmptyDirectory_ReturnsNull()
    {
        using TempDirectory temp = new();
        Directory.CreateDirectory(temp.Path);
        RuntimePerfReportStore store = new(temp.Path);

        Assert.Null(store.ReadLatest());
    }

    [Fact]
    public void ReadLatest_MultipleReports_ReturnsNewest()
    {
        using TempDirectory temp = new();
        RuntimePerfReportStore store = new(temp.Path);

        store.Write(SampleReport(
            new DateTimeOffset(2026, 7, 30, 10, 0, 0,
                TimeSpan.Zero), trigger: "repair"));
        store.Write(SampleReport(
            new DateTimeOffset(2026, 7, 30, 14, 0, 0,
                TimeSpan.Zero), trigger: "enable"));
        store.Write(SampleReport(
            new DateTimeOffset(2026, 7, 30, 12, 0, 0,
                TimeSpan.Zero), trigger: "disable"));

        RuntimeCyclePerfReport? latest = store.ReadLatest();

        Assert.NotNull(latest);
        Assert.Equal("enable", latest.Trigger);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 30, 14, 0, 0,
                TimeSpan.Zero),
            latest.CompletedAt);
    }

    [Fact]
    public void ReadLatest_IgnoresNonReportFiles()
    {
        using TempDirectory temp = new();
        Directory.CreateDirectory(temp.Path);
        RuntimePerfReportStore store = new(temp.Path);

        File.WriteAllText(
            Path.Combine(temp.Path, "notes.txt"), "ignore me");
        File.WriteAllText(
            Path.Combine(temp.Path, "other.json"), "{}");

        store.Write(SampleReport(
            new DateTimeOffset(2026, 7, 30, 10, 0, 0,
                TimeSpan.Zero)));

        RuntimeCyclePerfReport? latest = store.ReadLatest();

        Assert.NotNull(latest);
        Assert.Equal("enable", latest.Trigger);
    }

    [Fact]
    public void ReadLatest_WithTrigger_ReturnsLatestForTrigger()
    {
        using TempDirectory temp = new();
        RuntimePerfReportStore store = new(temp.Path);

        store.Write(SampleReport(
            new DateTimeOffset(2026, 7, 30, 10, 0, 0,
                TimeSpan.Zero), trigger: "enable"));
        store.Write(SampleReport(
            new DateTimeOffset(2026, 7, 30, 12, 0, 0,
                TimeSpan.Zero), trigger: "enable"));
        store.Write(SampleReport(
            new DateTimeOffset(2026, 7, 30, 11, 0, 0,
                TimeSpan.Zero), trigger: "repair"));

        RuntimeCyclePerfReport? latest = store.ReadLatest("enable");

        Assert.NotNull(latest);
        Assert.Equal("enable", latest.Trigger);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 30, 12, 0, 0,
                TimeSpan.Zero),
            latest.CompletedAt);
    }

    [Fact]
    public void LatestOverall_CanBeRepair_WhileLatestEnable_ReturnsEnable()
    {
        using TempDirectory temp = new();
        RuntimePerfReportStore store = new(temp.Path);

        // The long enable finished earlier; a later no-op repair
        // is now the overall latest.
        store.Write(SampleReport(
            new DateTimeOffset(2026, 7, 30, 10, 0, 0,
                TimeSpan.Zero), trigger: "enable",
            totalMs: 1354000));
        store.Write(SampleReport(
            new DateTimeOffset(2026, 7, 30, 12, 0, 0,
                TimeSpan.Zero), trigger: "repair",
            totalMs: 1600));

        RuntimeCyclePerfReport? overall = store.ReadLatest();
        RuntimeCyclePerfReport? enable = store.ReadLatest("enable");

        Assert.NotNull(overall);
        Assert.Equal("repair", overall.Trigger);

        Assert.NotNull(enable);
        Assert.Equal("enable", enable.Trigger);
        Assert.Equal(1354000, enable.TotalMs);
    }

    [Fact]
    public async Task ReadLatestAsync_ReturnsLatest()
    {
        using TempDirectory temp = new();
        RuntimePerfReportStore store = new(temp.Path);

        store.Write(SampleReport(
            new DateTimeOffset(2026, 7, 30, 10, 0, 0,
                TimeSpan.Zero), trigger: "repair"));
        store.Write(SampleReport(
            new DateTimeOffset(2026, 7, 30, 14, 0, 0,
                TimeSpan.Zero), trigger: "enable"));

        RuntimeCyclePerfReport? latest =
            await store.ReadLatestAsync();

        Assert.NotNull(latest);
        Assert.Equal("enable", latest.Trigger);
    }

    [Fact]
    public async Task ReadLatestAsync_WithTrigger_ReturnsCorrectReport()
    {
        using TempDirectory temp = new();
        RuntimePerfReportStore store = new(temp.Path);

        store.Write(SampleReport(
            new DateTimeOffset(2026, 7, 30, 10, 0, 0,
                TimeSpan.Zero), trigger: "repair"));
        store.Write(SampleReport(
            new DateTimeOffset(2026, 7, 30, 12, 0, 0,
                TimeSpan.Zero), trigger: "enable"));
        store.Write(SampleReport(
            new DateTimeOffset(2026, 7, 30, 14, 0, 0,
                TimeSpan.Zero), trigger: "enable"));

        RuntimeCyclePerfReport? latest =
            await store.ReadLatestAsync("enable");

        Assert.NotNull(latest);
        Assert.Equal("enable", latest.Trigger);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 30, 14, 0, 0,
                TimeSpan.Zero),
            latest.CompletedAt);
    }

    [Fact]
    public async Task ListAsync_SortsNewestFirst()
    {
        using TempDirectory temp = new();
        RuntimePerfReportStore store = new(temp.Path);

        store.Write(SampleReport(
            new DateTimeOffset(2026, 7, 30, 10, 0, 0,
                TimeSpan.Zero), trigger: "repair"));
        store.Write(SampleReport(
            new DateTimeOffset(2026, 7, 30, 14, 0, 0,
                TimeSpan.Zero), trigger: "enable"));
        store.Write(SampleReport(
            new DateTimeOffset(2026, 7, 30, 12, 0, 0,
                TimeSpan.Zero), trigger: "disable"));

        IReadOnlyList<RuntimeCyclePerfReport> reports =
            await store.ListAsync();

        Assert.Equal(3, reports.Count);
        Assert.Equal("enable", reports[0].Trigger);
        Assert.Equal("disable", reports[1].Trigger);
        Assert.Equal("repair", reports[2].Trigger);
    }

    [Fact]
    public async Task ListAsync_WithTrigger_FiltersReports()
    {
        using TempDirectory temp = new();
        RuntimePerfReportStore store = new(temp.Path);

        store.Write(SampleReport(
            new DateTimeOffset(2026, 7, 30, 10, 0, 0,
                TimeSpan.Zero), trigger: "repair"));
        store.Write(SampleReport(
            new DateTimeOffset(2026, 7, 30, 12, 0, 0,
                TimeSpan.Zero), trigger: "enable"));
        store.Write(SampleReport(
            new DateTimeOffset(2026, 7, 30, 14, 0, 0,
                TimeSpan.Zero), trigger: "enable"));

        IReadOnlyList<RuntimeCyclePerfReport> reports =
            await store.ListAsync(trigger: "enable");

        Assert.Equal(2, reports.Count);
        Assert.Equal("enable", reports[0].Trigger);
        Assert.Equal("enable", reports[1].Trigger);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 30, 14, 0, 0,
                TimeSpan.Zero),
            reports[0].CompletedAt);
    }

    [Fact]
    public async Task ListAsync_WithLimit_RespectsLimit()
    {
        using TempDirectory temp = new();
        RuntimePerfReportStore store = new(temp.Path);

        store.Write(SampleReport(
            new DateTimeOffset(2026, 7, 30, 10, 0, 0,
                TimeSpan.Zero), trigger: "enable"));
        store.Write(SampleReport(
            new DateTimeOffset(2026, 7, 30, 12, 0, 0,
                TimeSpan.Zero), trigger: "disable"));
        store.Write(SampleReport(
            new DateTimeOffset(2026, 7, 30, 14, 0, 0,
                TimeSpan.Zero), trigger: "repair"));

        IReadOnlyList<RuntimeCyclePerfReport> reports =
            await store.ListAsync(limit: 2);

        Assert.Equal(2, reports.Count);
        Assert.Equal("repair", reports[0].Trigger);
        Assert.Equal("disable", reports[1].Trigger);
    }

    [Fact]
    public void LegacyCycleFilename_IsStillReadable()
    {
        using TempDirectory temp = new();
        Directory.CreateDirectory(temp.Path);
        RuntimePerfReportStore store = new(temp.Path);

        File.WriteAllText(
            Path.Combine(temp.Path,
                "cycle-20260730-144530-123-enable.json"),
            """{"Trigger":"enable","StartedAt":"2026-07-30T14:45:30+00:00","CompletedAt":"2026-07-30T14:45:30+00:00","TotalMs":10,"Categories":[]}""");

        RuntimeCyclePerfReport? read = store.ReadLatest("enable");

        Assert.NotNull(read);
        Assert.Equal("enable", read.Trigger);
    }

    private static RuntimeCyclePerfReport SampleReport(
        DateTimeOffset completedAt,
        string trigger = "enable",
        double totalMs = 4521.3,
        CycleCompletionStatus status =
            CycleCompletionStatus.Completed) =>
        new()
        {
            Trigger = trigger,
            StartedAt = completedAt.AddSeconds(-4),
            CompletedAt = completedAt,
            TotalMs = totalMs,
            CompletionStatus = status,
            ErrorSummary = status == CycleCompletionStatus.Completed
                ? null
                : "Sample failure.",
            PlannedSteps = 10,
            CompletedSteps = 6,
            Categories =
            [
                new RuntimePerfCategorySummary(
                    RuntimePerfCategory.ExecutionRouteCreate,
                    1944, 2100.5, 1.08, 0.4, 12.1, 2.4),
                new RuntimePerfCategorySummary(
                    RuntimePerfCategory.PersistenceStateSave,
                    1, 12.1, 12.1, 12.1, 12.1, 12.1)
            ]
        };

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"IranPerfStoreTest_{Guid.NewGuid()}");

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}
