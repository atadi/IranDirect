using PathVeer.Service;
using PathVeer.Service.Observability;

string dataDirectory = Path.Combine(
    Environment.GetFolderPath(
        Environment.SpecialFolder.CommonApplicationData),
    "IranDirect");

Directory.CreateDirectory(dataDirectory);

HostApplicationBuilder builder =
    Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "IranDirect Service";
});

// The application dependency graph lives in ServiceCompositionRoot so that the
// Service composition tests exercise the exact registrations the host uses.
builder.Services.AddIranDirectServiceComposition(
    builder.Configuration,
    dataDirectory);

builder.Services.AddIranDirectObservability(
    builder.Configuration,
    builder.Environment.EnvironmentName);

IHost host = builder.Build();

await host.RunAsync();
