using IranDirect.Core;
using IranDirect.Core.Configuration;
using IranDirect.Core.CustomRoutes;
using IranDirect.Core.Diagnostics;
using IranDirect.Core.Diagnostics.Configuration;
using IranDirect.Core.Diagnostics.Runtime;
using IranDirect.Core.Ipc;
using IranDirect.Core.Networking;
using IranDirect.Core.Observability;
using IranDirect.Core.Planning;
using IranDirect.Core.Prefixes;
using IranDirect.Core.Routing;
using IranDirect.Core.Runtime;
using IranDirect.Core.Runtime.Execution;
using IranDirect.Core.Runtime.Profiling;
using IranDirect.Core.Runtime.Reconciliation;
using IranDirect.Core.State;
using IranDirect.Core.Support;
using IranDirect.Core.SystemTools;
using IranDirect.Core.Testing.FaultInjection;
using IranDirect.Core.Vpn;

namespace IranDirect.Testing.Performance.Lifecycle;

/// <summary>
/// Wires the production orchestration stack (controller, cycle
/// coordinator, decision builder, reconciler, executor, snapshot and
/// support providers, diagnostics, prefix monitor, custom-route DNS
/// stack) against deterministic in-memory and temp-directory fakes.
/// No real network, DNS, Windows service, route table, or user-profile
/// file is touched.
/// </summary>
public sealed class SimulatedRuntimeEnvironment : IDisposable
{
    public SimulationTimeProvider Time { get; }

    public TemporaryLifecycleWorkspace Workspace { get; }

    public SimulatedRouteTable RouteTable { get; }

    public ObservationState Observation { get; }

    public FakeObservationSource ObservationSource { get; }

    public FakeWindowsRouteApi RouteApi { get; }

    public ScriptedPrefixSource PrefixSource { get; }

    public ScriptedPrefixUpdateChecker PrefixUpdateChecker { get; }

    public CountingPrefixUpdateChecker CountingPrefixUpdateChecker { get; }

    public ScriptedDnsResolver DnsResolver { get; }

    public IFaultInjectionPolicy FaultPolicy { get; set; }

    /// <summary>
    /// Deterministic gate seam used by cancellation tests: when set,
    /// every execution-handler entry awaits this delegate before
    /// touching the production step handler. Tests complete the
    /// returned task to release the gate. Defaults to null (no gate).
    /// </summary>
    public Func<Task>? ExecutionHold { get; set; }

    public IRouteManager RouteManager { get; }

    public VpnEndpointRouteManager VpnEndpointRouteManager { get; }

    public StateRepository StateRepository { get; }

    public RouteInventoryStore RouteInventoryStore { get; }

    public VpnEndpointInventoryStore VpnEndpointInventoryStore { get; }

    public CountryPrefixStore PrefixStore { get; }

    public DirectCountryCode Country { get; } = DirectCountryCode.IR;

    public DesiredConfigurationService ConfigurationService { get; }

    public RuntimeOperationStatus OperationStatus { get; }

    public RuntimeCycleProfiler Profiler { get; }

    public RuntimePerfReportStore? PerfReportStore { get; }

    public RuntimeCycleCoordinator CycleCoordinator { get; }

    public IRuntimeExecutor Executor { get; }

    public CountingExecutionHandlerDecorator ExecutionHandler { get; }

    public IranDirectController Controller { get; }

    public RuntimeSnapshotProvider SnapshotProvider { get; }

    public DiagnosticRunner DiagnosticRunner { get; }

    public RuntimePreviewPlanner PreviewPlanner { get; }

    public SupportSnapshotProvider SupportSnapshotProvider { get; }

    public SupportSnapshotExporter SupportSnapshotExporter { get; }

    public SupportSnapshotSerializer SupportSnapshotSerializer { get; }

    public SupportBundleExporter SupportBundleExporter { get; }

    public PrefixUpdateMonitor Monitor { get; }

    public CustomRouteService CustomRouteService { get; }

    public CustomRouteDnsCacheService DnsCacheService { get; }

    public CustomRouteResolver CustomRouteResolver { get; }

    public PrefixSourceMetadataService PrefixMetadataService { get; }

    public PrefixSourceUpdateHistoryService PrefixHistoryService { get; }

    public string VpnProfilePath { get; }

    public string CustomRoutesPath { get; }

    public string DnsCachePath { get; }

    public SimulatedRuntimeEnvironment(
        IFaultInjectionPolicy? faultPolicy = null,
        DateTimeOffset? startTime = null,
        bool enablePerfReports = true)
    {
        FaultPolicy = faultPolicy ?? FaultInjectionPolicy.Never;

        Time = new SimulationTimeProvider(
            startTime ?? new DateTimeOffset(
                2026, 8, 3, 0, 0, 0, TimeSpan.Zero));

        Workspace = new TemporaryLifecycleWorkspace("simulation");
        Workspace.EnsureDirectories();

        string root = Workspace.PathValue;
        string statePath = Path.Combine(root, "state.json");
        string configurationPath = Path.Combine(root, "configuration.json");
        string routeInventoryPath = Path.Combine(root, "route-inventory.json");
        string vpnEndpointInventoryPath = Path.Combine(root, "vpn-endpoint-inventory.json");
        string prefixFilePath = Path.Combine(root, "prefixes.txt");
        string prefixMetadataPath = Path.Combine(root, "prefix-metadata.json");
        string prefixHistoryPath = Path.Combine(root, "prefix-history.json");
        CustomRoutesPath = Path.Combine(root, "custom-routes.json");
        DnsCachePath = Path.Combine(root, "custom-route-dns.json");
        VpnProfilePath = Path.Combine(root, "vpn-profile.ovpn");
        string perfDirectory = Workspace.PerfDirectory;

        RouteTable = new SimulatedRouteTable();
        RouteApi = new FakeWindowsRouteApi(RouteTable);
        Observation = new ObservationState();
        ObservationSource = new FakeObservationSource(Observation);
        PrefixSource = new ScriptedPrefixSource(FaultPolicy);
        PrefixUpdateChecker = new ScriptedPrefixUpdateChecker(Time);
        CountingPrefixUpdateChecker = new CountingPrefixUpdateChecker(
            PrefixUpdateChecker);
        DnsResolver = new ScriptedDnsResolver();

        RouteManager = new WindowsRouteManager(
            new CommandRunner(),
            RouteApi,
            FaultPolicy);

        StateRepository = new StateRepository(statePath);
        RouteInventoryStore = new RouteInventoryStore(routeInventoryPath);
        VpnEndpointInventoryStore = new VpnEndpointInventoryStore(
            vpnEndpointInventoryPath);
        PrefixStore = new CountryPrefixStore(root);

        DesiredConfigurationValidator configurationValidator = new();
        DesiredConfigurationStore configurationStore = new(
            configurationPath,
            configurationValidator);
        ConfigurationService = new DesiredConfigurationService(
            configurationStore);

        PrefixMetadataService = new PrefixSourceMetadataService(
            PrefixStore,
            Time);

        PrefixHistoryService = new PrefixSourceUpdateHistoryService(
            PrefixStore,
            timeProvider: Time);

        DesiredConfiguration defaults = new()
        {
            Enabled = false,
            VpnProvider = VpnProviderType.OpenVpn,
            VpnProfilePath = VpnProfilePath,
            AutoRepair = true,
            RepairInterval = TimeSpan.FromSeconds(30),
            AutoUpdatePrefixes = true,
            PrefixUpdateInterval = TimeSpan.FromDays(1)
        };
        configurationStore.SaveAsync(defaults).GetAwaiter().GetResult();

        Profiler = enablePerfReports
            ? new RuntimeCycleProfiler(
                enabled: true,
                store: new RuntimePerfReportStore(perfDirectory))
            : RuntimeCycleProfiler.Noop;

        PerfReportStore = Profiler.Enabled
            ? new RuntimePerfReportStore(perfDirectory)
            : null;

        RuntimePlanner planner = new(configurationValidator);
        RuntimeObserver observer = new(ObservationSource);
        RuntimeCoordinator planCoordinator = new(
            ConfigurationService,
            observer,
            planner);

        RuntimeRouteOwnershipProvider ownershipProvider = new(
            new InventoryRouteOwnershipSource(
                RouteInventoryStore,
                VpnEndpointInventoryStore));
        RuntimeReconciler reconciler = new(
            ownershipProvider,
            new RuntimeChangeSetPlanner());
        RuntimeExecutionPlanner executionPlanner = new();

        RuntimeDecisionBuilder decisionBuilder = new(
            planCoordinator,
            reconciler,
            executionPlanner,
            Time);

        CycleCoordinator = new RuntimeCycleCoordinator(
            decisionBuilder,
            Profiler);

        VpnEndpointRouteManager = new VpnEndpointRouteManager(
            RouteManager);

        WindowsRuntimeExecutionStepHandler stepHandler = new(
            RouteManager,
            RouteInventoryStore,
            VpnEndpointInventoryStore,
            Profiler);

        ExecutionHandler = new CountingExecutionHandlerDecorator(
            stepHandler,
            () => ExecutionHold?.Invoke() ?? Task.CompletedTask);

        Executor = new RuntimeExecutor(ExecutionHandler);

        OperationStatus = new RuntimeOperationStatus();

        OpenVpnEndpointProvider vpnEndpointProvider = new(
            VpnProfilePath,
            new OpenVpnProfileParser(),
            new VpnEndpointResolver());

        Controller = new IranDirectController(
            PrefixSource,
            PrefixStore,
            new GatewayDetector(),
            RouteManager,
            StateRepository,
            RouteInventoryStore,
            vpnEndpointProvider,
            VpnEndpointRouteManager,
            VpnEndpointInventoryStore,
            CycleCoordinator,
            Executor,
            ConfigurationService,
            OperationStatus,
            Profiler,
            PrefixMetadataService,
            PrefixHistoryService);

        Monitor = new PrefixUpdateMonitor(
            CountingPrefixUpdateChecker,
            new PrefixUpdateMonitorOptions
            {
                Interval = TimeSpan.FromHours(12)
            },
            Time);

        CustomRouteStore customRouteStore = new(CustomRoutesPath);
        CustomRouteRepository customRouteRepository = new(customRouteStore);
        CustomRouteService = new CustomRouteService(
            customRouteRepository,
            new CustomRouteEntryValidator(),
            Time);

        CustomRouteDnsCacheStore dnsCacheStore = new(DnsCachePath);
        CustomRouteDnsCacheRepository dnsCacheRepository = new(
            dnsCacheStore,
            Time);
        DnsCacheService = new CustomRouteDnsCacheService(
            CustomRouteService,
            dnsCacheRepository,
            Time);

        CustomRouteResolver = new CustomRouteResolver(
            customRouteRepository,
            dnsCacheRepository,
            new CustomRouteDnsCacheOptions(),
            Time,
            DnsResolver.ResolveAsync,
            FaultPolicy);

        PreviewPlanner = new RuntimePreviewPlanner(
            decisionBuilder,
            new ExecutionPreviewBuilder(Time));

        IReadOnlyList<IDiagnosticCheck> checks =
        [
            new RuntimeOperationDiagnosticCheck(OperationStatus),
            new RuntimeStateDiagnosticCheck(Controller),
            new DesiredConfigurationDiagnosticCheck(ConfigurationService),
            new PrefixConfigurationDiagnosticCheck(
                PrefixStore,
                () => Country)
        ];

        DiagnosticRunner = new DiagnosticRunner(
            checks,
            FaultPolicy);

        SnapshotProvider = new RuntimeSnapshotProvider(
            Controller,
            ConfigurationService,
            RouteInventoryStore,
            DnsCacheService,
            PerfReportStore,
            PrefixMetadataService,
            PrefixUpdateChecker,
            Time,
            Monitor,
            FaultPolicy);

        SupportSnapshotProvider = new SupportSnapshotProvider(
            SnapshotProvider,
            DiagnosticRunner,
            PreviewPlanner,
            ConfigurationService,
            PrefixMetadataService,
            PrefixHistoryService,
            DnsCacheService,
            PerfReportStore,
            Time);

        SupportSnapshotSerializer = new SupportSnapshotSerializer();
        SupportSnapshotExporter = new SupportSnapshotExporter(
            SupportSnapshotProvider,
            SupportSnapshotSerializer,
            Time);
        SupportBundleExporter = new SupportBundleExporter(
            SupportSnapshotExporter,
            Time);
    }

    public Task<RuntimeCycleExecutionResult> RunCycleAsync(
        CancellationToken cancellationToken = default) =>
        Controller.RunCycleAsync(cancellationToken);

    public Task<RuntimeCycleExecutionResult> EnableAsync(
        CancellationToken cancellationToken = default) =>
        Controller.EnableAsync(cancellationToken);

    public Task<RuntimeCycleExecutionResult> DisableAsync(
        CancellationToken cancellationToken = default) =>
        Controller.DisableAsync(cancellationToken);

    public void Dispose()
    {
        Monitor.Dispose();
        Workspace.Dispose();
    }
}
