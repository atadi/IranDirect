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
// hardened (CurrentUser DPAPI + canonical ACL) layout. Runs as LocalSystem so
// the legacy blob remains decryptable exactly once, at upgrade time.
//
// RESILIENCE: a Cloud security problem must NOT take down routing or the whole
// PathVeer Service. ACL hardening is defense-in-depth; the cryptographic
// boundary is the CurrentUser DPAPI scope (LocalSystem profile), which ordinary
// users cannot decrypt even if they read the bytes. If hardening/migration
// fails (it should not, as LocalSystem), we log a clear, non-secret diagnostic
// and CONTINUE — routing stays up. Crucially, on failure we must NOT claim the
// credential is CurrentUser-protected; the vulnerable legacy LocalMachine blob
// may still be present and is the original at-risk format.
string cloudDirectory = Path.Combine(dataDirectory, "cloud");
string legacyCloudPath = Path.Combine(dataDirectory, "cloud-registration.json");
try
{
    // As early as safely possible, deny ordinary users read access to the
    // legacy vulnerable blob (if it exists) so it cannot be obtained while
    // migration is pending.
    PathVeer.Service.Cloud.CloudStateSecurity.SecureLegacyFileIfPresent(
        legacyCloudPath);

    PathVeer.Service.Cloud.CloudStateSecurity.EnsureSecured(cloudDirectory);

    var cloudMigrator = new PathVeer.Service.Cloud.CloudRegistrationMigrator(
        legacyPath: legacyCloudPath,
        newPath: Path.Combine(cloudDirectory, "cloud-registration.json"),
        protector: new PathVeer.Service.Cloud.WindowsDpapiCloudSecretProtector(),
        legacyDecoder: new PathVeer.Service.Cloud.LegacyCloudCredentialDecoder());
    await cloudMigrator.MigrateAsync();
}
catch (Exception ex)
{
    Console.Error.WriteLine(
        "[PathVeer] Cloud state hardening/migration did NOT complete: " +
        "the legacy credential file may still use the original " +
        "LocalMachine (machine-wide, any-local-user) DPAPI format and is " +
        "NOT yet CurrentUser-protected. PathVeer continues operating; " +
        "routing is unaffected. Remediate Cloud enrollment manually if " +
        "needed. Detail: " + ex.GetType().Name + ": " + ex.Message);
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
