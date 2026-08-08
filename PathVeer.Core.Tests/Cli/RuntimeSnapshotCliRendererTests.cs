using PathVeer.Core.Cli;
using PathVeer.Core.Configuration;
using PathVeer.Core.CustomRoutes;
using PathVeer.Core.Observability;
using PathVeer.Core.Prefixes;
using PathVeer.Core.Runtime.Execution;
using PathVeer.Core.Runtime.Profiling;
using PathVeer.Core.Vpn;

namespace PathVeer.Core.Tests.Cli;

public sealed class RuntimeSnapshotCliRendererTests
{
    [Fact]
    public void Render_IncludesAllSections()
    {
        RuntimeSnapshot snapshot = CreateSnapshot();

        string[] lines = RuntimeSnapshotCliRenderer
            .Render(snapshot)
            .ToArray();

        Assert.Contains("=== Runtime Snapshot ===", lines);
        Assert.Contains(
            "Captured: 2026-01-01 00:00:00 UTC",
            lines);
        Assert.Contains("Configuration:", lines);
        Assert.Contains("Desired: Enabled", lines);
        Assert.Contains("Runtime:", lines);
        Assert.Contains("Applied: Enabled", lines);
        Assert.Contains("Operation:", lines);
        Assert.Contains("Idle", lines);
        Assert.Contains("Routes:", lines);
        Assert.Contains("Managed: 3", lines);
        Assert.Contains("Desired: 5", lines);
        Assert.Contains("Inventory: 4", lines);
        Assert.Contains("VPN:", lines);
        Assert.Contains("Protected: 1 / 2", lines);
        Assert.Contains("DNS:", lines);
        Assert.Contains("Domains: 4", lines);
        Assert.Contains("Fresh: 3", lines);
        Assert.Contains("Stale: 1", lines);
        Assert.Contains("Failed: 0", lines);
        Assert.Contains("Performance:", lines);
        Assert.Contains("Last cycle: 42.1 sec", lines);
        Assert.Contains("Prefix Source:", lines);
        Assert.Contains("Source: RIPE", lines);
        Assert.Contains("Status: Succeeded", lines);
        Assert.Contains(
            "Last success: 2026-08-03 14:10:00 UTC",
            lines);
        Assert.Contains(
            "Last attempted: 2026-08-03 14:10:00 UTC",
            lines);
        Assert.Contains("Prefixes: 1946", lines);
        Assert.Contains("Hash: ab12ef34...", lines);
        Assert.Contains("Added: 14", lines);
        Assert.Contains("Removed: 6", lines);
        Assert.Contains("Unchanged: 1940", lines);
        Assert.Contains("Changed: Yes", lines);
        Assert.Contains("Repair interval: 00:30:00", lines);
        Assert.Contains("Prefix update interval: 1.00:00:00", lines);
        Assert.Contains("VPN profile: vpn-profile.ovpn", lines);
        Assert.Contains("Auto repair: True", lines);
    }

    [Fact]
    public void Render_EmptyPerformance_ShowsNone()
    {
        RuntimeSnapshot snapshot =
            CreateSnapshot() with { Performance = null };

        string[] lines = RuntimeSnapshotCliRenderer
            .Render(snapshot)
            .ToArray();

        Assert.Contains("Last cycle: none", lines);
    }

    [Fact]
    public void Render_NoDnsEntries_ShowsZeroCounts()
    {
        RuntimeSnapshot snapshot =
            CreateSnapshot() with { DnsCache = [] };

        string[] lines = RuntimeSnapshotCliRenderer
            .Render(snapshot)
            .ToArray();

        Assert.Contains("Domains: 0", lines);
        Assert.Contains("Fresh: 0", lines);
        Assert.Contains("Stale: 0", lines);
        Assert.Contains("Failed: 0", lines);
    }

    [Fact]
    public void Render_NullRuntime_ShowsUnknownApplied()
    {
        RuntimeSnapshot snapshot =
            CreateSnapshot() with { Runtime = null };

        string[] lines = RuntimeSnapshotCliRenderer
            .Render(snapshot)
            .ToArray();

        Assert.Contains("Applied: Unknown", lines);
    }

    [Fact]
    public void Render_MissingPrefixSource_ShowsUnavailable()
    {
        RuntimeSnapshot snapshot =
            CreateSnapshot() with { PrefixSource = null };

        string[] lines = RuntimeSnapshotCliRenderer
            .Render(snapshot)
            .ToArray();

        Assert.Contains("Prefix Source:", lines);
        Assert.Contains("Unavailable.", lines);
    }

    [Fact]
    public void Render_PrefixSource_NeverUpdated()
    {
        RuntimeSnapshot snapshot = CreateSnapshot() with
        {
            PrefixSource = CreatePrefixSource() with
            {
                LastStatus = PrefixSourceUpdateStatus.NeverUpdated,
                LastSucceededAt = null,
                PrefixCount = 0,
                ContentHash = null
            }
        };

        string[] lines = RuntimeSnapshotCliRenderer
            .Render(snapshot)
            .ToArray();

        Assert.Contains("Status: Never updated", lines);
        Assert.Contains("Last success: -", lines);
        Assert.Contains("Hash: -", lines);
    }

    [Fact]
    public void Render_PrefixSource_FailedStatus()
    {
        RuntimeSnapshot snapshot = CreateSnapshot() with
        {
            PrefixSource = CreatePrefixSource() with
            {
                LastStatus = PrefixSourceUpdateStatus.Failed
            }
        };

        string[] lines = RuntimeSnapshotCliRenderer
            .Render(snapshot)
            .ToArray();

        Assert.Contains("Status: Failed", lines);
    }

    [Fact]
    public void Render_PrefixSource_NotModifiedStatus()
    {
        RuntimeSnapshot snapshot = CreateSnapshot() with
        {
            PrefixSource = CreatePrefixSource() with
            {
                LastStatus = PrefixSourceUpdateStatus.NotModified
            }
        };

        string[] lines = RuntimeSnapshotCliRenderer
            .Render(snapshot)
            .ToArray();

        Assert.Contains("Status: Not modified", lines);
    }

    [Fact]
    public void Render_PrefixSource_MissingChangeSummary_ShowsDashes()
    {
        RuntimeSnapshot snapshot = CreateSnapshot() with
        {
            PrefixSource = CreatePrefixSource() with
            {
                ChangeSummary = null
            }
        };

        string[] lines = RuntimeSnapshotCliRenderer
            .Render(snapshot)
            .ToArray();

        Assert.Contains("Added: -", lines);
        Assert.Contains("Removed: -", lines);
        Assert.Contains("Unchanged: -", lines);
        Assert.Contains("Changed: -", lines);
    }

    [Fact]
    public void Render_PrefixSource_NoChanges()
    {
        RuntimeSnapshot snapshot = CreateSnapshot() with
        {
            PrefixSource = CreatePrefixSource() with
            {
                ChangeSummary = CreateChangeSummary() with
                {
                    HasChanges = false,
                    AddedCount = 0,
                    RemovedCount = 0,
                    UnchangedCount = 1946
                }
            }
        };

        string[] lines = RuntimeSnapshotCliRenderer
            .Render(snapshot)
            .ToArray();

        Assert.Contains("Added: 0", lines);
        Assert.Contains("Removed: 0", lines);
        Assert.Contains("Unchanged: 1946", lines);
        Assert.Contains("Changed: No", lines);
    }

    [Fact]
    public void Render_PrefixSource_ShortHashNotTruncated()
    {
        RuntimeSnapshot snapshot = CreateSnapshot() with
        {
            PrefixSource = CreatePrefixSource() with
            {
                ContentHash = "abc123"
            }
        };

        string[] lines = RuntimeSnapshotCliRenderer
            .Render(snapshot)
            .ToArray();

        Assert.Contains("Hash: abc123", lines);
    }

    [Fact]
    public void Render_PrefixUpdate_Current()
    {
        RuntimeSnapshot snapshot = CreateSnapshot() with
        {
            PrefixUpdate = CreateUpdate(
                PrefixUpdateCheckStatus.Current)
        };

        string[] lines = RuntimeSnapshotCliRenderer
            .Render(snapshot)
            .ToArray();

        Assert.Contains("Prefix Update:", lines);
        Assert.Contains("Status:", lines);
        Assert.Contains("Current", lines);
    }

    [Fact]
    public void Render_PrefixUpdate_UpdateAvailable()
    {
        RuntimeSnapshot snapshot = CreateSnapshot() with
        {
            PrefixUpdate = CreateUpdate(
                PrefixUpdateCheckStatus.UpdateAvailable)
        };

        string[] lines = RuntimeSnapshotCliRenderer
            .Render(snapshot)
            .ToArray();

        Assert.Contains("Status:", lines);
        Assert.Contains("Update Available", lines);
    }

    [Fact]
    public void Render_PrefixUpdate_Unknown()
    {
        RuntimeSnapshot snapshot = CreateSnapshot() with
        {
            PrefixUpdate = CreateUpdate(
                PrefixUpdateCheckStatus.Unknown)
        };

        string[] lines = RuntimeSnapshotCliRenderer
            .Render(snapshot)
            .ToArray();

        Assert.Contains("Status:", lines);
        Assert.Contains("Unknown", lines);
    }

    [Fact]
    public void Render_PrefixUpdate_Failed()
    {
        RuntimeSnapshot snapshot = CreateSnapshot() with
        {
            PrefixUpdate = CreateUpdate(
                PrefixUpdateCheckStatus.Failed)
        };

        string[] lines = RuntimeSnapshotCliRenderer
            .Render(snapshot)
            .ToArray();

        Assert.Contains("Status:", lines);
        Assert.Contains("Failed", lines);
    }

    [Fact]
    public void Render_MissingPrefixUpdate_ShowsUnavailable()
    {
        RuntimeSnapshot snapshot =
            CreateSnapshot() with { PrefixUpdate = null };

        string[] lines = RuntimeSnapshotCliRenderer
            .Render(snapshot)
            .ToArray();

        Assert.Contains("Prefix Update:", lines);
        Assert.Contains("Unavailable.", lines);
    }

    [Fact]
    public void Render_PrefixUpdate_ShowsRemoteDetailsAndReason()
    {
        RuntimeSnapshot snapshot = CreateSnapshot() with
        {
            PrefixUpdate = CreateUpdate(
                PrefixUpdateCheckStatus.UpdateAvailable,
                lastModified: new DateTimeOffset(
                    2026, 8, 5, 18, 10, 0, TimeSpan.Zero),
                etag: "\"abc123\"",
                contentLength: 12345,
                reason: "ETag differs.")
        };

        string[] lines = RuntimeSnapshotCliRenderer
            .Render(snapshot)
            .ToArray();

        Assert.Contains("Status:", lines);
        Assert.Contains("Update Available", lines);
        Assert.Contains("Remote Last Modified:", lines);
        Assert.Contains("2026-08-05 18:10:00 UTC", lines);
        Assert.Contains("Remote ETag:", lines);
        Assert.Contains("\"abc123\"", lines);
        Assert.Contains("Remote Content Length:", lines);
        Assert.Contains("12345", lines);
        Assert.Contains("Reason:", lines);
        Assert.Contains("ETag differs.", lines);
    }

    [Fact]
    public void Render_PrefixUpdate_MissingRemoteDetails_OmitsFieldLines()
    {
        RuntimeSnapshot snapshot = CreateSnapshot() with
        {
            PrefixUpdate = CreateUpdate(
                PrefixUpdateCheckStatus.Failed,
                reason: "Remote check failed: network down")
        };

        string[] lines = RuntimeSnapshotCliRenderer
            .Render(snapshot)
            .ToArray();

        Assert.Contains("Status:", lines);
        Assert.Contains("Failed", lines);
        Assert.Contains("Reason:", lines);
        Assert.Contains("Remote check failed: network down", lines);
        Assert.DoesNotContain("Remote Last Modified:", lines);
        Assert.DoesNotContain("Remote ETag:", lines);
        Assert.DoesNotContain("Remote Content Length:", lines);
    }

    private static RuntimeSnapshot CreateSnapshot()
    {
        return new RuntimeSnapshot
        {
            CapturedAt = new DateTimeOffset(
                2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Configuration = new DesiredConfiguration
            {
                Enabled = true,
                RepairInterval = TimeSpan.FromMinutes(30),
                PrefixUpdateInterval = TimeSpan.FromDays(1),
                VpnProfilePath = "vpn-profile.ovpn",
                AutoRepair = true
            },
            Runtime = new IranDirectStatus
            {
                Enabled = true
            },
            PrefixCount = 5,
            InstalledRouteCount = 3,
            RouteInventoryCount = 4,
            PrefixSource = CreatePrefixSource(),
            Operation = new RuntimeOperationSnapshot
            {
                State = OperationState.Idle
            },
            VpnEndpointHealth = new VpnEndpointProtectionHealth
            {
                CurrentEndpointCount = 2,
                ProtectedEndpointCount = 1
            },
            DnsCache =
            [
                new CustomRouteDnsCacheStatus
                {
                    State = CustomRouteDnsCacheState.Fresh
                },
                new CustomRouteDnsCacheStatus
                {
                    State = CustomRouteDnsCacheState.Fresh
                },
                new CustomRouteDnsCacheStatus
                {
                    State = CustomRouteDnsCacheState.Fresh
                },
                new CustomRouteDnsCacheStatus
                {
                    State = CustomRouteDnsCacheState.Stale
                }
            ],
            Performance = new RuntimeCyclePerfReport
            {
                Trigger = "enable",
                StartedAt = new DateTimeOffset(
                    2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                CompletedAt = new DateTimeOffset(
                    2026, 1, 1, 0, 0, 42, TimeSpan.Zero),
                TotalMs = 42100,
                Categories = []
            }
        };
    }

    private static PrefixUpdateCheckResult CreateUpdate(
        PrefixUpdateCheckStatus status,
        DateTimeOffset? lastModified = null,
        string? etag = null,
        long? contentLength = null,
        string? reason = null)
    {
        PrefixUpdateCheckRemoteMetadata? remote =
            lastModified is null
                && etag is null
                && contentLength is null
                    ? null
                    : new PrefixUpdateCheckRemoteMetadata
                    {
                        LastModified = lastModified,
                        ETag = etag,
                        ContentLength = contentLength
                    };

        return new PrefixUpdateCheckResult
        {
            Status = status,
            CheckedAt = new DateTimeOffset(
                2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            RemoteMetadata = remote,
            Reason = reason
        };
    }

    private static PrefixSourceMetadata CreatePrefixSource()
    {
        return new PrefixSourceMetadata
        {
            SourceId = "ripe-stat-country-resource-list-ipv4",
            SourceDisplayName = "RIPE",
            SourceUri =
                "https://stat.ripe.net/data/country-resource-list/data.json?resource=IR",
            Format = "ripestat-country-resource-list-json",
            ParserVersion = "1",
            LastAttemptedAt = new DateTimeOffset(
                2026, 8, 3, 14, 10, 0, TimeSpan.Zero),
            LastSucceededAt = new DateTimeOffset(
                2026, 8, 3, 14, 10, 0, TimeSpan.Zero),
            ContentHash =
                "ab12ef34" + new string('c', 56),
            ContentLength = 512,
            PrefixCount = 1946,
            DownloadDuration = TimeSpan.FromSeconds(4),
            LastStatus = PrefixSourceUpdateStatus.Succeeded,
            ChangeSummary = CreateChangeSummary()
        };
    }

    [Fact]
    public void Render_WithMonitor_IncludesMonitorFields()
    {
        RuntimeSnapshot snapshot = CreateSnapshot() with
        {
            PrefixUpdateMonitor = new PrefixUpdateMonitorSnapshot
            {
                CurrentResult = CreateUpdate(
                    PrefixUpdateCheckStatus.Current),
                LastCheckedAt = new DateTimeOffset(
                    2026, 8, 3, 14, 10, 0, TimeSpan.Zero),
                LastSuccessfulCheckAt = new DateTimeOffset(
                    2026, 8, 3, 14, 10, 0, TimeSpan.Zero),
                ConsecutiveFailures = 2,
                Running = true,
                Checking = false
            }
        };

        string[] lines = RuntimeSnapshotCliRenderer
            .Render(snapshot)
            .ToArray();

        Assert.Contains("Prefix Update Monitor:", lines);
        Assert.Contains("Monitor Running: True", lines);
        Assert.Contains("Checking: False", lines);
        Assert.Contains(
            "Last Checked: 2026-08-03 14:10:00 UTC",
            lines);
        Assert.Contains(
            "Last Successful Check: 2026-08-03 14:10:00 UTC",
            lines);
        Assert.Contains("Consecutive Failures: 2", lines);
    }

    [Fact]
    public void Render_NullMonitor_ShowsUnavailable()
    {
        RuntimeSnapshot snapshot =
            CreateSnapshot() with { PrefixUpdateMonitor = null };

        string[] lines = RuntimeSnapshotCliRenderer
            .Render(snapshot)
            .ToArray();

        Assert.Contains("Prefix Update Monitor:", lines);
        Assert.Contains("Unavailable.", lines);
        Assert.DoesNotContain("Monitor Running: True", lines);
    }

    private static PrefixSourceChangeSummary CreateChangeSummary()
    {
        return new PrefixSourceChangeSummary
        {
            PreviousContentHash = new string('b', 64),
            CurrentContentHash =
                "ab12ef34" + new string('c', 56),
            AddedCount = 14,
            RemovedCount = 6,
            UnchangedCount = 1940,
            HasChanges = true,
            ComparedAt = new DateTimeOffset(
                2026, 8, 3, 14, 10, 0, TimeSpan.Zero)
        };
    }
}
