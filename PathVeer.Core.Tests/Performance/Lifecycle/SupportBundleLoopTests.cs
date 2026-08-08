using System.IO.Compression;
using PathVeer.Core.Support;
using PathVeer.Testing.Performance.Lifecycle;

namespace PathVeer.Core.Tests.Performance.Lifecycle;

/// <summary>
/// Support bundle loop: repeatedly captures support snapshots,
/// serializes them, and exports real ZIP bundles into a temporary
/// workspace through the production
/// <see cref="SupportBundleExporter"/>. Every produced archive must
/// open cleanly and no orphan temporary file may survive a cycle.
/// </summary>
public sealed class SupportBundleLoopTests
{
    [Fact]
    public async Task RepeatedExports_ProduceValidArchivesAndCleanWorkspace()
    {
        const int exports = 25;

        using SimulatedRuntimeEnvironment environment = new();

        await ServiceSimulationRunner.RunAsync(
            environment,
            LifecycleScenarioFixtures.ConvergedPlan(
                "support-install",
                idleCycles: 0));

        ServiceSimulationRunResult result =
            await ServiceSimulationRunner.RunAsync(
                environment,
                new ServiceSimulationPlan(
                    "support-loop",
                    [new ExportSupportBundleStep(exports)]));

        Assert.Equal(exports, result.Metrics.SupportBundlesExported);

        AssertAllBundlesValid(environment, exports);

        ServiceSimulationVerifier.AssertNoPendingTempFiles(environment);
        ServiceSimulationVerifier.AssertNoActiveExecution(environment);
        ServiceSimulationVerifier.AssertOperationCompleted(environment);
    }

    [Fact]
    public async Task RepeatedSnapshotSerialization_StaysValidJson()
    {
        const int captures = 50;

        using SimulatedRuntimeEnvironment environment = new();

        string? previous = null;

        for (int i = 0; i < captures; i++)
        {
            SupportSnapshot snapshot = await environment
                .SupportSnapshotProvider
                .CaptureAsync();

            string json = environment.SupportSnapshotSerializer
                .Serialize(snapshot);

            Assert.False(string.IsNullOrWhiteSpace(json));

            // Each serialization must be independently parseable.
            using System.Text.Json.JsonDocument document =
                System.Text.Json.JsonDocument.Parse(json);

            Assert.Equal(
                System.Text.Json.JsonValueKind.Object,
                document.RootElement.ValueKind);

            previous = json;
        }

        Assert.NotNull(previous);
        ServiceSimulationVerifier.AssertNoPendingTempFiles(environment);
    }

    [Fact]
    public async Task ExportAfterFault_RecoversOnNextCycle()
    {
        using SimulatedRuntimeEnvironment environment = new();

        ServiceSimulationRunResult result =
            await ServiceSimulationRunner.RunAsync(
                environment,
                new ServiceSimulationPlan(
                    "support-fault",
                    [
                        new FaultScopeStep(
                            PathVeer.Core.Testing.FaultInjection
                                .FaultInjectionPoint.SnapshotCapture,
                            new ExportSupportBundleStep(
                                1,
                                "faulted")),
                        new ExportSupportBundleStep(1, "recovered")
                    ]));

        Assert.Equal(1, result.Metrics.FaultScopesEntered);
        Assert.Equal(1, result.Metrics.RecoverableFailuresObserved);

        // Only the post-fault export completed and was recorded.
        Assert.Equal(1, result.Metrics.SupportBundlesExported);

        string recovered = Path.Combine(
            environment.Workspace.SupportDirectory,
            "recovered-0.zip");

        Assert.True(
            File.Exists(recovered),
            "Recovery export did not produce a bundle.");

        AssertZipOpens(recovered);

        // A faulted export must not leave a half-written archive or
        // any orphan temporary file behind.
        ServiceSimulationVerifier.AssertNoPendingTempFiles(environment);
    }

    [Fact]
    public async Task TwoIndependentEnvironments_DoNotShareWorkspaces()
    {
        using SimulatedRuntimeEnvironment first = new();
        using SimulatedRuntimeEnvironment second = new();

        Assert.NotEqual(
            first.Workspace.PathValue,
            second.Workspace.PathValue);

        await ServiceSimulationRunner.RunAsync(
            first,
            new ServiceSimulationPlan(
                "support-first",
                [new ExportSupportBundleStep(3, "first")]));

        await ServiceSimulationRunner.RunAsync(
            second,
            new ServiceSimulationPlan(
                "support-second",
                [new ExportSupportBundleStep(2, "second")]));

        Assert.Equal(
            3,
            CountBundles(first));
        Assert.Equal(
            2,
            CountBundles(second));

        ServiceSimulationVerifier.AssertNoPendingTempFiles(first);
        ServiceSimulationVerifier.AssertNoPendingTempFiles(second);
    }

    private static int CountBundles(
        SimulatedRuntimeEnvironment environment) =>
        Directory.Exists(environment.Workspace.SupportDirectory)
            ? Directory.GetFiles(
                environment.Workspace.SupportDirectory,
                "*.zip").Length
            : 0;

    private static void AssertAllBundlesValid(
        SimulatedRuntimeEnvironment environment,
        int expected)
    {
        string[] bundles = Directory.GetFiles(
            environment.Workspace.SupportDirectory,
            "*.zip");

        Assert.Equal(expected, bundles.Length);

        foreach (string bundle in bundles)
        {
            AssertZipOpens(bundle);
        }
    }

    private static void AssertZipOpens(string path)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);

        Assert.NotEmpty(archive.Entries);
    }
}
