namespace IranDirect.Core.Tests.Performance.Persistence;

using IranDirect.Core.Runtime.Profiling;
using IranDirect.Testing.Performance.Persistence;

public sealed class RuntimePerfReportEnduranceTests
{
    private static readonly string[] Triggers =
        ["enable", "disable", "repair"];

    private static readonly DateTimeOffset Base =
        new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OneThousandReports_LatestByTriggerAndListing_AreDeterministic()
    {
        using TemporaryPersistenceWorkspace workspace =
            new("perf-reports-endurance");
        RuntimePerfReportStore store = new(workspace.RootPath);

        for (int i = 0; i < 1_000; i++)
        {
            string trigger = Triggers[i % Triggers.Length];

            store.Write(PersistenceEnduranceFixtures.PerfReport(
                trigger,
                Base.AddSeconds(i),
                i));
        }

        // The legacy cycle-<stamp>-<trigger>.json filename convention
        // must keep participating in reading and ordering.
        File.WriteAllText(
            Path.Combine(
                workspace.RootPath,
                "cycle-20260803-000000-000-enable.json"),
            """{"Trigger":"enable","StartedAt":"2026-08-03T00:00:00+00:00","CompletedAt":"2026-08-03T00:00:00+00:00","TotalMs":1,"Categories":[]}""");

        RuntimeCyclePerfReport? latestEnable = store.ReadLatest("enable");
        Assert.NotNull(latestEnable);
        Assert.Equal(Base.AddSeconds(999), latestEnable.CompletedAt);

        RuntimeCyclePerfReport? latestDisable = store.ReadLatest("disable");
        Assert.NotNull(latestDisable);
        Assert.Equal(Base.AddSeconds(997), latestDisable.CompletedAt);

        RuntimeCyclePerfReport? latestRepair = store.ReadLatest("repair");
        Assert.NotNull(latestRepair);
        Assert.Equal(Base.AddSeconds(998), latestRepair.CompletedAt);

        RuntimeCyclePerfReport? latestOverall = store.ReadLatest();
        Assert.NotNull(latestOverall);
        Assert.Equal("enable", latestOverall.Trigger);
        Assert.Equal(Base.AddSeconds(999), latestOverall.CompletedAt);

        IReadOnlyList<RuntimeCyclePerfReport> enableList =
            await store.ListAsync(trigger: "enable", limit: 5);

        Assert.Equal(5, enableList.Count);
        Assert.Equal(Base.AddSeconds(999), enableList[0].CompletedAt);
        Assert.Equal(Base.AddSeconds(996), enableList[1].CompletedAt);
        Assert.Equal(Base.AddSeconds(987), enableList[^1].CompletedAt);

        IReadOnlyList<RuntimeCyclePerfReport> all =
            await store.ListAsync(limit: 1_001);

        Assert.Equal(1_001, all.Count);

        for (int i = 1; i < all.Count; i++)
        {
            Assert.True(
                all[i - 1].CompletedAt >= all[i].CompletedAt,
                "Reports must be ordered newest-first.");
        }

        Assert.Contains(all, r => r.Trigger == "enable");

        FileSystemSnapshot snapshot = workspace.Snapshot();
        Assert.Equal(1_001, snapshot.FileCount);
        PersistenceEnduranceVerifier.VerifyNoOrphanTempFiles(snapshot);
    }

    [Fact]
    public async Task ListAsync_OrderIsStableAcrossRuns()
    {
        using TemporaryPersistenceWorkspace workspace =
            new("perf-reports-order");
        RuntimePerfReportStore store = new(workspace.RootPath);

        for (int i = 0; i < 100; i++)
        {
            string trigger = Triggers[i % Triggers.Length];

            store.Write(PersistenceEnduranceFixtures.PerfReport(
                trigger,
                Base.AddSeconds(i),
                i));
        }

        IReadOnlyList<RuntimeCyclePerfReport> first =
            await store.ListAsync(limit: 100);
        IReadOnlyList<RuntimeCyclePerfReport> second =
            await store.ListAsync(limit: 100);

        Assert.Equal(first.Count, second.Count);

        for (int i = 0; i < first.Count; i++)
        {
            Assert.Equal(
                first[i].CompletedAt,
                second[i].CompletedAt);
            Assert.Equal(first[i].Trigger, second[i].Trigger);
        }
    }
}
