using PathVeer.Core.Configuration;
using PathVeer.Core.CustomRoutes;
using PathVeer.Core.Diagnostics;
using PathVeer.Core.Planning;
using PathVeer.Core.Prefixes;
using PathVeer.Core.Support;

namespace PathVeer.Benchmarks.Infrastructure;

public enum SnapshotSize
{
    Small,
    Medium,
    Large
}

public static class SupportSnapshotFactory
{
    public static SupportSnapshot Create(SnapshotSize size)
    {
        (int diagnosticResults, int historyEntries, int dnsEntries,
            int previewSteps) = size switch
        {
            SnapshotSize.Small => (10, 5, 3, 10),
            SnapshotSize.Medium => (100, 50, 30, 100),
            SnapshotSize.Large => (1_000, 500, 300, 1_000),
            _ => throw new ArgumentOutOfRangeException(
                nameof(size),
                size,
                null)
        };

        DiagnosticReport diagnostics =
            DiagnosticReportFactory.Create(diagnosticResults);

        ExecutionPreview preview =
            CreatePreview(previewSteps);

        IReadOnlyList<PrefixSourceUpdateHistoryEntry> history =
            CreateHistory(historyEntries);

        IReadOnlyList<CustomRouteDnsCacheStatus> dnsCache =
            CreateDnsCache(dnsEntries);

        return new SupportSnapshot
        {
            CapturedAt = FixedTime.Value,
            Diagnostics = diagnostics,
            ExecutionPreview = preview,
            Configuration = ConfigurationDefaults.Create(),
            PrefixHistory = history,
            DnsCache = dnsCache,
            Summary = new SupportSnapshotSummary
            {
                DiagnosticPassedCount = diagnostics.PassedCount,
                DiagnosticWarningCount = diagnostics.WarningCount,
                DiagnosticFailedCount = diagnostics.FailedCount,
                PrefixCount = historyEntries,
                DnsDomainCount = dnsEntries,
                ExecutionPreviewHasChanges = preview.HasChanges,
                RuntimeAvailable = true,
                PerformanceAvailable = false
            }
        };
    }

    private static ExecutionPreview CreatePreview(int stepCount)
    {
        var steps = new List<ExecutionPreviewStep>(stepCount);
        int createCount = 0;
        int deleteCount = 0;

        for (int i = 0; i < stepCount; i++)
        {
            bool delete = i % 3 == 0;
            if (delete)
            {
                deleteCount++;
            }
            else
            {
                createCount++;
            }

            steps.Add(
                new ExecutionPreviewStep
                {
                    Category =
                        i % 4 == 0
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

        return new ExecutionPreview
        {
            CapturedAt = FixedTime.Value,
            Summary = new ExecutionPreviewSummary
            {
                CreateCount = createCount,
                DeleteCount = deleteCount,
                VerifyCount = 0,
                InventoryUpdates = createCount,
                CustomRouteUpdates = 0,
                VpnEndpointUpdates = stepCount / 4
            },
            Steps = steps
        };
    }

    private static IReadOnlyList<PrefixSourceUpdateHistoryEntry>
        CreateHistory(int count)
    {
        var entries =
            new List<PrefixSourceUpdateHistoryEntry>(count);

        for (int i = 0; i < count; i++)
        {
            PrefixSourceUpdateStatus status = (i % 10) switch
            {
                0 => PrefixSourceUpdateStatus.Failed,
                1 => PrefixSourceUpdateStatus.NotModified,
                _ => PrefixSourceUpdateStatus.Succeeded
            };

            entries.Add(
                new PrefixSourceUpdateHistoryEntry
                {
                    Id = new Guid(
                        (uint)i,
                        0x1234,
                        0x5678,
                        1, 2, 3, 4, 5, 6, 7, 8),
                    SourceId = $"source-{i % 3}",
                    SourceDisplayName =
                        $"Iranian IPv4 Prefix Source {i % 3}",
                    Format = "txt",
                    ParserVersion = "1",
                    Status = status,
                    StartedAt = FixedTime.Value,
                    CompletedAt = FixedTime.Value,
                    Duration = TimeSpan.FromMilliseconds(250),
                    AttemptedAt = FixedTime.Value,
                    CurrentContentHash =
                        $"hash-{i % 17}",
                    ContentLength = 1000 + i,
                    PrefixCount = 100 + i,
                    AddedCount = i % 5,
                    RemovedCount = i % 3,
                    UnchangedCount = 100,
                    HasChanges = i % 2 == 0,
                    Error = status == PrefixSourceUpdateStatus.Failed
                        ? $"Fetch failed for source-{i % 3}."
                        : null
                });
        }

        return entries;
    }

    private static IReadOnlyList<CustomRouteDnsCacheStatus>
        CreateDnsCache(int count)
    {
        var statuses =
            new List<CustomRouteDnsCacheStatus>(count);

        for (int i = 0; i < count; i++)
        {
            CustomRouteDnsCacheState state = (i % 10) switch
            {
                0 => CustomRouteDnsCacheState.Expired,
                1 => CustomRouteDnsCacheState.Stale,
                _ => CustomRouteDnsCacheState.Fresh
            };

            statuses.Add(
                new CustomRouteDnsCacheStatus
                {
                    CustomRouteEntryId = new Guid(
                        (uint)i,
                        0x8765,
                        0x4321,
                        8, 7, 6, 5, 4, 3, 2, 1),
                    Domain = $"host-{i}.example.invalid",
                    Enabled = i % 3 != 0,
                    State = state,
                    IPv4Addresses =
                    [
                        $"192.0.2.{i % 253}",
                        $"198.51.100.{(i * 7) % 253}"
                    ],
                    LastAttemptedAt = FixedTime.Value,
                    LastSucceededAt = FixedTime.Value,
                    ExpiresAt = FixedTime.Value,
                    StaleUntil = FixedTime.Value,
                    LastError = state == CustomRouteDnsCacheState.Expired
                        ? "DNS response expired."
                        : null
                });
        }

        return statuses;
    }
}
