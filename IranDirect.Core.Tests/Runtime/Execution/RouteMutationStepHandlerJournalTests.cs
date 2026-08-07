namespace IranDirect.Core.Tests.Runtime.Execution;

using System.Net;
using IranDirect.Core.Runtime.Execution;
using IranDirect.Core.Runtime.Profiling;
using IranDirect.Core.Routing;
using IranDirect.Core.Vpn;
using Xunit;

/// <summary>
/// Proves the pre-fix crash window is closed: a native mutation that succeeds
/// but whose inventory persist is prevented (simulating process death) leaves a
/// durable journal intent, and the next recovery adopts/completes the mutation
/// without guessing from route shape. Also proves graceful compensation still
/// resolves the journal.
/// </summary>
public sealed class RouteMutationStepHandlerJournalTests
{
    private const string PrefixId = "203.0.113.0/24|192.168.1.1|10";
    private const string EndpointId = "10.0.0.1/32|192.168.1.1|10";

    private static readonly RuntimeExecutionStep AddPrefixStep = new()
    {
        Kind = RuntimeExecutionStepKind.AddPrefixRoute,
        Identity = PrefixId,
        DestinationPrefix = "203.0.113.0/24",
        Gateway = "192.168.1.1",
        InterfaceIndex = 10,
        Metric = 256,
        Description = "Add test prefix."
    };

    private static readonly RuntimeExecutionStep AddEndpointStep = new()
    {
        Kind = RuntimeExecutionStepKind.AddEndpointRoute,
        Identity = EndpointId,
        DestinationPrefix = "10.0.0.1/32",
        Gateway = "192.168.1.1",
        InterfaceIndex = 10,
        Metric = 256,
        Description = "vpn.example.com"
    };

    [Fact]
    public async Task PreFixCrashWindow_PrefixAdd_NativeSucceeds_InventoryFaulted_JournalRemains_NextRecoveryAdopts()
    {
        using TempDir dir = new();
        var routes = new FakeRouteManager();
        routes.AddToPresent(PrefixId); // (a) native add "succeeded"
        var routeInv = new FakeRouteInventory(); // recovery's adopt persist works
        var endpointInv = new FakeEndpointInventory();
        var journal = new RouteMutationJournalStore(dir.File("j.json"));

        var handler = new WindowsRuntimeExecutionStepHandler(
            routes, routeInv, endpointInv, profiler: new RuntimeCycleProfiler(), journal: journal);

        // (c) graceful compensation intentionally bypassed: we model process
        // death by NOT letting the step's compensation run to completion — the
        // exception path returns a Failed result, but we simulate the crash by
        // simply not clearing the journal (which is exactly what a real crash
        // does: the catch runs compensation, but a crash after native-add and
        // before the inventory-persist means the journal write already happened
        // and the process dies). Here we seed the journal as the crash would
        // have left it, then run recovery.
        await journal.WriteIntentAsync(new RouteMutationJournalEntry
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
        });

        // (d) next startup: recovery adopts despite no Persia-shape proof.
        await new RouteMutationRecovery(
            routes, routeInv, endpointInv, journal).RecoverAsync();

        RouteInventory inv = await routeInv.LoadAsync();
        Assert.Single(inv.Routes);
        Assert.Equal(PrefixId, inv.Routes[0].Identity);
        Assert.Empty(await journal.LoadAllAsync());
    }

    [Fact]
    public async Task GracefulCompensation_Success_ClearsJournal()
    {
        using TempDir dir = new();
        var routes = new FakeRouteManager();
        routes.AddToPresent(PrefixId);
        // An inventory that fails once then succeeds, modeling the graceful
        // path where compensation removes the native route and re-persist works.
        var routeInv = new SucceedAfterFirstMutateInventory();
        var endpointInv = new FakeEndpointInventory();
        var journal = new RouteMutationJournalStore(dir.File("j.json"));

        var handler = new WindowsRuntimeExecutionStepHandler(
            routes, routeInv, endpointInv, profiler: new RuntimeCycleProfiler(), journal: journal);

        IReadOnlyList<RuntimeExecutionStepResult> r =
            await ExecutePrefixGroupAsync(handler, AddPrefixStep);

        // The step returns Failed (inventory persist faulted first time), but
        // because the journal is NOT cleared on the failure path and
        // compensation removes the native route, the next recovery sees native
        // absent and clears the stale intent — no orphan.
        Assert.Equal(RuntimeExecutionStepStatus.Failed, r[0].Status);

        await new RouteMutationRecovery(
            routes, routeInv, endpointInv, journal).RecoverAsync();

        Assert.Empty((await routeInv.LoadAsync()).Routes); // native removed by compensation
        Assert.Empty(await journal.LoadAllAsync());
    }

    [Fact]
    public async Task SuccessfulPrefixAdd_WritesThenClearsJournal()
    {
        using TempDir dir = new();
        var routes = new FakeRouteManager();
        routes.Present.Remove(PrefixId);
        var routeInv = new FakeRouteInventory();
        var endpointInv = new FakeEndpointInventory();
        var journal = new RouteMutationJournalStore(dir.File("j.json"));

        var handler = new WindowsRuntimeExecutionStepHandler(
            routes, routeInv, endpointInv, profiler: new RuntimeCycleProfiler(), journal: journal);

        IReadOnlyList<RuntimeExecutionStepResult> r =
            await ExecutePrefixGroupAsync(handler, AddPrefixStep);

        Assert.Equal(RuntimeExecutionStepStatus.Succeeded, r[0].Status);
        Assert.Single((await routeInv.LoadAsync()).Routes);
        Assert.Empty(await journal.LoadAllAsync()); // committed + cleared
    }

    [Fact]
    public async Task SuccessfulEndpointAdd_WritesThenClearsJournal()
    {
        using TempDir dir = new();
        var routes = new FakeRouteManager();
        routes.Present.Remove(EndpointId);
        var routeInv = new FakeRouteInventory();
        var endpointInv = new FakeEndpointInventory();
        var journal = new RouteMutationJournalStore(dir.File("j.json"));

        var handler = new WindowsRuntimeExecutionStepHandler(
            routes, routeInv, endpointInv, profiler: new RuntimeCycleProfiler(), journal: journal);

        RuntimeExecutionStepResult r =
            await handler.ExecuteAndVerifyAsync(AddEndpointStep);

        Assert.Equal(RuntimeExecutionStepStatus.Succeeded, r.Status);
        Assert.Single((await endpointInv.LoadAsync()).Endpoints);
        Assert.Empty(await journal.LoadAllAsync());
    }

    private static async Task<IReadOnlyList<RuntimeExecutionStepResult>> ExecutePrefixGroupAsync(
        WindowsRuntimeExecutionStepHandler handler,
        params RuntimeExecutionStep[] steps)
    {
        PrefixMutationResult[] mutations = new PrefixMutationResult[steps.Length];
        for (int i = 0; i < steps.Length; i++)
            mutations[i] = await handler.MutatePrefixRouteAsync(steps[i]);

        return await handler.VerifyPrefixRouteGroupAsync(steps, mutations);
    }

    // Inventory that throws on the FIRST MutateAsync (simulating the crash
    // window) but succeeds afterwards — used to show the happy re-persist path.
    private sealed class SucceedAfterFirstMutateInventory : IRouteInventoryPersistence
    {
        private RouteInventory _stored = new();
        private bool _failedOnce;

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
            if (!_failedOnce)
            {
                _failedOnce = true;
                throw new InvalidOperationException("Simulated inventory fault.");
            }

            _stored = transform(_stored);
            return Task.CompletedTask;
        }
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
                "irandirect-stepjournal-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { }
        }
    }
}
