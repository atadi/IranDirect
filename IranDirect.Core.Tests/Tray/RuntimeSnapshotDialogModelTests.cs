using IranDirect.Core.Configuration;
using IranDirect.Core.CustomRoutes;
using IranDirect.Core.Observability;
using IranDirect.Core.Runtime.Execution;
using IranDirect.Core.Runtime.Profiling;
using IranDirect.Core.Vpn;
using IranDirect.Tray;

namespace IranDirect.Core.Tests.Tray;

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
                "Performance"
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
}
