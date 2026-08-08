using PathVeer.Core.State;
using PathVeer.Service;
using PathVeer.Service.Observability;

// Phase 36.3: resolve the authoritative PathVeer state root and migrate the
// legacy %ProgramData%\IranDirect root (copy/verify/publish) when required.
// Migration runs before any store is constructed so the composition root only
// ever sees the single authoritative PathVeer root.
StateRootResolver stateRootResolver = new();
StateRootMigrator stateRootMigrator = new(stateRootResolver);
stateRootMigrator.EnsureCurrentRoot();

string dataDirectory = stateRootResolver.CurrentRoot;
Directory.CreateDirectory(dataDirectory);

HostApplicationBuilder builder =
    Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "PathVeer Service";
});

// The application dependency graph lives in ServiceCompositionRoot so that the
// Service composition tests exercise the exact registrations the host uses.
builder.Services.AddPathVeerServiceComposition(
    builder.Configuration,
    dataDirectory);

builder.Services.AddIranDirectObservability(
    builder.Configuration,
    builder.Environment.EnvironmentName);

IHost host = builder.Build();

await host.RunAsync();
