using System.Security.Cryptography;
using System.Text;
using PathVeer.Core.Configuration;
using PathVeer.Core.CustomRoutes;
using PathVeer.Core.Prefixes;
using PathVeer.Core.Routing;
using PathVeer.Core.Runtime.Profiling;
using PathVeer.Core.State;
using PathVeer.Core.Vpn;

namespace PathVeer.Testing.Performance.Persistence;

/// <summary>
/// Deterministic fixtures for the persistence endurance harness. Every
/// value is derived from the seed and the revision index, so schedules
/// and documents are stable across runs without relying on wall-clock
/// time.
/// </summary>
public static class PersistenceEnduranceFixtures
{
    public const string GeneratorVersion = "1.0";

    public const int DefaultSeed = 20260803;

    private static readonly DateTimeOffset BaseTime =
        new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);

    public static string HexHash(string value) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    public static Guid DeterministicGuid(int index) =>
        Guid.Parse(index.ToString("x32"));

    public static PersistenceEnduranceDocument Document(
        int seed,
        int revision) =>
        new()
        {
            Revision = revision,
            Payload = HexHash($"payload-{seed}-{revision}")
        };

    public static RouteInventoryItem RouteItem(int index) =>
        new()
        {
            DestinationPrefix =
                $"10.{index % 256}.{(index / 256) % 256}.0/24",
            Gateway = $"192.168.0.{index % 254 + 1}",
            InterfaceIndex = (uint)(index % 8 + 1),
            Metric = index % 100 + 1
        };

    public static RouteInventory RouteInventory(
        int seed,
        int count) =>
        new()
        {
            SchemaVersion = 1,
            Routes = Enumerable.Range(0, count)
                .Select(RouteItem)
                .ToArray()
        };

    public static VpnEndpointInventoryItem EndpointItem(int index) =>
        new()
        {
            Host = $"vpn-{index}.example.test",
            Address = $"203.0.113.{index % 254 + 1}",
            Port = 1194 + index % 100,
            Protocol = "udp",
            DestinationPrefix = $"10.{index % 256}.0.0/24",
            Gateway = $"192.168.1.{index % 254 + 1}",
            InterfaceIndex = (uint)(index % 4 + 1),
            Metric = 1,
            AddedByIranDirect = true,
            IsCurrent = true,
            ProtectedAt = BaseTime.AddSeconds(index),
            LastSeenAt = BaseTime.AddSeconds(index)
        };

    public static VpnEndpointInventory VpnEndpointInventory(
        int seed,
        int count) =>
        new()
        {
            SchemaVersion = 1,
            Endpoints = Enumerable.Range(0, count)
                .Select(EndpointItem)
                .ToArray()
        };

    public static IranDirectState State(int revision) =>
        new()
        {
            Enabled = revision % 2 == 0,
            Gateway = $"192.168.5.{revision % 254 + 1}",
            InterfaceIndex = (uint)(revision % 4 + 1),
            InterfaceName = $"iface-{revision % 4}",
            PrefixCount = revision,
            EnabledAt = BaseTime.AddMinutes(revision % 1024),
            PrefixesUpdatedAt = BaseTime.AddHours(revision % 24),
            LastError = revision % 7 == 0
                ? "endurance-fault"
                : null
        };

    public static DesiredConfiguration Configuration(int revision) =>
        new()
        {
            SchemaVersion = 1,
            Enabled = revision % 2 == 0,
            VpnProvider = VpnProviderType.OpenVpn,
            VpnProfilePath = $"profile-{revision % 3}.ovpn",
            AutoRepair = revision % 3 != 0,
            RepairInterval = TimeSpan.FromSeconds(10 + revision % 50),
            AutoUpdatePrefixes = revision % 2 == 0,
            PrefixUpdateInterval =
                TimeSpan.FromMinutes(15 + revision % 120)
        };

    public static CustomRouteEntry CustomRoute(int index) =>
        new()
        {
            Id = DeterministicGuid(index),
            Type = index % 2 == 0
                ? CustomRouteEntryType.Cidr
                : CustomRouteEntryType.Domain,
            Value = index % 2 == 0
                ? $"10.{index % 256}.0.0/16"
                : $"host-{index}.example.test",
            Enabled = true,
            Description = $"Endurance route {index}",
            CreatedAt = BaseTime.AddSeconds(index),
            ModifiedAt = BaseTime.AddSeconds(index)
        };

    public static CustomRouteCollection CustomRoutes(
        int seed,
        int count) =>
        new()
        {
            SchemaVersion = 1,
            Entries = Enumerable.Range(0, count)
                .Select(CustomRoute)
                .ToArray()
        };

    public static CustomRouteDnsCacheEntry DnsCacheEntry(int index) =>
        new()
        {
            CustomRouteEntryId = DeterministicGuid(index),
            Domain = $"host-{index}.example.test",
            IPv4Addresses = [$"203.0.113.{index % 254 + 1}"],
            LastAttemptedAt = BaseTime.AddSeconds(index),
            LastSucceededAt = BaseTime.AddSeconds(index),
            ExpiresAt = BaseTime.AddMinutes(index % 60 + 5)
        };

    public static CustomRouteDnsCacheCollection DnsCache(
        int seed,
        int count) =>
        new()
        {
            SchemaVersion = 1,
            Entries = Enumerable.Range(0, count)
                .Select(DnsCacheEntry)
                .ToArray()
        };

    public static PrefixSourceMetadata Metadata(int index) =>
        new()
        {
            SourceId = $"source-{index % 3}",
            SourceDisplayName = $"Source {index % 3}",
            SourceUri = $"https://example.test/{index % 3}.txt",
            Format = "text",
            ParserVersion = "1.0",
            LastAttemptedAt = BaseTime.AddSeconds(index),
            LastSucceededAt = BaseTime.AddSeconds(index),
            SourceLastModified = BaseTime.AddSeconds(index),
            ETag = $"\"etag-{index}\"",
            ContentHash = HexHash($"content-{index}"),
            ContentLength = 1_000 + index,
            PrefixCount = index % 1_000,
            DownloadDuration = TimeSpan.FromMilliseconds(index % 500),
            LastStatus = PrefixSourceUpdateStatus.Succeeded
        };

    public static PrefixSourceMetadataDocument MetadataDocument(
        int seed,
        int index) =>
        new()
        {
            SchemaVersion = 1,
            Current = Metadata(index)
        };

    public static PrefixSourceUpdateHistoryEntry HistoryEntry(
        int index,
        PrefixSourceUpdateStatus status =
            PrefixSourceUpdateStatus.Succeeded) =>
        new()
        {
            Id = DeterministicGuid(index),
            SourceId = "source-0",
            SourceDisplayName = "Source 0",
            SourceUri = "https://example.test/0.txt",
            Format = "text",
            ParserVersion = "1.0",
            Status = status,
            StartedAt = BaseTime.AddSeconds(index),
            CompletedAt = BaseTime.AddSeconds(index + 1),
            Duration = TimeSpan.FromMilliseconds(index % 500),
            AttemptedAt = BaseTime.AddSeconds(index),
            SourceLastModified = BaseTime.AddSeconds(index),
            ETag = $"\"etag-{index}\"",
            PreviousContentHash = HexHash($"previous-{index}"),
            CurrentContentHash = HexHash($"current-{index}"),
            ContentLength = 1_000 + index,
            PrefixCount = index % 1_000,
            AddedCount = index % 5,
            RemovedCount = index % 3,
            UnchangedCount = index % 900,
            HasChanges = true,
            Error = status == PrefixSourceUpdateStatus.Failed
                ? "endurance failure"
                : null
        };

    public static RuntimeCyclePerfReport PerfReport(
        string trigger,
        DateTimeOffset completedAt,
        int index) =>
        new()
        {
            Trigger = trigger,
            StartedAt = completedAt.AddSeconds(-1),
            CompletedAt = completedAt,
            TotalMs = index % 10_000 + 1.5,
            CompletionStatus = CycleCompletionStatus.Completed,
            PlannedSteps = index % 20,
            CompletedSteps = index % 20,
            Categories =
            [
                new RuntimePerfCategorySummary(
                    RuntimePerfCategory.ExecutionRouteCreate,
                    index,
                    index,
                    index,
                    index,
                    index,
                    index)
            ]
        };

    public static PersistenceEnduranceDocument Document(int revision) =>
        Document(DefaultSeed, revision);
}

public sealed record PersistenceEnduranceDocument
{
    public int Revision { get; init; }

    public string Payload { get; init; } = "";
}
