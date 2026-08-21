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

// Harden the dedicated Cloud credential directory and upgrade any existing D2
// registration from the legacy (LocalMachine, permissive-ACL) layout to the
// hardened (CurrentUser DPAPI + ACL-denied Users) layout. Runs as LocalSystem
// so the legacy blob remains decryptable exactly once, at upgrade time.
//
// RESILIENCE: ACL hardening is defense-in-depth; the cryptographic boundary is
// the CurrentUser DPAPI scope (LocalSystem profile), which ordinary users
// cannot decrypt even if they read the bytes. If hardening fails (it should
// not, as LocalSystem), we must NOT brick the whole Service (routing included)
// over a secondary control — log and continue; per-write file hardening
// (CloudStateSecurity.HardenFile) remains the ongoing backstop.
string cloudDirectory = Path.Combine(dataDirectory, "cloud");
try
{
    PathVeer.Service.Cloud.CloudStateSecurity.EnsureSecured(cloudDirectory);

    var cloudMigrator = new PathVeer.Service.Cloud.CloudRegistrationMigrator(
        legacyPath: Path.Combine(dataDirectory, "cloud-registration.json"),
        newPath: Path.Combine(cloudDirectory, "cloud-registration.json"),
        protector: new PathVeer.Service.Cloud.WindowsDpapiCloudSecretProtector(),
        legacyDecoder: new PathVeer.Service.Cloud.LegacyCloudCredentialDecoder());
    await cloudMigrator.MigrateAsync();
}
catch (Exception ex)
{
    Console.Error.WriteLine(
        "[PathVeer] Cloud state hardening/migration was skipped due to an " +
        "error; the credential remains protected by user-scoped DPAPI. " +
        "Detail: " + ex.GetType().Name + ": " + ex.Message);
}

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

builder.Services.AddPathVeerObservability(
    builder.Configuration,
    builder.Environment.EnvironmentName);

IHost host = builder.Build();

await host.RunAsync();
