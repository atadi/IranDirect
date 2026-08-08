using PathVeer.Core.Configuration;
using PathVeer.Core.CustomRoutes;
using PathVeer.Core.Observability;
using PathVeer.Core.Prefixes;
using PathVeer.Core.Runtime.Execution;
using PathVeer.Core.Runtime.Profiling;
using PathVeer.Core.Vpn;
using PathVeer.Tray;

namespace PathVeer.Core.Tests.Tray;

public sealed class RuntimeSnapshotDialogModelTests
{
    [Fact]
    public void MapSections_CoversAllSnapshotGroups()
    {
        RuntimeSnapshot snapshot = CreateSnapshot();

        IReadOnlyList<SnapshotSection> sections =
            RuntimeSnapshotDialogModel.MapSections(snapshot);

        Assert.Equal(
            [
                "Configuration",
                "Runtime",
                "Operation",
                "Routes",
                "VPN",
                "DNS",
                "Performance",
                "Prefix Source",
                "Prefix Update"
            ],
            sections.Select(section => section.Title));
    }

    [Fact]
    public void MapSections_MapsDisplayValues()
    {
        RuntimeSnapshot snapshot = CreateSnapshot();

        IReadOnlyList<SnapshotSection> sections =
            RuntimeSnapshotDialogModel.MapSections(snapshot);

        SnapshotSection configuration = sections[0];
        Assert.Contains(
            new SnapshotSectionRow("Desired", "Yes"),
            configuration.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Repair interval", "00:30:00"),
            configuration.Rows);
        Assert.Contains(
            new SnapshotSectionRow("VPN profile", "vpn-profile.ovpn"),
            configuration.Rows);

        SnapshotSection runtime = sections[1];
        Assert.Contains(
            new SnapshotSectionRow("Applied", "Yes"),
            runtime.Rows);

        SnapshotSection operation = sections[2];
        Assert.Contains(
            new SnapshotSectionRow("State", "Idle"),
            operation.Rows);

        SnapshotSection routes = sections[3];
        Assert.Contains(
            new SnapshotSectionRow("Managed", "3"),
            routes.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Desired", "5"),
            routes.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Inventory", "4"),
            routes.Rows);

        SnapshotSection vpn = sections[4];
        Assert.Contains(
            new SnapshotSectionRow("Protected", "1 / 2"),
            vpn.Rows);

        SnapshotSection dns = sections[5];
        Assert.Contains(
            new SnapshotSectionRow("Domains", "4"),
            dns.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Fresh", "3"),
            dns.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Stale", "1"),
            dns.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Failed", "0"),
            dns.Rows);

        SnapshotSection performance = sections[6];
        Assert.Contains(
            new SnapshotSectionRow("Last cycle", "42.1 sec"),
            performance.Rows);

        SnapshotSection prefixSource = sections[7];
        Assert.Contains(
            new SnapshotSectionRow("Source", "RIPE"),
            prefixSource.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Status", "Succeeded"),
            prefixSource.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Prefix Count", "1946"),
            prefixSource.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Added", "14"),
            prefixSource.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Removed", "6"),
            prefixSource.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Unchanged", "1940"),
            prefixSource.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Hash", "ab12ef34..."),
            prefixSource.Rows);
    }

    [Fact]
    public void MapSections_MissingPrefixSource_ShowsNotAvailable()
    {
        RuntimeSnapshot snapshot =
            CreateSnapshot() with { PrefixSource = null };

        SnapshotSection prefixSource = RuntimeSnapshotDialogModel
            .MapSections(snapshot)[7];

        Assert.Contains(
            new SnapshotSectionRow("Source", "-"),
            prefixSource.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Status", "-"),
            prefixSource.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Last Success", "-"),
            prefixSource.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Prefix Count", "-"),
            prefixSource.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Added", "-"),
            prefixSource.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Removed", "-"),
            prefixSource.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Unchanged", "-"),
            prefixSource.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Hash", "-"),
            prefixSource.Rows);
    }

    [Fact]
    public void MapSections_PrefixSource_NeverUpdated()
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

        SnapshotSection prefixSource = RuntimeSnapshotDialogModel
            .MapSections(snapshot)[7];

        Assert.Contains(
            new SnapshotSectionRow("Status", "Never updated"),
            prefixSource.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Last Success", "-"),
            prefixSource.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Hash", "-"),
            prefixSource.Rows);
    }

    [Fact]
    public void MapSections_PrefixSource_FailedAndNotModifiedStatuses()
    {
        RuntimeSnapshot failed = CreateSnapshot() with
        {
            PrefixSource = CreatePrefixSource() with
            {
                LastStatus = PrefixSourceUpdateStatus.Failed
            }
        };
        RuntimeSnapshot notModified = CreateSnapshot() with
        {
            PrefixSource = CreatePrefixSource() with
            {
                LastStatus = PrefixSourceUpdateStatus.NotModified
            }
        };

        SnapshotSection failedSection = RuntimeSnapshotDialogModel
            .MapSections(failed)[7];
        SnapshotSection notModifiedSection =
            RuntimeSnapshotDialogModel
                .MapSections(notModified)[7];

        Assert.Contains(
            new SnapshotSectionRow("Status", "Failed"),
            failedSection.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Status", "Not modified"),
            notModifiedSection.Rows);
    }

    [Fact]
    public void MapSections_PrefixSource_MissingChangeSummary_ShowsDashes()
    {
        RuntimeSnapshot snapshot = CreateSnapshot() with
        {
            PrefixSource = CreatePrefixSource() with
            {
                ChangeSummary = null
            }
        };

        SnapshotSection prefixSource = RuntimeSnapshotDialogModel
            .MapSections(snapshot)[7];

        Assert.Contains(
            new SnapshotSectionRow("Added", "-"),
            prefixSource.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Removed", "-"),
            prefixSource.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Unchanged", "-"),
            prefixSource.Rows);
    }

    [Fact]
    public void MapSections_PrefixSource_NoChanges()
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

        SnapshotSection prefixSource = RuntimeSnapshotDialogModel
            .MapSections(snapshot)[7];

        Assert.Contains(
            new SnapshotSectionRow("Added", "0"),
            prefixSource.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Removed", "0"),
            prefixSource.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Unchanged", "1946"),
            prefixSource.Rows);
    }

    [Fact]
    public void MapSections_PrefixSource_ShortHashNotTruncated()
    {
        RuntimeSnapshot snapshot = CreateSnapshot() with
        {
            PrefixSource = CreatePrefixSource() with
            {
                ContentHash = "abc123"
            }
        };

        SnapshotSection prefixSource = RuntimeSnapshotDialogModel
            .MapSections(snapshot)[7];

        Assert.Contains(
            new SnapshotSectionRow("Hash", "abc123"),
            prefixSource.Rows);
    }

    [Fact]
    public void MapSections_NoDnsEntries_ShowsZeroCounts()
    {
        RuntimeSnapshot snapshot =
            CreateSnapshot() with { DnsCache = [] };

        SnapshotSection dns = RuntimeSnapshotDialogModel
            .MapSections(snapshot)[5];

        Assert.Contains(
            new SnapshotSectionRow("Domains", "0"),
            dns.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Fresh", "0"),
            dns.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Stale", "0"),
            dns.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Failed", "0"),
            dns.Rows);
    }

    [Fact]
    public void MapSections_EmptyPerformance_ShowsNone()
    {
        RuntimeSnapshot snapshot =
            CreateSnapshot() with { Performance = null };

        SnapshotSection performance = RuntimeSnapshotDialogModel
            .MapSections(snapshot)[6];

        Assert.Contains(
            new SnapshotSectionRow("Last cycle", "none"),
            performance.Rows);
    }

    [Fact]
    public void FormatPerformance_ConvertsMillisecondsToSeconds()
    {
        Assert.Equal(
            "none",
            RuntimeSnapshotDialogModel.FormatPerformance(null));
        Assert.Equal(
            "42.1 sec",
            RuntimeSnapshotDialogModel.FormatPerformance(
                new RuntimeCyclePerfReport
                {
                    Trigger = "enable",
                    StartedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow,
                    TotalMs = 42100,
                    Categories = []
                }));
    }

    [Fact]
    public void MapSections_PrefixUpdate_AllStatuses()
    {
        (PrefixUpdateCheckStatus Status, string Expected)[] cases =
        [
            (PrefixUpdateCheckStatus.Current, "Current"),
            (
                PrefixUpdateCheckStatus.UpdateAvailable,
                "Update Available"),
            (PrefixUpdateCheckStatus.Unknown, "Unknown"),
            (PrefixUpdateCheckStatus.Failed, "Failed")
        ];

        foreach ((PrefixUpdateCheckStatus status, string expected)
            in cases)
        {
            RuntimeSnapshot snapshot = CreateSnapshot() with
            {
                PrefixUpdate = CreateUpdate(status)
            };

            SnapshotSection section = RuntimeSnapshotDialogModel
                .MapSections(snapshot)[8];

            Assert.Contains(
                new SnapshotSectionRow("Status", expected),
                section.Rows);
        }
    }

    [Fact]
    public void MapSections_PrefixUpdate_NullSnapshot_ShowsNotAvailable()
    {
        RuntimeSnapshot snapshot =
            CreateSnapshot() with { PrefixUpdate = null };

        SnapshotSection section = RuntimeSnapshotDialogModel
            .MapSections(snapshot)[8];

        Assert.Contains(
            new SnapshotSectionRow("Status", "-"),
            section.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Remote Last Modified", "-"),
            section.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Remote ETag", "-"),
            section.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Remote Size", "-"),
            section.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Reason", "-"),
            section.Rows);
    }

    [Fact]
    public void MapSections_PrefixUpdate_FormatsRemoteFields()
    {
        DateTimeOffset lastModified = new(
            2026, 8, 5, 18, 10, 0, TimeSpan.Zero);
        string expectedLastModified =
            lastModified.ToLocalTime().ToString(
                "yyyy-MM-dd HH:mm:ss");

        RuntimeSnapshot snapshot = CreateSnapshot() with
        {
            PrefixUpdate = CreateUpdate(
                PrefixUpdateCheckStatus.UpdateAvailable,
                lastModified: lastModified,
                etag: "\"abc123\"",
                contentLength: 12345,
                reason: "ETag differs.")
        };

        SnapshotSection section = RuntimeSnapshotDialogModel
            .MapSections(snapshot)[8];

        Assert.Contains(
            new SnapshotSectionRow(
                "Status",
                "Update Available"),
            section.Rows);
        Assert.Contains(
            new SnapshotSectionRow(
                "Remote Last Modified",
                expectedLastModified),
            section.Rows);
        Assert.Contains(
            new SnapshotSectionRow(
                "Remote ETag",
                "\"abc123\""),
            section.Rows);
        Assert.Contains(
            new SnapshotSectionRow(
                "Remote Size",
                "12345"),
            section.Rows);
        Assert.Contains(
            new SnapshotSectionRow(
                "Reason",
                "ETag differs."),
            section.Rows);
    }

    [Fact]
    public void MapSections_PrefixUpdate_MissingRemoteDetails_ShowsNotAvailable()
    {
        RuntimeSnapshot snapshot = CreateSnapshot() with
        {
            PrefixUpdate = CreateUpdate(
                PrefixUpdateCheckStatus.Failed,
                reason: "Remote check failed: network down")
        };

        SnapshotSection section = RuntimeSnapshotDialogModel
            .MapSections(snapshot)[8];

        Assert.Contains(
            new SnapshotSectionRow("Status", "Failed"),
            section.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Remote Last Modified", "-"),
            section.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Remote ETag", "-"),
            section.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Remote Size", "-"),
            section.Rows);
        Assert.Contains(
            new SnapshotSectionRow(
                "Reason",
                "Remote check failed: network down"),
            section.Rows);
    }

    [Fact]
    public void MapSections_WithMonitor_MapsMonitorRows()
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
                ConsecutiveFailures = 3,
                Running = true,
                Checking = false
            }
        };

        SnapshotSection section = RuntimeSnapshotDialogModel
            .MapSections(snapshot)[8];

        Assert.Contains(
            new SnapshotSectionRow("Monitor Running", "Yes"),
            section.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Checking", "No"),
            section.Rows);
        Assert.Contains(
            new SnapshotSectionRow(
                "Last Checked",
                new DateTimeOffset(
                    2026, 8, 3, 14, 10, 0, TimeSpan.Zero)
                    .ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
            section.Rows);
        Assert.Contains(
            new SnapshotSectionRow(
                "Last Successful Check",
                new DateTimeOffset(
                    2026, 8, 3, 14, 10, 0, TimeSpan.Zero)
                    .ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
            section.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Consecutive Failures", "3"),
            section.Rows);
    }

    [Fact]
    public void MapSections_NullMonitor_MonitorRowsShowNotAvailable()
    {
        RuntimeSnapshot snapshot =
            CreateSnapshot() with { PrefixUpdateMonitor = null };

        SnapshotSection section = RuntimeSnapshotDialogModel
            .MapSections(snapshot)[8];

        Assert.Contains(
            new SnapshotSectionRow("Monitor Running", "-"),
            section.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Checking", "-"),
            section.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Last Checked", "-"),
            section.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Last Successful Check", "-"),
            section.Rows);
        Assert.Contains(
            new SnapshotSectionRow("Consecutive Failures", "-"),
            section.Rows);
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
            Runtime = new PathVeerStatus
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
