using IranDirect.Core.Testing.FaultInjection;

namespace IranDirect.Core.Tests.Testing.FaultInjection;

public sealed class FaultInjectionArchitectureTests
{
    private const string FrameworkNamespace =
        "IranDirect.Core.Testing.FaultInjection";

    private static readonly string[] AllowedConsumerSuffixes =
    [
        "IranDirect.Core\\Persistence\\JsonStore.cs",
        "IranDirect.Core\\Prefixes\\OfficialIranPrefixSource.cs",
        "IranDirect.Core\\Prefixes\\OfficialIranPrefixUpdateChecker.cs",
        "IranDirect.Core\\CustomRoutes\\CustomRouteResolver.cs",
        "IranDirect.Core\\Routing\\WindowsRouteManager.cs",
        "IranDirect.Core\\Routing\\WindowsRouteApi.cs",
        "IranDirect.Core\\Ipc\\IranDirectServiceClient.cs",
        "IranDirect.Core\\Observability\\RuntimeSnapshotProvider.cs",
        "IranDirect.Core\\Diagnostics\\DiagnosticRunner.cs"
    ];

    [Fact]
    public void ProductionReferences_AreConfinedToAllowedFiles()
    {
        string[] productionFiles = FindProductionReferences();

        Assert.All(
            productionFiles,
            path => Assert.Contains(
                AllowedConsumerSuffixes,
                suffix => path.EndsWith(
                    suffix,
                    StringComparison.Ordinal)));
    }

    [Fact]
    public void OnlySanctionedConsumers_ReferenceTheFramework()
    {
        string[] consumers = FindRuntimeConsumers();

        Assert.All(
            consumers,
            path => Assert.Contains(
                AllowedConsumerSuffixes,
                suffix => path.EndsWith(
                    suffix,
                    StringComparison.Ordinal)));
    }

    private static string[] FindProductionReferences()
    {
        string root = FindRepositoryRoot();
        string testingRoot = Path.Combine(
            root, "IranDirect.Core", "Testing");
        string[] projectFolders =
        [
            Path.Combine(root, "IranDirect.Cli"),
            Path.Combine(root, "IranDirect.Core"),
            Path.Combine(root, "IranDirect.Service"),
            Path.Combine(root, "IranDirect.Tray")
        ];

        return projectFolders
            .Where(Directory.Exists)
            .SelectMany(Directory.EnumerateFiles)
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal))
            .Where(IsNotGenerated)
            .Where(path => !path.StartsWith(
                testingRoot,
                StringComparison.Ordinal))
            .Where(ContainsFrameworkReference)
            .ToArray();
    }

    private static string[] FindRuntimeConsumers()
    {
        string root = FindRepositoryRoot();
        string testingRoot = Path.Combine(
            root, "IranDirect.Core", "Testing");
        string[] runtimeFolders =
        [
            Path.Combine(root, "IranDirect.Cli"),
            Path.Combine(root, "IranDirect.Core"),
            Path.Combine(root, "IranDirect.Service"),
            Path.Combine(root, "IranDirect.Tray")
        ];

        return runtimeFolders
            .Where(Directory.Exists)
            .SelectMany(Directory.EnumerateFiles)
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal))
            .Where(IsNotGenerated)
            .Where(path => !path.StartsWith(
                testingRoot,
                StringComparison.Ordinal))
            .Where(ContainsFrameworkReference)
            .ToArray();
    }

    private static bool ContainsFrameworkReference(string path)
    {
        string[] lines = File.ReadAllLines(path);

        return lines.Any(line => line.Contains(
            FrameworkNamespace,
            StringComparison.Ordinal));
    }

    private static bool IsNotGenerated(string path) =>
        !path.Contains("\\obj\\", StringComparison.Ordinal)
        && !path.Contains("\\bin\\", StringComparison.Ordinal);

    private static string FindRepositoryRoot()
    {
        string? current = AppContext.BaseDirectory;

        while (current is not null)
        {
            if (File.Exists(Path.Combine(
                current, "IranDirect.slnx")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new InvalidOperationException(
            "Repository root was not found.");
    }
}
