using System.Diagnostics;
using System.Reflection;
using IranDirect.Core.Observability.Telemetry;
using Xunit;
using Xunit.Abstractions;

namespace IranDirect.Core.Tests.Observability.Telemetry;

/// <summary>
/// Guards the architectural boundary of the telemetry foundation: no
/// OpenTelemetry packages, no second ActivitySource/Meter, no workflow
/// instrumentation, no generic free-form helpers, planner/models telemetry-free.
/// Relies on reflection/assembly scanning, permitted in tests.
/// </summary>
public sealed class TelemetryArchitectureTests
{
    private readonly ITestOutputHelper _output;

    public TelemetryArchitectureTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void NoOpenTelemetryPackageReferences()
    {
        string[] projectFiles = Directory.GetFiles(
            Path.Combine(RepoRoot(), "IranDirect.Core"),
            "*.csproj",
            SearchOption.TopDirectoryOnly);

        foreach (var file in projectFiles)
        {
            string content = File.ReadAllText(file);
            Assert.DoesNotContain(
                "OpenTelemetry",
                content,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void SingleActivitySourceAndMeterDefinition()
    {
        var types = typeof(IranDirectTelemetry).Assembly.GetTypes();
        int sourceDefs = types.Count(t =>
            t.GetProperties(BindingFlags.Static | BindingFlags.Public)
             .Any(f => f.PropertyType == typeof(ActivitySource) &&
                       f.Name == nameof(IranDirectTelemetry.ActivitySource)));
        int meterDefs = types.Count(t =>
            t.GetProperties(BindingFlags.Static | BindingFlags.Public)
             .Any(f => f.PropertyType == typeof(System.Diagnostics.Metrics.Meter) &&
                       f.Name == nameof(IranDirectTelemetry.Meter)));

        Assert.Equal(1, sourceDefs);
        Assert.Equal(1, meterDefs);
    }

    [Fact]
    public void NoProductionWorkflowStartsActivities()
    {
        AssertNoMatch("StartActivity", new[]
        {
            "IranDirect.Core/Observability/Telemetry/",
            "IranDirect.Core.Tests/",
        });
    }

    [Fact]
    public void NoWorkflowRecordsMetrics()
    {
        // Only metric-instrument construction is forbidden in workflows this
        // phase. Collection .Add(.Record( on List/Dictionary/HashSet are
        // unrelated and intentionally ignored.
        AssertNoMatch(
            "Meter[.]Create|new Counter<|new Histogram<|new ObservableGauge<|CreateCounter|CreateHistogram|CreateGauge",
            new[]
            {
                "IranDirect.Core/Observability/Telemetry/",
                "IranDirect.Core.Tests/",
            });
    }

    [Fact]
    public void NoGenericFreeFormTelemetryHelpers()
    {
        // The foundation must expose only strongly-typed accessors, constants,
        // and bounded mappers — not StartActivity(name, Dictionary<...>) or
        // RecordMetric(name, params object[]).
        var methods = typeof(IranDirectTelemetry).Assembly
            .GetTypes()
            .Where(t => t.Namespace != null &&
                        t.Namespace.Contains("Observability.Telemetry"))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .ToList();

        foreach (var m in methods)
        {
            Assert.False(
                m.GetParameters().Any(p => p.ParameterType == typeof(string) &&
                                           p.Name is { } pname &&
                                           pname.Contains("name", StringComparison.OrdinalIgnoreCase) &&
                                           m.Name.Contains("Start", StringComparison.OrdinalIgnoreCase)),
                $"free-form StartActivity-like API found: {m.DeclaringType}.{m.Name}");
        }
    }

    [Fact]
    public void PlannerAndModelNamespaces_DoNotReferenceTelemetry()
    {
        string[] dirs =
        [
            "IranDirect.Core/Runtime/Reconciliation",
            "IranDirect.Core/Runtime/Execution",
            "IranDirect.Core/Routing",
        ];
        foreach (var dir in dirs)
        {
            if (!Directory.Exists(Path.Combine(RepoRoot(), dir)))
                continue;
            foreach (var file in Directory.GetFiles(
                         Path.Combine(RepoRoot(), dir), "*.cs", SearchOption.AllDirectories))
            {
                // RuntimeReconciler is the approved planning-telemetry owner;
                // planner models and the planner implementation itself must
                // stay telemetry-free (verified by
                // PlannerModelsAndPlannerImplementation_AreTelemetryFree).
                if (file.Replace('\\', '/').EndsWith(
                        "Runtime/Reconciliation/RuntimeReconciler.cs"))
                    continue;

                string content = File.ReadAllText(file);
                Assert.DoesNotContain(
                    "Observability.Telemetry",
                    content,
                    StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void ProhibitedTagConstants_DoNotAppearInProductionSource()
    {
        // Prohibited tag names live in the test catalog consts only; assert the
        // production telemetry foundation never references them as actual tags.
        string[] prohibited = IranDirectTagNames.Prohibited.ToArray();
        string foundationDir = Path.Combine(
            RepoRoot(), "IranDirect.Core/Observability/Telemetry");
        foreach (var file in Directory.GetFiles(foundationDir, "*.cs"))
        {
            // IranDirectTagNames.cs defines the prohibited list itself, and
            // IranDirectTagValues.cs / the mapper legitimately carry bounded
            // values (e.g. route_kind=endpoint). The contract under test is
            // that no workflow uses a prohibited name as a tag *name*; with no
            // instrumentation yet, the remaining files must be clean.
            string normalized = file.Replace('\\', '/');
            if (normalized.EndsWith("IranDirectTagNames.cs") ||
                normalized.EndsWith("IranDirectTagValues.cs") ||
                normalized.EndsWith("TelemetryOutcomeMapper.cs") ||
                normalized.EndsWith("TelemetryFailureCategoryMapper.cs"))
                continue;

            string content = File.ReadAllText(file);
            foreach (var tag in prohibited)
            {
                Assert.DoesNotContain(
                    $"\"{tag}\"",
                    content,
                    StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void ExactlyOneRuntimeCycleStartActivity_NoChildSpans()
    {
        // Production must start exactly one runtime-cycle root activity, and
        // no child spans yet. The single approved StartActivity passes the
        // Phase 32.2 name constant. Count StartActivity( occurrences in
        // production that reference RuntimeCycle (Telemetry foundation is the
        // home of the one approved call; tests are excluded).
        string root = RepoRoot();
        int runtimeCycleStarts = 0;

        foreach (var file in Directory.GetFiles(
                     Path.Combine(root, "IranDirect.Core"),
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            string normalized = file.Replace('\\', '/');
            if (normalized.Contains("IranDirect.Core.Tests/"))
                continue;

            string content = File.ReadAllText(file);
            if (content.Contains("StartActivity(") &&
                content.Contains("IranDirectActivityNames.RuntimeCycle"))
                runtimeCycleStarts++;
        }

        Assert.Equal(1, runtimeCycleStarts);

        // No workflow StartActivity with a non-constant string name anywhere in
        // production (tests excluded).
        var matches = new List<string>();
        foreach (var file in Directory.GetFiles(
                     Path.Combine(root, "IranDirect.Core"),
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            string normalized = file.Replace('\\', '/');
            if (normalized.Contains("IranDirect.Core.Tests/"))
                continue;

            foreach (var line in File.ReadAllLines(file))
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(
                        line, @"StartActivity\s*\(\s*\"""))
                    matches.Add($"{file}: {line.Trim()}");
            }
        }
        if (matches.Count != 0)
        {
            foreach (var m in matches.Take(20))
                _output.WriteLine(m);
            Assert.Fail($"{matches.Count} dynamic span name(s) found");
        }
    }

    [Fact]
    public void ExactlyFiveRuntimeCycleInstruments()
    {
        string path = Path.Combine(
            RepoRoot(),
            "IranDirect.Core/Observability/Telemetry/RuntimeCycleTelemetry.cs");
        string content = File.ReadAllText(path);

        int counters = System.Text.RegularExpressions.Regex.Matches(
            content, @"Meter\.CreateCounter<long>").Count;
        int histograms = System.Text.RegularExpressions.Regex.Matches(
            content, @"Meter\.CreateHistogram<double>").Count;

        Assert.Equal(4, counters);
        Assert.Equal(1, histograms);
    }

    [Fact]
    public void Controller_OnlyApprovedRuntimeCycleInstrumentation()
    {
        // The controller may only StartActivity(IranDirectActivityNames.RuntimeCycle)
        // and call RuntimeCycleTelemetry.Start; it must not create instruments
        // or use any other telemetry name.
        string path = Path.Combine(RepoRoot(), "IranDirect.Core/IranDirectController.cs");
        string content = File.ReadAllText(path);

        Assert.Contains(
            "RuntimeCycleTelemetry.Start(TelemetryTrigger",
            content);
        Assert.DoesNotContain("new ActivitySource", content);
        Assert.DoesNotContain("Meter.Create", content);
        Assert.DoesNotContain("StartActivity(IranDirectActivityNames.", content);
    }

    private void AssertNoMatch(string pattern, string[] excludeDirs)
    {
        var matches = new List<string>();
        foreach (var file in Directory.GetFiles(
                     Path.Combine(RepoRoot(), "IranDirect.Core"),
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            if (excludeDirs.Any(d =>
                    file.Replace('\\', '/').Contains(d.Replace('\\', '/'))))
                continue;

            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(lines[i], pattern))
                    matches.Add($"{file}:{i + 1}: {lines[i].Trim()}");
            }
        }

        if (matches.Count != 0)
        {
            foreach (var m in matches.Take(20))
                _output.WriteLine(m);
            Assert.Fail($"{matches.Count} workflow instrumentation match(es) found");
        }
    }

    [Fact]
    public void ExactlyOnePlanningStartActivity_NoChildSpansBeyondPlanChanges()
    {
        // Production must start exactly one Runtime.PlanChanges activity, and
        // no child spans beneath it. Count StartActivity( occurrences that
        // reference RuntimePlanChanges (Telemetry foundation is the home of
        // the one approved call; tests excluded).
        string root = RepoRoot();
        int planStarts = 0;

        foreach (var file in Directory.GetFiles(
                     Path.Combine(root, "IranDirect.Core"),
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            string normalized = file.Replace('\\', '/');
            if (normalized.Contains("IranDirect.Core.Tests/"))
                continue;

            string content = File.ReadAllText(file);
            if (content.Contains("StartActivity(") &&
                content.Contains("RuntimePlanChanges"))
                planStarts++;
        }

        Assert.Equal(1, planStarts);

        // No production StartActivity with a non-constant string name.
        var matches = new List<string>();
        foreach (var file in Directory.GetFiles(
                     Path.Combine(root, "IranDirect.Core"),
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            string normalized = file.Replace('\\', '/');
            if (normalized.Contains("IranDirect.Core.Tests/"))
                continue;

            foreach (var line in File.ReadAllLines(file))
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(
                        line, @"StartActivity\s*\(\s*\"""))
                    matches.Add($"{file}: {line.Trim()}");
            }
        }
        if (matches.Count != 0)
        {
            foreach (var m in matches.Take(20))
                _output.WriteLine(m);
            Assert.Fail($"{matches.Count} dynamic span name(s) found");
        }
    }

    [Fact]
    public void ExactlyOnePlanningDurationAndChangedRoutesHistograms()
    {
        string path = Path.Combine(
            RepoRoot(),
            "IranDirect.Core/Observability/Telemetry/RuntimePlanningTelemetry.cs");
        string content = File.ReadAllText(path);

        int durations = System.Text.RegularExpressions.Regex.Matches(
            content, @"Meter\.CreateHistogram<double>").Count;
        Assert.Equal(2, durations); // planning.duration + changed_routes

        // No planner lifecycle counters invented.
        Assert.DoesNotContain("planner.started", content);
        Assert.DoesNotContain("planner.completed", content);
        Assert.DoesNotContain("planner.failed", content);
        Assert.DoesNotContain("CreateCounter", content);
    }

    [Fact]
    public void PlannerModelsAndPlannerImplementation_AreTelemetryFree()
    {
        // RuntimeChangeSetPlanner itself must remain free of telemetry
        // references (only a non-sealed/virtual Plan seam was added for tests).
        string plannerPath = Path.Combine(
            RepoRoot(),
            "IranDirect.Core/Runtime/Reconciliation/RuntimeChangeSetPlanner.cs");
        string content = File.ReadAllText(plannerPath);
        Assert.DoesNotContain("Observability.Telemetry", content);
        Assert.DoesNotContain("StartActivity", content);
        Assert.DoesNotContain("Meter", content);
    }

    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "IranDirect.slnx")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repo root not found");
    }
}
