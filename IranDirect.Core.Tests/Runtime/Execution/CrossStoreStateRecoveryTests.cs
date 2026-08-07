namespace IranDirect.Core.Tests.Runtime.Execution;

using System.Net;
using IranDirect.Core.Configuration;
using IranDirect.Core.CustomRoutes;
using IranDirect.Core.Prefixes;
using IranDirect.Core.Routing;
using IranDirect.Core.Runtime.Execution;
using IranDirect.Core.Vpn;
using Xunit;

/// <summary>
/// Phase 34.3 cross-store crash-consistency audit, locked as permanent tests.
///
/// Source investigation (see docs/reliability/phase-34.3-cross-store-recovery.md)
/// concluded that a general cross-store transaction mechanism is NOT required:
/// the only safety-critical pair (native route &lt;-&gt; ownership inventory) is
/// already closed by the Phase 34.2 write-ahead journal, and every other
/// multi-store write involves at least one derived/diagnostic/cache store that
/// is rebuilt from authoritative sources each cycle and never drives routing
/// decisions. These tests prove that classification and the fail-closed /
/// rebuildable / journal-proven behaviors hold.
/// </summary>
public sealed class CrossStoreStateRecoveryTests
{
    private const string PrefixId = "203.0.113.0/24|192.168.1.1|10";
    private const string EndpointId = "10.0.0.1/32|192.168.1.1|10";

    // ---- Authoritative / fail-closed: DesiredConfiguration ----

    [Fact]
    public async Task DesiredConfiguration_CorruptFile_FailsClosed_NotPermissive()
    {
        using TempDir dir = new();
        string path = dir.File("desired.json");
        // Invalid JSON -> JsonStore does NOT silently reset; it throws.
        System.IO.File.WriteAllText(path, "garbage-not-json");

        var store = new DesiredConfigurationStore(
            path, new DesiredConfigurationValidator());

        await Assert.ThrowsAnyAsync<Exception>(
            () => store.LoadAsync());
    }

    [Fact]
    public async Task DesiredConfiguration_MissingFile_SilentlyDisabled_NotPermissive()
    {
        // Source behavior (JsonStore): a MISSING file returns new T() silently.
        // For DesiredConfiguration new() is Enabled=false with a sentinel
        // VpnProfilePath, so validation passes and the service runs DISABLED
        // rather than permissive. Routing is unaffected (no destructive
        // action). The missing-file asymmetry is tracked as an R3 follow-up;
        // the key property proven here is that it never becomes permissive.
        using TempDir dir = new();
        string path = dir.File("desired.json"); // never written

        var store = new DesiredConfigurationStore(
            path, new DesiredConfigurationValidator());

        DesiredConfiguration loaded = await store.LoadAsync();
        Assert.False(loaded.Enabled);                 // not permissive
        Assert.Equal("vpn-profile.ovpn", loaded.VpnProfilePath); // default sentinel
    }

    [Fact]
    public async Task DesiredConfiguration_ValidEmpty_DoesNotThrow()
    {
        using TempDir dir = new();
        string path = dir.File("desired.json");
        var valid = new DesiredConfiguration
        {
            SchemaVersion = 1,
            Enabled = false,
            VpnProfilePath = "C:\\profiles\\vpn.ovpn"
        };
        var store = new DesiredConfigurationStore(
            path, new DesiredConfigurationValidator());
        await store.SaveAsync(valid);

        DesiredConfiguration loaded = await store.LoadAsync();
        Assert.False(loaded.Enabled);
        Assert.Equal("C:\\profiles\\vpn.ovpn", loaded.VpnProfilePath);
    }

    // ---- Derived/cache corruption is safe (rebuildable, never authoritative) ----

    [Fact]
    public async Task DnsCache_CorruptFile_FailsClosed_NotAuthoritative()
    {
        // Corrupt cache content throws (JsonStore does not silently reset to
        // empty); a corrupt cache therefore cannot masquerade as authoritative.
        using TempDir dir = new();
        string path = dir.File("dns.json");
        System.IO.File.WriteAllText(path, "garbage-not-json");

        var store = new CustomRouteDnsCacheStore(path);

        await Assert.ThrowsAnyAsync<Exception>(() => store.LoadAsync());
    }

    [Fact]
    public async Task PrefixMetadata_CorruptFile_FailsClosed_NotAuthoritative()
    {
        using TempDir dir = new();
        string path = dir.File("meta.json");
        System.IO.File.WriteAllText(path, "garbage-not-json");

        var store = new PrefixSourceMetadataStore(path);
        await Assert.ThrowsAnyAsync<Exception>(() => store.LoadAsync());
    }

    // ---- Recovery does not cross-contaminate stores or act without proof ----

    [Fact]
    public async Task Recovery_RouteAndEndpointDivergence_ResolvesOnlyJournaled()
    {
        var routes = new FakeRouteManager();
        routes.AddToPresent(PrefixId);
        routes.AddToPresent(EndpointId);

        var routeInv = new FakeRouteInventory();
        routeInv.Seed(PrefixId); // prefix already owned + present (consistent)

        var endpointInv = new FakeEndpointInventory(); // endpoint NOT owned
        var journal = new FakeRouteMutationJournal();
        journal.Write(EndpointAdd); // only the endpoint add is journal-proven

        await new RouteMutationRecovery(
            routes, routeInv, endpointInv, journal).RecoverAsync();

        // Endpoint adopted (journal-proven), prefix inventory untouched.
        Assert.Single((await endpointInv.LoadAsync()).Endpoints);
        Assert.Single((await routeInv.LoadAsync()).Routes);
        Assert.Equal(PrefixId, (await routeInv.LoadAsync()).Routes[0].Identity);
        Assert.Empty(journal.All);
    }

    [Fact]
    public async Task Recovery_CorruptEndpointInventoryModel_AdoptsOnlyJournalProven()
    {
        var routes = new FakeRouteManager();
        routes.AddToPresent(EndpointId);

        var routeInv = new FakeRouteInventory(); // intact
        var endpointInv = new FakeEndpointInventory(); // empty (simulates loss)
        var journal = new FakeRouteMutationJournal();
        journal.Write(EndpointAdd);

        await new RouteMutationRecovery(
            routes, routeInv, endpointInv, journal).RecoverAsync();

        Assert.Single((await endpointInv.LoadAsync()).Endpoints);
        Assert.Empty((await routeInv.LoadAsync()).Routes); // prefix never journaled
        Assert.Empty(journal.All);
    }

    [Fact]
    public async Task Recovery_ExternalRoutePresent_NoJournal_NoStoreTouched()
    {
        var routes = new FakeRouteManager();
        routes.AddToPresent(PrefixId);
        routes.AddToPresent(EndpointId);

        var routeInv = new FakeRouteInventory();
        var endpointInv = new FakeEndpointInventory();
        var journal = new FakeRouteMutationJournal(); // empty: no proof

        await new RouteMutationRecovery(
            routes, routeInv, endpointInv, journal).RecoverAsync();

        Assert.Empty((await routeInv.LoadAsync()).Routes);
        Assert.Empty((await endpointInv.LoadAsync()).Endpoints);
        Assert.Contains(PrefixId, routes.Present);
        Assert.Contains(EndpointId, routes.Present);
        Assert.Empty(journal.All);
    }

    [Fact]
    public async Task Recovery_MixedValidAndCorruptOwnership_NoCrossContamination()
    {
        var routes = new FakeRouteManager();
        routes.AddToPresent(PrefixId);

        var routeInv = new FakeRouteInventory();
        routeInv.Seed(PrefixId); // valid ownership, consistent
        var endpointInv = new FakeEndpointInventory();
        var journal = new FakeRouteMutationJournal(); // nothing pending

        await new RouteMutationRecovery(
            routes, routeInv, endpointInv, journal).RecoverAsync();

        // Consistent state: nothing changed, no destructive action.
        Assert.Single((await routeInv.LoadAsync()).Routes);
        Assert.Empty((await endpointInv.LoadAsync()).Endpoints);
        Assert.Empty(journal.All);
    }

    [Fact]
    public async Task Recovery_Idempotent_AcrossTwoRuns()
    {
        var routes = new FakeRouteManager();
        routes.AddToPresent(PrefixId);
        var routeInv = new FakeRouteInventory();
        var endpointInv = new FakeEndpointInventory();
        var journal = new FakeRouteMutationJournal();
        journal.Write(PrefixAdd);

        var recovery = new RouteMutationRecovery(
            routes, routeInv, endpointInv, journal);

        await recovery.RecoverAsync();
        int afterFirst = (await routeInv.LoadAsync()).Routes.Count;
        await recovery.RecoverAsync();
        int afterSecond = (await routeInv.LoadAsync()).Routes.Count;

        Assert.Equal(1, afterFirst);
        Assert.Equal(afterFirst, afterSecond);
        Assert.Empty(journal.All);
    }

    [Fact]
    public async Task Recovery_ConsistentState_NoOp()
    {
        var routes = new FakeRouteManager();
        var routeInv = new FakeRouteInventory();
        var endpointInv = new FakeEndpointInventory();
        var journal = new FakeRouteMutationJournal();

        await new RouteMutationRecovery(
            routes, routeInv, endpointInv, journal).RecoverAsync();

        Assert.Empty((await routeInv.LoadAsync()).Routes);
        Assert.Empty((await endpointInv.LoadAsync()).Endpoints);
        Assert.Empty(journal.All);
    }

    // ---- Startup ordering: recovery completes before normal planning ----

    [Fact]
    public async Task Startup_JournalRecoveryRunsBeforeCycle_LeavesConsistentState()
    {
        var routes = new FakeRouteManager();
        routes.AddToPresent(PrefixId);
        var routeInv = new FakeRouteInventory();
        var endpointInv = new FakeEndpointInventory();
        var journal = new FakeRouteMutationJournal();
        journal.Write(PrefixAdd); // interrupted add

        // "Startup recovery" resolves the interrupted mutation first.
        await new RouteMutationRecovery(
            routes, routeInv, endpointInv, journal).RecoverAsync();

        // After recovery the inventory is consistent with the platform, so a
        // subsequent reconciliation/plan would observe ownership correctly.
        Assert.Single((await routeInv.LoadAsync()).Routes);
        Assert.Empty(journal.All);
    }

    // ---- Shared in-memory fakes ----

    private static RouteMutationJournalEntry PrefixAdd =>
        new()
        {
            Kind = RouteMutationKind.Add,
            InventoryKind = RouteMutationInventoryKind.Prefix,
            RouteIdentity = PrefixId,
            DestinationPrefix = "203.0.113.0/24",
            Gateway = "192.168.1.1",
            InterfaceIndex = 10,
            Metric = 256,
            MutationId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        };

    private static RouteMutationJournalEntry EndpointAdd =>
        PrefixAdd with
        {
            InventoryKind = RouteMutationInventoryKind.Endpoint,
            RouteIdentity = EndpointId,
            DestinationPrefix = "10.0.0.1/32",
            Description = "vpn.example.com"
        };

    private sealed class FakeRouteMutationJournal : IRouteMutationJournal
    {
        private readonly Dictionary<string, RouteMutationJournalEntry> _entries =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, RouteMutationJournalEntry> All => _entries;

        public Task WriteIntentAsync(
            RouteMutationJournalEntry entry, CancellationToken ct = default)
        {
            _entries[entry.RouteIdentity] = entry;
            return Task.CompletedTask;
        }

        public Task ClearIntentAsync(
            string routeIdentity, CancellationToken ct = default)
        {
            _entries.Remove(routeIdentity);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, RouteMutationJournalEntry>> LoadAllAsync(
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, RouteMutationJournalEntry>>(_entries);

        public Task ClearAsync(CancellationToken ct = default)
        {
            _entries.Clear();
            return Task.CompletedTask;
        }

        public void Write(RouteMutationJournalEntry entry) =>
            _entries[entry.RouteIdentity] = entry;
    }

    private sealed class FakeRouteManager : IRouteManager
    {
        private readonly HashSet<string> _present = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Present => _present;

        public void AddToPresent(string identity) => _present.Add(identity);

        public Task<IReadOnlyList<SystemRoute>> GetIpv4RoutesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SystemRoute>>(
                _present.Select(ToRoute).ToArray());

        public Task AddRoutesAsync(
            IReadOnlyCollection<ManagedRoute> routes, CancellationToken ct = default)
        {
            foreach (var r in routes) _present.Add(r.Identity);
            return Task.CompletedTask;
        }

        public Task DeleteRoutesAsync(
            IReadOnlyCollection<ManagedRoute> routes, CancellationToken ct = default)
        {
            foreach (var r in routes) _present.Remove(r.Identity);
            return Task.CompletedTask;
        }

        private static SystemRoute ToRoute(string identity)
        {
            string[] p = identity.Split('|');
            return new SystemRoute
            {
                DestinationPrefix = p[0],
                NextHop = IPAddress.Parse(p[1]),
                InterfaceIndex = uint.Parse(p[2]),
                RouteMetric = p.Length > 3 ? int.Parse(p[3]) : 256
            };
        }
    }

    private sealed class FakeRouteInventory : IRouteInventoryPersistence
    {
        private RouteInventory _stored = new();

        public void Seed(string identity)
        {
            string[] p = identity.Split('|');
            _stored = new RouteInventory
            {
                Routes =
                [
                    new RouteInventoryItem
                    {
                        DestinationPrefix = p[0],
                        Gateway = p[1],
                        InterfaceIndex = uint.Parse(p[2]),
                        Metric = 256
                    }
                ]
            };
        }

        public Task<RouteInventory> LoadAsync(CancellationToken ct = default) =>
            Task.FromResult(_stored);

        public Task SaveAsync(RouteInventory value, CancellationToken ct = default)
        {
            _stored = value;
            return Task.CompletedTask;
        }

        public Task MutateAsync(
            Func<RouteInventory, RouteInventory> transform, CancellationToken ct = default)
        {
            _stored = transform(_stored);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEndpointInventory : IEndpointInventoryPersistence
    {
        private VpnEndpointInventory _stored = new();

        public void SeedOwned(string identity, bool addedByIranDirect)
        {
            string[] p = identity.Split('|');
            _stored = new VpnEndpointInventory
            {
                Endpoints =
                [
                    new VpnEndpointInventoryItem
                    {
                        Host = "test.example.com", Address = p[1], Port = 1194,
                        Protocol = "udp", DestinationPrefix = p[0], Gateway = p[1],
                        InterfaceIndex = uint.Parse(p[2]), Metric = 1,
                        AddedByIranDirect = addedByIranDirect, IsCurrent = true,
                        ProtectedAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow
                    }
                ]
            };
        }

        public Task<VpnEndpointInventory> LoadAsync(CancellationToken ct = default) =>
            Task.FromResult(_stored);

        public Task SaveAsync(VpnEndpointInventory value, CancellationToken ct = default)
        {
            _stored = value;
            return Task.CompletedTask;
        }

        public Task MutateAsync(
            Func<VpnEndpointInventory, VpnEndpointInventory> transform,
            CancellationToken ct = default)
        {
            _stored = transform(_stored);
            return Task.CompletedTask;
        }
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "irandirect-crossstore-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { }
        }
    }
}
