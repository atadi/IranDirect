using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PathVeer.Core.Configuration;
using PathVeer.Core.CustomRoutes;
using PathVeer.Core.Diagnostics;
using PathVeer.Core.Observability;
using PathVeer.Core.Planning;
using PathVeer.Core.Prefixes;
using PathVeer.Core.Runtime.Profiling;
using PathVeer.Core.Support;

namespace PathVeer.Core.Tests.Support;

/// <summary>
/// Proves the optimized <see cref="SupportSnapshotSerializer"/> produces
/// byte-for-byte identical JSON (and UTF-8 bytes) to the pre-change reference
/// path across a wide range of inputs. Exact string equality is required;
/// parsed-JSON equality alone is not sufficient.
/// </summary>
public sealed class SupportSnapshotSerializerEquivalenceTests
{
    private static readonly DateTimeOffset FixedTime =
        new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    private static readonly UTF8Encoding s_utf8NoBom =
        new(encoderShouldEmitUTF8Identifier: false);

    private static readonly SupportSnapshotSerializer s_serializer = new();

    private static SupportSnapshot Build(
        RuntimeSnapshot? runtime,
        DiagnosticReport? diagnostics,
        ExecutionPreview? preview,
        DesiredConfiguration? configuration,
        PrefixSourceMetadata? prefixMetadata,
        IReadOnlyList<PrefixSourceUpdateHistoryEntry> history,
        IReadOnlyList<CustomRouteDnsCacheStatus> dnsCache,
        RuntimeCyclePerfReport? performance)
    {
        return new SupportSnapshot
        {
            CapturedAt = FixedTime,
            Runtime = runtime,
            Diagnostics = diagnostics,
            ExecutionPreview = preview,
            Configuration = configuration,
            PrefixMetadata = prefixMetadata,
            PrefixHistory = history,
            DnsCache = dnsCache,
            Performance = performance,
            Summary = new SupportSnapshotSummary
            {
                DiagnosticPassedCount = 0,
                DiagnosticWarningCount = 0,
                DiagnosticFailedCount = 0,
                PrefixCount = 0,
                DnsDomainCount = 0,
                ExecutionPreviewHasChanges = false,
                RuntimeAvailable = false,
                PerformanceAvailable = false
            }
        };
    }

    private static SupportSnapshot BuildPopulated()
    {
        RuntimeSnapshot runtime = new() { CapturedAt = FixedTime, PrefixCount = 5 };

        DiagnosticReport diagnostics = new(FixedTime,
        [
            new DiagnosticResult(Id: "p1", Title: "Pass 1",
                Status: DiagnosticStatus.Passed, Severity: DiagnosticSeverity.Pass,
                Message: "ok", SuggestedAction: null),
            new DiagnosticResult(Id: "w1", Title: "Warn 1",
                Status: DiagnosticStatus.Warning, Severity: DiagnosticSeverity.Warning,
                Message: "watch", SuggestedAction: null)
        ]);

        ExecutionPreview preview = new()
        {
            CapturedAt = FixedTime,
            Summary = new ExecutionPreviewSummary
            {
                CreateCount = 1, DeleteCount = 0, VerifyCount = 0,
                InventoryUpdates = 1, CustomRouteUpdates = 0, VpnEndpointUpdates = 0
            },
            Steps =
            [
                new ExecutionPreviewStep
                {
                    Category = ExecutionPreviewCategory.Route,
                    Operation = ExecutionPreviewOperation.Create,
                    Target = "10.0.0.0/24",
                    Reason = "missing"
                }
            ]
        };

        DesiredConfiguration configuration = new();
        PrefixSourceMetadata metadata = new()
        {
            SourceId = "src", SourceDisplayName = "Source", Format = "txt",
            ParserVersion = "1", LastAttemptedAt = FixedTime,
            LastSucceededAt = FixedTime,
            LastStatus = PrefixSourceUpdateStatus.Succeeded, PrefixCount = 5
        };
        PrefixSourceUpdateHistoryEntry entry = new()
        {
            SourceId = "src", SourceDisplayName = "Source", Format = "txt",
            ParserVersion = "1", Status = PrefixSourceUpdateStatus.Succeeded,
            StartedAt = FixedTime, CompletedAt = FixedTime, AttemptedAt = FixedTime,
            PrefixCount = 5, HasChanges = true
        };
        CustomRouteDnsCacheStatus status = new()
        {
            CustomRouteEntryId = Guid.NewGuid(), Domain = "example.com",
            Enabled = true, State = CustomRouteDnsCacheState.Fresh
        };
        RuntimeCyclePerfReport performance = new()
        {
            Trigger = "enable", StartedAt = FixedTime,
            CompletedAt = FixedTime.AddSeconds(1), TotalMs = 100, Categories = []
        };

        return new SupportSnapshot
        {
            CapturedAt = FixedTime,
            Runtime = runtime,
            Diagnostics = diagnostics,
            ExecutionPreview = preview,
            Configuration = configuration,
            PrefixMetadata = metadata,
            PrefixHistory = [entry],
            DnsCache = [status],
            Performance = performance,
            Summary = new SupportSnapshotSummary
            {
                DiagnosticPassedCount = 1, DiagnosticWarningCount = 1,
                DiagnosticFailedCount = 0, PrefixCount = 5, DnsDomainCount = 1,
                ExecutionPreviewHasChanges = true, RuntimeAvailable = true,
                PerformanceAvailable = true
            }
        };
    }

    private static SupportSnapshot BuildWithDiagnostics(int count)
    {
        var results = new List<DiagnosticResult>(count);
        for (int i = 0; i < count; i++)
        {
            DiagnosticStatus status = (i % 20) switch
            {
                0 => DiagnosticStatus.Failed,
                1 => DiagnosticStatus.Warning,
                _ => DiagnosticStatus.Passed
            };
            DiagnosticSeverity severity = status switch
            {
                DiagnosticStatus.Failed => DiagnosticSeverity.Error,
                DiagnosticStatus.Warning => DiagnosticSeverity.Warning,
                _ => DiagnosticSeverity.Pass
            };
            results.Add(new DiagnosticResult(
                Id: $"check-{i}",
                Title: $"Check {i}",
                Status: status,
                Severity: severity,
                Message: $"Diagnostic result {i}.",
                SuggestedAction: i % 3 == 0 ? $"Action {i}." : null));
        }

        DiagnosticReport report = new(FixedTime, results);
        return Build(null, report, null, null, null, [], [], null);
    }

    private static SupportSnapshot BuildWithPreviewSteps(int stepCount)
    {
        var steps = new List<ExecutionPreviewStep>(stepCount);
        int createCount = 0;
        int deleteCount = 0;
        for (int i = 0; i < stepCount; i++)
        {
            bool delete = i % 3 == 0;
            if (delete) deleteCount++; else createCount++;
            steps.Add(new ExecutionPreviewStep
            {
                Category = i % 4 == 0
                    ? ExecutionPreviewCategory.VpnEndpoint
                    : ExecutionPreviewCategory.Route,
                Operation = delete
                    ? ExecutionPreviewOperation.Delete
                    : ExecutionPreviewOperation.Create,
                Target = $"198.51.100.{i % 254}.0/24",
                Reason = delete
                    ? "Owned route is no longer desired."
                    : "Desired route is missing."
            });
        }

        ExecutionPreview preview = new()
        {
            CapturedAt = FixedTime,
            Summary = new ExecutionPreviewSummary
            {
                CreateCount = createCount, DeleteCount = deleteCount,
                VerifyCount = 0, InventoryUpdates = createCount,
                CustomRouteUpdates = 0, VpnEndpointUpdates = stepCount / 4
            },
            Steps = steps
        };

        return Build(null, null, preview, null, null, [], [], null);
    }

    private static void AssertExact(
        string label,
        SupportSnapshot snapshot)
    {
        string optimized = s_serializer.Serialize(snapshot);
        string reference = ReferenceSupportSnapshotSerializer.Serialize(snapshot);

        Assert.Equal(reference, optimized);

        byte[] optimizedBytes = s_utf8NoBom.GetBytes(optimized);
        byte[] referenceBytes =
            ReferenceSupportSnapshotSerializer.SerializeToUtf8Bytes(snapshot);

        Assert.Equal(referenceBytes, optimizedBytes);
        Assert.False(
            optimizedBytes.Length >= 3 &&
            optimizedBytes[0] == 0xEF &&
            optimizedBytes[1] == 0xBB &&
            optimizedBytes[2] == 0xBF,
            "Optimized output must not include a UTF-8 BOM.");

        using JsonDocument doc = JsonDocument.Parse(optimized);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    [Fact]
    public void MinimalSnapshot_Exact() =>
        AssertExact("minimal", Build(null, null, null, null, null, [], [], null));

    [Fact]
    public void AllNullableNull_Exact() =>
        AssertExact("all-null",
            Build(null, null, null, null, null, [], [], null));

    [Fact]
    public void AllCollectionsEmpty_Exact() =>
        AssertExact("empty-collections",
            Build(null, null, null, null, null, [], [], null));

    [Fact]
    public void FullyPopulated_Exact() =>
        AssertExact("populated", BuildPopulated());

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(5000)]
    public void WithDiagnostics_Exact(int count) =>
        AssertExact($"diagnostics-{count}", BuildWithDiagnostics(count));

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(10000)]
    public void ExecutionPreviewSteps_Exact(int stepCount) =>
        AssertExact($"preview-{stepCount}", BuildWithPreviewSteps(stepCount));

    [Fact]
    public void PrefixMetadataPresent_Exact()
    {
        PrefixSourceMetadata metadata = new()
        {
            SourceId = "src", SourceDisplayName = "Source", Format = "txt",
            ParserVersion = "1", LastAttemptedAt = FixedTime,
            LastSucceededAt = FixedTime,
            LastStatus = PrefixSourceUpdateStatus.NotModified, PrefixCount = 7
        };
        AssertExact("prefix-metadata",
            Build(null, null, null, null, metadata, [], [], null));
    }

    [Fact]
    public void PrefixHistoryPopulated_Exact()
    {
        PrefixSourceUpdateHistoryEntry entry = new()
        {
            SourceId = "src", SourceDisplayName = "Source", Format = "txt",
            ParserVersion = "1", Status = PrefixSourceUpdateStatus.Failed,
            StartedAt = FixedTime, CompletedAt = FixedTime, AttemptedAt = FixedTime,
            CurrentContentHash = "hash", ContentLength = 1234, PrefixCount = 50,
            AddedCount = 3, RemovedCount = 1, UnchangedCount = 46,
            HasChanges = true, Error = "Fetch failed."
        };
        AssertExact("prefix-history",
            Build(null, null, null, null, null, [entry], [], null));
    }

    [Fact]
    public void DnsCachePopulated_Exact()
    {
        CustomRouteDnsCacheStatus status = new()
        {
            CustomRouteEntryId = Guid.NewGuid(), Domain = "host.example.invalid",
            Enabled = true, State = CustomRouteDnsCacheState.Stale,
            IPv4Addresses = ["192.0.2.1", "198.51.100.2"],
            LastAttemptedAt = FixedTime, LastSucceededAt = FixedTime,
            ExpiresAt = FixedTime, StaleUntil = FixedTime,
            LastError = "DNS response expired."
        };
        AssertExact("dns-cache",
            Build(null, null, null, null, null, [], [status], null));
    }

    [Fact]
    public void PerformanceReportPresent_Exact()
    {
        RuntimeCyclePerfReport report = new()
        {
            Trigger = "enable", StartedAt = FixedTime,
            CompletedAt = FixedTime.AddSeconds(2), TotalMs = 2000,
            CompletionStatus = CycleCompletionStatus.Completed,
            Categories =
            [
                new RuntimePerfCategorySummary(
                    RuntimePerfCategory.ExecutionRouteCreate,
                    Count: 1, TotalMs: 1000, AverageMs: 1000,
                    MinMs: 1000, MaxMs: 1000, P95Ms: 1000)
            ]
        };
        AssertExact("performance",
            Build(null, null, null, null, null, [], [], report));
    }

    [Fact]
    public void EveryEnumValue_Exact()
    {
        // Exercise all enum values reachable in the graph.
        CustomRouteDnsCacheStatus dns = new()
        {
            CustomRouteEntryId = Guid.NewGuid(), Domain = "e.invalid",
            Enabled = false, State = CustomRouteDnsCacheState.Expired,
            LastAttemptedAt = FixedTime, LastSucceededAt = FixedTime,
            ExpiresAt = FixedTime, StaleUntil = FixedTime
        };
        // Cover all Dns states + preview categories/operations + history states.
        SupportSnapshot snapshot = Build(
            null, null,
            new ExecutionPreview
            {
                CapturedAt = FixedTime,
                Summary = new ExecutionPreviewSummary
                {
                    CreateCount = 1, DeleteCount = 1, VerifyCount = 0,
                    InventoryUpdates = 1, CustomRouteUpdates = 0,
                    VpnEndpointUpdates = 1
                },
                Steps =
                [
                    new ExecutionPreviewStep
                    {
                        Category = ExecutionPreviewCategory.VpnEndpoint,
                        Operation = ExecutionPreviewOperation.Delete,
                        Target = "1.2.3.0/24", Reason = "r"
                    },
                    new ExecutionPreviewStep
                    {
                        Category = ExecutionPreviewCategory.Route,
                        Operation = ExecutionPreviewOperation.Create,
                        Target = "5.6.7.0/24", Reason = "r"
                    }
                ]
            },
            null, null,
            [
                new PrefixSourceUpdateHistoryEntry
                {
                    SourceId = "s", SourceDisplayName = "S", Format = "txt",
                    ParserVersion = "1", Status = PrefixSourceUpdateStatus.Succeeded,
                    StartedAt = FixedTime, CompletedAt = FixedTime,
                    AttemptedAt = FixedTime, PrefixCount = 1, HasChanges = false
                },
                new PrefixSourceUpdateHistoryEntry
                {
                    SourceId = "s2", SourceDisplayName = "S2", Format = "txt",
                    ParserVersion = "1", Status = PrefixSourceUpdateStatus.NotModified,
                    StartedAt = FixedTime, CompletedAt = FixedTime,
                    AttemptedAt = FixedTime, PrefixCount = 1, HasChanges = false
                }
            ],
            [
                dns,
                new CustomRouteDnsCacheStatus
                {
                    CustomRouteEntryId = Guid.NewGuid(), Domain = "f.invalid",
                    Enabled = true, State = CustomRouteDnsCacheState.Fresh,
                    LastAttemptedAt = FixedTime, LastSucceededAt = FixedTime,
                    ExpiresAt = FixedTime, StaleUntil = FixedTime
                }
            ],
            null);
        AssertExact("enums", snapshot);
    }

    [Fact]
    public void NonUtcDateTimeOffset_Exact()
    {
        DateTimeOffset tehran = new(
            2026, 8, 3, 15, 30, 0, TimeSpan.FromHours(3.5));
        SupportSnapshot snapshot = new()
        {
            CapturedAt = tehran,
            Summary = new SupportSnapshotSummary
            {
                DiagnosticPassedCount = 0, DiagnosticWarningCount = 0,
                DiagnosticFailedCount = 0, PrefixCount = 0, DnsDomainCount = 0,
                ExecutionPreviewHasChanges = false, RuntimeAvailable = false,
                PerformanceAvailable = false
            }
        };
        AssertExact("non-utc", snapshot);
    }

    [Fact]
    public void UnicodeStrings_Exact()
    {
        PrefixSourceMetadata metadata = new()
        {
            SourceId = "src-ایران", SourceDisplayName = "منبع ایرانی",
            Format = "txt", ParserVersion = "۱", LastAttemptedAt = FixedTime,
            LastSucceededAt = FixedTime,
            LastStatus = PrefixSourceUpdateStatus.Succeeded, PrefixCount = 1
        };
        AssertExact("unicode",
            Build(null, null, null, null, metadata, [], [], null));
    }

    [Fact]
    public void EscapedCharacters_Exact()
    {
        ExecutionPreview preview = new()
        {
            CapturedAt = FixedTime,
            Summary = new ExecutionPreviewSummary
            {
                CreateCount = 0, DeleteCount = 0, VerifyCount = 0,
                InventoryUpdates = 0, CustomRouteUpdates = 0,
                VpnEndpointUpdates = 0
            },
            Steps =
            [
                new ExecutionPreviewStep
                {
                    Category = ExecutionPreviewCategory.Route,
                    Operation = ExecutionPreviewOperation.Create,
                    Target = "10.0.0.0/24",
                    Reason = "Quote \" and backslash \\ and\nnewline and \t tab."
                }
            ]
        };
        AssertExact("escapes", Build(null, null, preview, null, null, [], [], null));
    }

    [Fact]
    public void RepeatedSerialization_Deterministic()
    {
        SupportSnapshot snapshot = BuildPopulated();
        string first = s_serializer.Serialize(snapshot);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(first, s_serializer.Serialize(snapshot));
        }
        // Also identical to reference each time.
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(
                ReferenceSupportSnapshotSerializer.Serialize(snapshot),
                s_serializer.Serialize(snapshot));
        }
    }

    [Fact]
    public void DeterministicCollectionOrdering_Exact()
    {
        var entries = new List<PrefixSourceUpdateHistoryEntry>();
        for (int i = 0; i < 5; i++)
        {
            entries.Add(new PrefixSourceUpdateHistoryEntry
            {
                SourceId = $"src-{i}",
                SourceDisplayName = $"Source {i}",
                Format = "txt", ParserVersion = "1",
                Status = PrefixSourceUpdateStatus.Succeeded,
                StartedAt = FixedTime, CompletedAt = FixedTime,
                AttemptedAt = FixedTime, PrefixCount = i, HasChanges = false
            });
        }
        AssertExact("ordering",
            Build(null, null, null, null, null, entries, [], null));
    }

    [Fact]
    public void Serialize_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => s_serializer.Serialize(null!));
    }
}
