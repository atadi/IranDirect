using IranDirect.Core.Cli;
using IranDirect.Core.Configuration;
using IranDirect.Core.CustomRoutes;
using IranDirect.Core.Observability;
using IranDirect.Core.Runtime.Execution;
using IranDirect.Core.Runtime.Profiling;
using IranDirect.Core.Vpn;

namespace IranDirect.Core.Tests.Cli;

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
