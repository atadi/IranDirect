namespace PathVeer.Core.Tests.Runtime.Execution;

using System.Net;
using System.Text;
using PathVeer.Core.Routing;
using PathVeer.Core.Vpn;
using PathVeer.Core.Runtime.Execution;
using Xunit;

public sealed class RouteMutationRecoveryTests
{
    private const string PrefixId = "203.0.113.0/24|192.168.1.1|10";
    private const string EndpointId = "10.0.0.1/32|192.168.1.1|10";

    private static RouteMutationJournalEntry PrefixAdd =>
        new()
        {
            Kind = RouteMutationKind.Add,
            InventoryKind = RouteMutationInventoryKind.Prefix,
            RouteIdentity = PrefixId,
            DestinationPrefix = "203.0.113.0/24",
            Gateway = "192.168.1.1",
            InterfaceIndex = 10,
            Metric = 5,
            MutationId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        };

    private static RouteMutationJournalEntry PrefixDelete =>
        PrefixAdd with { Kind = RouteMutationKind.Delete };

    private static RouteMutationJournalEntry EndpointAdd =>
        PrefixAdd with
        {
            InventoryKind = RouteMutationInventoryKind.Endpoint,
            RouteIdentity = EndpointId,
            DestinationPrefix = "10.0.0.1/32",
            Description = "vpn.example.com"
        };

    private static RouteMutationJournalEntry EndpointDelete =>
        EndpointAdd with { Kind = RouteMutationKind.Delete };

    [Fact]
    public async Task NoJournal_LeavesStateUnchanged()
    {
        var routes = new FakeRouteManager();
        var routeInv = new FakeRouteInventory();
        var endpointInv = new FakeEndpointInventory();
        var journal = new FakeRouteMutationJournal();

        await new RouteMutationRecovery(
            routes, routeInv, endpointInv, journal).RecoverAsync();

        Assert.Empty(routes.Present);
        Assert.Empty((await routeInv.LoadAsync()).Routes);
        Assert.Empty((await endpointInv.LoadAsync()).Endpoints);
        Assert.Empty(journal.All);
    }

    // ----- ADD crash matrix -----

    [Fact]
    public async Task Add_AfterJournalWrite_BeforeNative_CrashState_NativePresent_InventoryMissing_Adopts()
    {
        var routes = new FakeRouteManager();
        routes.AddToPresent(PrefixId);
        var routeInv = new FakeRouteInventory();
        var endpointInv = new FakeEndpointInventory();
        var journal = new FakeRouteMutationJournal();
        journal.Write(PrefixAdd); // simulates crash after journal, before/at native

        await new RouteMutationRecovery(
            routes, routeInv, endpointInv, journal).RecoverAsync();

        RouteInventory inv = await routeInv.LoadAsync();
        Assert.Single(inv.Routes);
        Assert.Equal(PrefixId, inv.Routes[0].Identity);
        Assert.Contains("192.168.1.1", inv.Routes[0].Identity);
        Assert.Empty(journal.All); // cleared after adopt
    }

    [Fact]
    public async Task Add_AfterJournalWrite_NativeAbsent_Stale_ClearedWithoutOwnership()
    {
        var routes = new FakeRouteManager();
        var routeInv = new FakeRouteInventory();
        var endpointInv = new FakeEndpointInventory();
        var journal = new FakeRouteMutationJournal();
        journal.Write(PrefixAdd);

        await new RouteMutationRecovery(
            routes, routeInv, endpointInv, journal).RecoverAsync();

        Assert.Empty((await routeInv.LoadAsync()).Routes);
        Assert.Empty(journal.All);
    }

    [Fact]
    public async Task Add_InventoryAlreadyHas_Committed_Cleared()
    {
        var routes = new FakeRouteManager();
        routes.AddToPresent(PrefixId);
        var routeInv = new FakeRouteInventory();
        routeInv.Seed(PrefixId);
        var endpointInv = new FakeEndpointInventory();
        var journal = new FakeRouteMutationJournal();
        journal.Write(PrefixAdd);

        await new RouteMutationRecovery(
            routes, routeInv, endpointInv, journal).RecoverAsync();

        Assert.Single((await routeInv.LoadAsync()).Routes);
        Assert.Empty(journal.All);
    }

    // ----- DELETE crash matrix -----

    [Fact]
    public async Task Delete_NativeAbsent_InventoryClaimsOwnership_CompletesRemoval()
    {
        var routes = new FakeRouteManager(); // native absent
        var routeInv = new FakeRouteInventory();
        routeInv.Seed(PrefixId);
        var endpointInv = new FakeEndpointInventory();
        var journal = new FakeRouteMutationJournal();
        journal.Write(PrefixDelete);

        await new RouteMutationRecovery(
            routes, routeInv, endpointInv, journal).RecoverAsync();

        Assert.Empty((await routeInv.LoadAsync()).Routes);
        Assert.Empty(journal.All);
    }

    [Fact]
    public async Task Delete_NativeStillPresent_Owned_Defers_ClearsJournal()
    {
        var routes = new FakeRouteManager();
        routes.AddToPresent(PrefixId);
        var routeInv = new FakeRouteInventory();
        routeInv.Seed(PrefixId);
        var endpointInv = new FakeEndpointInventory();
        var journal = new FakeRouteMutationJournal();
        journal.Write(PrefixDelete);

        await new RouteMutationRecovery(
            routes, routeInv, endpointInv, journal).RecoverAsync();

        // Ownership preserved; reconciliation will retry the native delete.
        Assert.Single((await routeInv.LoadAsync()).Routes);
        Assert.Empty(journal.All);
    }

    [Fact]
    public async Task Delete_InventoryAlreadyClean_Committed_Cleared()
    {
        var routes = new FakeRouteManager(); // native absent
        var routeInv = new FakeRouteInventory();
        var endpointInv = new FakeEndpointInventory();
        var journal = new FakeRouteMutationJournal();
        journal.Write(PrefixDelete);

        await new RouteMutationRecovery(
            routes, routeInv, endpointInv, journal).RecoverAsync();

        Assert.Empty((await routeInv.LoadAsync()).Routes);
        Assert.Empty(journal.All);
    }

    // ----- Endpoint variants -----

    [Fact]
    public async Task EndpointAdd_NativePresent_InventoryMissing_AdoptsIntoEndpointInventory()
    {
        var routes = new FakeRouteManager();
        routes.AddToPresent(EndpointId);
        var routeInv = new FakeRouteInventory();
        var endpointInv = new FakeEndpointInventory();
        var journal = new FakeRouteMutationJournal();
        journal.Write(EndpointAdd);

        await new RouteMutationRecovery(
            routes, routeInv, endpointInv, journal).RecoverAsync();

        VpnEndpointInventory inv = await endpointInv.LoadAsync();
        Assert.Single(inv.Endpoints);
        Assert.Equal(EndpointId, inv.Endpoints[0].Identity);
        Assert.True(inv.Endpoints[0].AddedByIranDirect);
        Assert.Empty(journal.All);
    }

    [Fact]
    public async Task EndpointDelete_NativeAbsent_Owned_CompletesRemoval()
    {
        var routes = new FakeRouteManager();
        var routeInv = new FakeRouteInventory();
        var endpointInv = new FakeEndpointInventory();
        endpointInv.SeedOwned(EndpointId, addedByIranDirect: true);
        var journal = new FakeRouteMutationJournal();
        journal.Write(EndpointDelete);

        await new RouteMutationRecovery(
            routes, routeInv, endpointInv, journal).RecoverAsync();

        Assert.Empty((await endpointInv.LoadAsync()).Endpoints);
        Assert.Empty(journal.All);
    }

    // ----- External-route safety (no journal) -----

    [Fact]
    public async Task ExternalRoute_IdenticalToDesired_NoJournal_NotAdoptedOrDeleted()
    {
        // An externally-created route that happens to match a desired identity.
        var routes = new FakeRouteManager();
        routes.AddToPresent(PrefixId);
        var routeInv = new FakeRouteInventory(); // IranDirect does NOT own it
        var endpointInv = new FakeEndpointInventory();
        var journal = new FakeRouteMutationJournal(); // empty: no proof

        await new RouteMutationRecovery(
            routes, routeInv, endpointInv, journal).RecoverAsync();

        Assert.Empty((await routeInv.LoadAsync()).Routes);
        Assert.Contains(PrefixId, routes.Present); // left untouched
        Assert.Empty(journal.All);
    }

    [Fact]
    public async Task ExternalRoute_IdenticalExceptMetric_NoJournal_Untouched()
    {
        var routes = new FakeRouteManager();
        routes.AddToPresent("203.0.113.0/24|192.168.1.1|10|999"); // different metric
        var routeInv = new FakeRouteInventory();
        var endpointInv = new FakeEndpointInventory();
        var journal = new FakeRouteMutationJournal();

        await new RouteMutationRecovery(
            routes, routeInv, endpointInv, journal).RecoverAsync();

        Assert.Empty((await routeInv.LoadAsync()).Routes);
        Assert.Single(routes.Present);
    }

    [Fact]
    public async Task ExternalRoute_IdenticalExceptGateway_NoJournal_Untouched()
    {
        var routes = new FakeRouteManager();
        routes.AddToPresent("203.0.113.0/24|192.168.1.2|10"); // different gateway
        var routeInv = new FakeRouteInventory();
        var endpointInv = new FakeEndpointInventory();
        var journal = new FakeRouteMutationJournal();

        await new RouteMutationRecovery(
            routes, routeInv, endpointInv, journal).RecoverAsync();

        Assert.Empty((await routeInv.LoadAsync()).Routes);
        Assert.Single(routes.Present);
    }

    [Fact]
    public async Task ExternallyRecreatedRoute_AfterInventoryLoss_NoJournal_NotClaimed()
    {
        // Route was IranDirect-owned, then inventory was lost (reset) and the
        // route was recreated externally. Without a journal, IranDirect must
        // NOT claim ownership of it.
        var routes = new FakeRouteManager();
        routes.AddToPresent(PrefixId);
        var routeInv = new FakeRouteInventory(); // empty = "lost" inventory
        var endpointInv = new FakeEndpointInventory();
        var journal = new FakeRouteMutationJournal();

        await new RouteMutationRecovery(
            routes, routeInv, endpointInv, journal).RecoverAsync();

        Assert.Empty((await routeInv.LoadAsync()).Routes);
        Assert.Contains(PrefixId, routes.Present);
    }

    // ----- Idempotency -----

    [Fact]
    public async Task Recovery_RunTwice_ConvergesIdentically()
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
        Assert.Single((await routeInv.LoadAsync()).Routes);

        // Second run: journal is now empty; state stable.
        await recovery.RecoverAsync();
        Assert.Single((await routeInv.LoadAsync()).Routes);
        Assert.Empty(journal.All);
    }

    // ----- Corrupt journal -----

    [Fact]
    public async Task CorruptJournal_DoesNotMutateRoutes_Quarantines()
    {
        using TempDir dir = new();
        string path = dir.File("j.json");
        await File.WriteAllTextAsync(path, "not json at all");

        var store = new RouteMutationJournalStore(path);
        var routes = new FakeRouteManager();
        routes.AddToPresent(PrefixId);
        var routeInv = new FakeRouteInventory();
        var endpointInv = new FakeEndpointInventory();

        await new RouteMutationRecovery(
            routes, routeInv, endpointInv, store).RecoverAsync();

        // No ownership claimed; corrupt file quarantined to .corrupt.
        Assert.Empty((await routeInv.LoadAsync()).Routes);
        Assert.Contains(PrefixId, routes.Present);
        Assert.True(File.Exists(path + ".corrupt"));
        Assert.False(File.Exists(path));
    }

    // Audit final closure: a PRESENT zero-byte journal is truncation and must
    // stop recovery exactly like malformed JSON — no routes mutated, file
    // quarantined so operators can inspect the bytes.
    [Fact]
    public async Task ZeroByteJournal_StopsRecoveryWithoutMutation_Quarantines()
    {
        using TempDir dir = new();
        string path = dir.File("j.json");
        await File.WriteAllBytesAsync(path, []);

        var store = new RouteMutationJournalStore(path);
        var routes = new FakeRouteManager();
        routes.AddToPresent(PrefixId);
        var routeInv = new FakeRouteInventory();
        var endpointInv = new FakeEndpointInventory();

        await new RouteMutationRecovery(
            routes, routeInv, endpointInv, store).RecoverAsync();

        Assert.Empty((await routeInv.LoadAsync()).Routes);
        Assert.Contains(PrefixId, routes.Present); // untouched
        Assert.True(File.Exists(path + ".corrupt"));
        Assert.False(File.Exists(path));
    }

    // A present whitespace-only journal is corruption, not "no pending
    // mutation"; recovery must stop and quarantine rather than adopt/delete
    // ownership from native route shape.
    [Fact]
    public async Task WhitespaceOnlyJournal_StopsRecoveryWithoutMutation_Quarantines()
    {
        using TempDir dir = new();
        string path = dir.File("j.json");
        await File.WriteAllTextAsync(path, "   \n\t  ");

        var store = new RouteMutationJournalStore(path);
        var routes = new FakeRouteManager();
        routes.AddToPresent(PrefixId);
        var routeInv = new FakeRouteInventory();
        var endpointInv = new FakeEndpointInventory();

        await new RouteMutationRecovery(
            routes, routeInv, endpointInv, store).RecoverAsync();

        Assert.Empty((await routeInv.LoadAsync()).Routes);
        Assert.Contains(PrefixId, routes.Present);
        Assert.True(File.Exists(path + ".corrupt"));
        Assert.False(File.Exists(path));
    }

    // Audit final UTF-8 closure at the recovery boundary: a journal whose raw
    // bytes contain invalid UTF-8 INSIDE a JSON string must surface
    // RouteMutationJournalCorruptException from the store, be caught by recovery,
    // stop all route/inventory mutation, and be quarantined (not normalized away).
    [Fact]
    public async Task InvalidUtf8Journal_StopsRecoveryWithoutMutation_Quarantines()
    {
        using TempDir dir = new();
        string path = dir.File("j.json");

        string json =
            "{\n" +
            "  \"SchemaVersion\": 1,\n" +
            "  \"Entries\": {\n" +
            "    \"pfx\": {\n" +
            "      \"Kind\": 0,\n" +
            "      \"InventoryKind\": 0,\n" +
            "      \"RouteIdentity\": \"pfx\",\n" +
            "      \"DestinationPrefix\": \"10.0.0.0/8\",\n" +
            "      \"Gateway\": \"192.168.1.1\",\n" +
            "      \"InterfaceIndex\": 12,\n" +
            "      \"Metric\": 100,\n" +
            "      \"Description\": \"MARKER\"\n" +
            "    }\n" +
            "  }\n" +
            "}";

        byte[] validBytes = Encoding.UTF8.GetBytes(json);
        byte[] marker = Encoding.UTF8.GetBytes("MARKER");
        int idx = validBytes.AsSpan().IndexOf(marker);
        byte[] corruptBytes = validBytes.ToArray();
        corruptBytes[idx] = 0xC3;      // invalid: expects a continuation byte
        corruptBytes[idx + 1] = 0x28;  // 0x28 is not a continuation byte
        await File.WriteAllBytesAsync(path, corruptBytes);

        var store = new RouteMutationJournalStore(path);
        var routes = new FakeRouteManager();
        routes.AddToPresent(PrefixId);
        var routeInv = new FakeRouteInventory();
        var endpointInv = new FakeEndpointInventory();

        await new RouteMutationRecovery(
            routes, routeInv, endpointInv, store).RecoverAsync();

        // No ownership claimed; corrupt file quarantined to .corrupt.
        Assert.Empty((await routeInv.LoadAsync()).Routes);
        Assert.Contains(PrefixId, routes.Present); // untouched native route
        Assert.True(File.Exists(path + ".corrupt"));
        Assert.False(File.Exists(path));
    }

    // ----- Shared in-memory fakes -----

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

        public void Write(RouteMutationJournalEntry entry) => _entries[entry.RouteIdentity] = entry;
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
            foreach (var r in routes)
                _present.Add(r.Identity);
            return Task.CompletedTask;
        }

        public Task DeleteRoutesAsync(
            IReadOnlyCollection<ManagedRoute> routes, CancellationToken ct = default)
        {
            foreach (var r in routes)
                _present.Remove(r.Identity);
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
                "irandirect-recovery-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public string File(string name) =>
            System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { }
        }
    }
}
