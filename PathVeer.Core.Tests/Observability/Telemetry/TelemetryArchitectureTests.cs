using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using PathVeer.Core.Observability.Telemetry;
using Xunit;
using Xunit.Abstractions;

namespace PathVeer.Core.Tests.Observability.Telemetry;

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
            Path.Combine(RepoRoot(), "PathVeer.Core"),
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
            "PathVeer.Core/Observability/Telemetry/",
            "PathVeer.Core.Tests/",
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
                "PathVeer.Core/Observability/Telemetry/",
                "PathVeer.Core.Tests/",
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
            "PathVeer.Core/Runtime/Reconciliation",
            "PathVeer.Core/Runtime/Execution",
            "PathVeer.Core/Routing",
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
            RepoRoot(), "PathVeer.Core/Observability/Telemetry");
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
                     Path.Combine(root, "PathVeer.Core"),
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            string normalized = file.Replace('\\', '/');
            if (normalized.Contains("PathVeer.Core.Tests/"))
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
                     Path.Combine(root, "PathVeer.Core"),
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            string normalized = file.Replace('\\', '/');
            if (normalized.Contains("PathVeer.Core.Tests/"))
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
            "PathVeer.Core/Observability/Telemetry/RuntimeCycleTelemetry.cs");
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
        string path = Path.Combine(RepoRoot(), "PathVeer.Core/PathVeerController.cs");
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
                     Path.Combine(RepoRoot(), "PathVeer.Core"),
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
                     Path.Combine(root, "PathVeer.Core"),
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            string normalized = file.Replace('\\', '/');
            if (normalized.Contains("PathVeer.Core.Tests/"))
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
                     Path.Combine(root, "PathVeer.Core"),
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            string normalized = file.Replace('\\', '/');
            if (normalized.Contains("PathVeer.Core.Tests/"))
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
            "PathVeer.Core/Observability/Telemetry/RuntimePlanningTelemetry.cs");
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
            "PathVeer.Core/Runtime/Reconciliation/RuntimeChangeSetPlanner.cs");
        string content = File.ReadAllText(plannerPath);
        Assert.DoesNotContain("Observability.Telemetry", content);
        Assert.DoesNotContain("StartActivity", content);
        Assert.DoesNotContain("Meter", content);
    }

    [Fact]
    public void ExactlyOneExecutionStartActivity_NoChildSpans()
    {
        // Production must start exactly one Runtime.Execute activity. Count
        // StartActivity( occurrences that reference RuntimeExecute.
        string root = RepoRoot();
        int execStarts = 0;

        foreach (var file in Directory.GetFiles(
                     Path.Combine(root, "PathVeer.Core"),
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            string normalized = file.Replace('\\', '/');
            if (normalized.Contains("PathVeer.Core.Tests/"))
                continue;

            string content = File.ReadAllText(file);
            if (content.Contains("StartActivity(") &&
                content.Contains("RuntimeExecute"))
                execStarts++;
        }

        Assert.Equal(1, execStarts);
    }

    [Fact]
    public void ExactlyTwoExecutionHistograms_NoLifecycleCounters()
    {
        string path = Path.Combine(
            RepoRoot(),
            "PathVeer.Core/Observability/Telemetry/RuntimeExecutionTelemetry.cs");
        string content = File.ReadAllText(path);

        int histograms = System.Text.RegularExpressions.Regex.Matches(
            content, @"Meter\.CreateHistogram<double>").Count;
        Assert.Equal(2, histograms); // execution.duration + operations.per_cycle

        // No execution lifecycle counters invented.
        Assert.DoesNotContain("execution.started", content);
        Assert.DoesNotContain("execution.completed", content);
        Assert.DoesNotContain("execution.failed", content);
        Assert.DoesNotContain("CreateCounter", content);
    }

    [Fact]
    public void NoStartActivityInsideExecutorOrLoops_NoPerStepRecords()
    {
        // RuntimeExecutor must remain telemetry-free, and no per-step
        // Histogram.Record calls may exist in execution paths.
        string executorPath = Path.Combine(
            RepoRoot(),
            "PathVeer.Core/Runtime/Execution/RuntimeExecutor.cs");
        string executorContent = File.ReadAllText(executorPath);
        Assert.DoesNotContain("Observability.Telemetry", executorContent);
        Assert.DoesNotContain("StartActivity", executorContent);
        Assert.DoesNotContain("Histogram", executorContent);

        string execTelemetry = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "PathVeer.Core/Observability/Telemetry/RuntimeExecutionTelemetry.cs"));
        // The two histograms are created once at static init; no per-step
        // Record calls should appear outside the two terminal helpers.
        int recordCalls = System.Text.RegularExpressions.Regex.Matches(
            execTelemetry, @"\.Record\(").Count;
        // 2 terminal helpers each call Record once.
        Assert.Equal(2, recordCalls);
    }

    [Fact]
    public void ExecutionTelemetry_OnlyApprovedTags_NoProhibited()
    {
        string path = Path.Combine(
            RepoRoot(),
            "PathVeer.Core/Observability/Telemetry/RuntimeExecutionTelemetry.cs");
        string content = File.ReadAllText(path);

        // operation / outcome / failure_category only.
        Assert.Contains("IranDirectTagNames.Operation", content);
        Assert.Contains("IranDirectTagNames.Outcome", content);

        foreach (var tag in IranDirectTagNames.Prohibited)
        {
            Assert.DoesNotContain($"\"{tag}\"", content);
        }
    }

    [Fact]
    public void ExactlyOneRouteSystemCallActivityLocation_NoPerRouteSpans()
    {
        // All Routes.* activities must originate from a single shared helper in
        // RouteSystemCallTelemetry. Count production (excludes tests) files that
        // reference a Routes.* activity name AND StartActivity(. There must be
        // exactly one such file, containing exactly one StartActivity call.
        string root = RepoRoot();
        int files = 0;
        int startCalls = 0;

        foreach (var file in Directory.GetFiles(
                     Path.Combine(root, "PathVeer.Core"),
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            string normalized = file.Replace('\\', '/');
            if (normalized.Contains("PathVeer.Core.Tests/"))
                continue;

            string content = File.ReadAllText(file);
            bool referencesRoutes = content.Contains("RoutesEnumerate") ||
                content.Contains("RoutesCreate") ||
                content.Contains("RoutesDelete");
            if (referencesRoutes && content.Contains("StartActivity("))
            {
                files++;
                startCalls += System.Text.RegularExpressions.Regex
                    .Matches(content, @"StartActivity\s*\(").Count;
            }
        }

        Assert.Equal(1, files);
        Assert.Equal(1, startCalls);
    }

    [Fact]
    public void RouteTelemetry_UsesApprovedActivityConstants()
    {
        string path = Path.Combine(
            RepoRoot(),
            "PathVeer.Core/Observability/Telemetry/RouteSystemCallTelemetry.cs");
        string content = File.ReadAllText(path);

        Assert.Contains("IranDirectActivityNames.RoutesEnumerate", content);
        Assert.Contains("IranDirectActivityNames.RoutesCreate", content);
        Assert.Contains("IranDirectActivityNames.RoutesDelete", content);
    }

    [Fact]
    public void RouteTelemetry_ExactlyThreeCountersAndOneHistogram()
    {
        string path = Path.Combine(
            RepoRoot(),
            "PathVeer.Core/Observability/Telemetry/RouteSystemCallTelemetry.cs");
        string content = File.ReadAllText(path);

        int counters = System.Text.RegularExpressions.Regex.Matches(
            content, @"CreateCounter<long>").Count;
        int histograms = System.Text.RegularExpressions.Regex.Matches(
            content, @"CreateHistogram<double>").Count;

        Assert.Equal(3, counters); // requested, succeeded, failed
        Assert.Equal(1, histograms); // system_call.duration

        Assert.Contains(
            "IranDirectMetricNames.RoutesOperationsRequested", content);
        Assert.Contains(
            "IranDirectMetricNames.RoutesOperationsSucceeded", content);
        Assert.Contains(
            "IranDirectMetricNames.RoutesOperationsFailed", content);
        Assert.Contains(
            "IranDirectMetricNames.RoutesSystemCallDuration", content);
    }

    [Fact]
    public void RouteTelemetry_NoPerRouteRecordsOrLoops()
    {
        string path = Path.Combine(
            RepoRoot(),
            "PathVeer.Core/Observability/Telemetry/RouteSystemCallTelemetry.cs");
        string content = File.ReadAllText(path);

        // No Histogram.Record calls inside any loop; the single duration Record
        // is in the static terminal helper. No per-route Counter.Add.
        int recordCalls = System.Text.RegularExpressions.Regex.Matches(
            content, @"\.Record\(").Count;
        int addCalls = System.Text.RegularExpressions.Regex.Matches(
            content, @"\.Add\(").Count;

        Assert.Equal(1, recordCalls); // duration only
        Assert.Equal(3, addCalls);    // requested, succeeded, failed (each once)

        // No foreach/for over routes inside the telemetry file.
        Assert.DoesNotContain("foreach", content);
        Assert.DoesNotContain("for (", content);
    }

    [Fact]
    public void RouteTelemetry_NoTelemetryReferencesInNativeOrModels()
    {
        // The native boundary and route models must stay telemetry-free so the
        // decorator remains the sole bridge.
        string windowsApi = Path.Combine(
            RepoRoot(), "PathVeer.Core/Routing/WindowsRouteApi.cs");
        Assert.DoesNotContain(
            "Observability.Telemetry", File.ReadAllText(windowsApi));
        Assert.DoesNotContain("StartActivity", File.ReadAllText(windowsApi));
        Assert.DoesNotContain("Meter", File.ReadAllText(windowsApi));

        string managedRoute = Path.Combine(
            RepoRoot(), "PathVeer.Core/Routing/ManagedRoute.cs");
        Assert.DoesNotContain(
            "Observability.Telemetry", File.ReadAllText(managedRoute));
    }

    [Fact]
    public void RouteTelemetry_OnlyApprovedTags_NoProhibited()
    {
        string path = Path.Combine(
            RepoRoot(),
            "PathVeer.Core/Observability/Telemetry/RouteSystemCallTelemetry.cs");
        string content = File.ReadAllText(path);

        Assert.Contains("IranDirectTagNames.Operation", content);
        Assert.Contains("IranDirectTagNames.Outcome", content);

        foreach (var tag in IranDirectTagNames.Prohibited)
        {
            Assert.DoesNotContain($"\"{tag}\"", content);
        }

        // No command/process-output tags: the file never references native
        // output, exit codes, or script contents as data.
        Assert.DoesNotContain("StandardOutput", content);
        Assert.DoesNotContain("StandardError", content);
        Assert.DoesNotContain("ExitCode", content);
    }

    [Fact]
    public void RouteTelemetry_WrapperIsOnlyBridge_NoOpenTelemetry()
    {
        // The decorator lives in the telemetry namespace and is the only new
        // file bridging to the native boundary; no OpenTelemetry packages or
        // exporters are introduced.
        string path = Path.Combine(
            RepoRoot(),
            "PathVeer.Core/Observability/Telemetry/TelemetryRouteApi.cs");
        string content = File.ReadAllText(path);
        Assert.Contains("IWindowsRouteApi", content);
        Assert.DoesNotContain("OpenTelemetry", content, StringComparison.OrdinalIgnoreCase);

        // The composition root must wire the wrapper but add no exporter and
        // no OpenTelemetry reference. The application graph lives in
        // ServiceCompositionRoot.cs (Program.cs delegates to it); both files
        // are checked so neither can smuggle in an exporter.
        string compositionRoot = Path.Combine(
            RepoRoot(), "PathVeer.Service/ServiceCompositionRoot.cs");
        string compositionRootContent = File.ReadAllText(compositionRoot);
        Assert.Contains("TelemetryRouteApi", compositionRootContent);
        Assert.DoesNotContain(
            "OpenTelemetry",
            compositionRootContent,
            StringComparison.OrdinalIgnoreCase);

        string program = Path.Combine(RepoRoot(), "PathVeer.Service/Program.cs");
        string programContent = File.ReadAllText(program);
        Assert.DoesNotContain(
            "OpenTelemetry", programContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Controller_OnlyApprovedExecutionInstrumentation()
    {
        // The controller may only call RuntimeExecutionTelemetry.Start and the
        // scope terminal methods; it must not create instruments or use other
        // telemetry names for execution.
        string path = Path.Combine(RepoRoot(), "PathVeer.Core/PathVeerController.cs");
        string content = File.ReadAllText(path);

        Assert.Contains("RuntimeExecutionTelemetry.Start(", content);
        Assert.DoesNotContain("new ActivitySource", content);
        Assert.DoesNotContain("Meter.Create", content);
    }

    [Fact]
    public void PrefixUpdateTelemetry_ExactlyOneRootNoPerPrefixSpans()
    {
        // All PrefixUpdateCheck / Prefix.* activities must originate from the
        // two approved helper files. Count production files that reference a
        // Prefix activity name AND a StartActivity( call.
        string root = RepoRoot();
        int files = 0;
        int startCalls = 0;

        foreach (var file in Directory.GetFiles(
                     Path.Combine(root, "PathVeer.Core"),
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            string normalized = file.Replace('\\', '/');
            if (normalized.Contains("PathVeer.Core.Tests/"))
                continue;

            string content = File.ReadAllText(file);
            bool referencesPrefix = content.Contains("PrefixUpdateCheck") ||
                content.Contains("PrefixHttpHead") ||
                content.Contains("PrefixHttpGet") ||
                content.Contains("PrefixCompare") ||
                content.Contains("PrefixPersistMetadata");
            if (referencesPrefix && content.Contains("StartActivity("))
            {
                files++;
                startCalls += System.Text.RegularExpressions.Regex
                    .Matches(content, @"StartActivity\s*\(").Count;
            }
        }

        Assert.Equal(1, files);   // PrefixUpdateTelemetry.cs only
        // The helper has exactly TWO literal StartActivity( calls: one for the
        // root PrefixUpdateCheck span, and one shared StartChild(...) used by
        // the Head/Get/Compare children. No per-prefix StartActivity exists.
        Assert.Equal(2, startCalls);
    }

    [Fact]
    public void PrefixUpdateTelemetry_UsesApprovedActivityConstants()
    {
        string path = Path.Combine(
            RepoRoot(),
            "PathVeer.Core/Observability/Telemetry/PrefixUpdateTelemetry.cs");
        string content = File.ReadAllText(path);

        Assert.Contains("IranDirectActivityNames.PrefixUpdateCheck", content);
        Assert.Contains("IranDirectActivityNames.PrefixHttpHead", content);
        Assert.Contains("IranDirectActivityNames.PrefixHttpGet", content);
        Assert.Contains("IranDirectActivityNames.PrefixCompare", content);
    }

    [Fact]
    public void PrefixUpdateTelemetry_ExactlyOneCounterAndOneHistogram()
    {
        string path = Path.Combine(
            RepoRoot(),
            "PathVeer.Core/Observability/Telemetry/PrefixUpdateTelemetry.cs");
        string content = File.ReadAllText(path);

        int counters = System.Text.RegularExpressions.Regex.Matches(
            content, @"CreateCounter<long>").Count;
        int histograms = System.Text.RegularExpressions.Regex.Matches(
            content, @"CreateHistogram<double>").Count;

        Assert.Equal(1, counters);  // prefix.checks
        Assert.Equal(1, histograms); // prefix.check.duration

        Assert.Contains(
            "IranDirectMetricNames.PrefixChecks", content);
        Assert.Contains(
            "IranDirectMetricNames.PrefixCheckDuration", content);
    }

    [Fact]
    public void PrefixUpdateTelemetry_OnlyApprovedTags_NoProhibited()
    {
        string path = Path.Combine(
            RepoRoot(),
            "PathVeer.Core/Observability/Telemetry/PrefixUpdateTelemetry.cs");
        string content = File.ReadAllText(path);

        Assert.Contains("IranDirectTagNames.Operation", content);
        Assert.Contains("IranDirectTagNames.Outcome", content);
        Assert.Contains("IranDirectTagNames.Source", content);
        Assert.Contains("IranDirectTagNames.Trigger", content);

        foreach (var tag in IranDirectTagNames.Prohibited)
        {
            Assert.DoesNotContain($"\"{tag}\"", content);
        }
    }

    [Fact]
    public void PrefixUpdateChecker_OnlyUsesTelemetryHelpers()
    {
        // The owner may call the dedicated telemetry helpers but must not
        // create activities/instruments itself.
        string path = Path.Combine(
            RepoRoot(), "PathVeer.Core/Prefixes/CountryPrefixUpdateChecker.cs");
        string content = File.ReadAllText(path);

        Assert.Contains("PrefixUpdateTelemetry.StartCheck", content);
        Assert.DoesNotContain("StartActivity(", content);
        Assert.DoesNotContain("Meter.Create", content);
        Assert.DoesNotContain("new ActivitySource", content);
    }

    [Fact]
    public void PrefixDatasetComparer_RemainsTelemetryFree()
    {
        string path = Path.Combine(
            RepoRoot(), "PathVeer.Core/Prefixes/PrefixDatasetComparer.cs");
        string content = File.ReadAllText(path);
        Assert.DoesNotContain("Observability.Telemetry", content);
        Assert.DoesNotContain("StartActivity", content);
        Assert.DoesNotContain("Meter", content);
    }

    [Fact]
    public void CustomRouteRefreshTelemetry_ExactlyOneRootNoPerAddressSpans()
    {
        string root = RepoRoot();
        int files = 0;
        int startCalls = 0;

        foreach (var file in Directory.GetFiles(
                     Path.Combine(root, "PathVeer.Core"),
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            string normalized = file.Replace('\\', '/');
            if (normalized.Contains("PathVeer.Core.Tests/"))
                continue;

            string content = File.ReadAllText(file);
            bool referencesDns = content.Contains("CustomRouteRefresh") ||
                content.Contains("DnsCacheRead") ||
                content.Contains("DnsResolve") ||
                content.Contains("DnsCacheWrite");
            if (referencesDns && content.Contains("StartActivity("))
            {
                files++;
                startCalls += System.Text.RegularExpressions.Regex
                    .Matches(content, @"StartActivity\s*\(").Count;
            }
        }

        Assert.Equal(1, files);   // CustomRouteRefreshTelemetry.cs only
        // The helper has exactly TWO literal StartActivity( calls: one for the
        // root CustomRouteRefresh span, and one shared StartChild(...) used by
        // the CacheRead/Resolve/CacheWrite children. No per-address
        // StartActivity exists.
        Assert.Equal(2, startCalls);
    }

    [Fact]
    public void CustomRouteRefreshTelemetry_UsesApprovedActivityConstants()
    {
        string path = Path.Combine(
            RepoRoot(),
            "PathVeer.Core/Observability/Telemetry/CustomRouteRefreshTelemetry.cs");
        string content = File.ReadAllText(path);

        Assert.Contains("IranDirectActivityNames.CustomRouteRefresh", content);
        Assert.Contains("IranDirectActivityNames.DnsCacheRead", content);
        Assert.Contains("IranDirectActivityNames.DnsResolve", content);
        Assert.Contains("IranDirectActivityNames.DnsCacheWrite", content);
    }

    [Fact]
    public void CustomRouteRefreshTelemetry_ExactlyOneLookupCounterAndHistogram()
    {
        string path = Path.Combine(
            RepoRoot(),
            "PathVeer.Core/Observability/Telemetry/CustomRouteRefreshTelemetry.cs");
        string content = File.ReadAllText(path);

        int counters = System.Text.RegularExpressions.Regex.Matches(
            content, @"CreateCounter<long>").Count;
        int histograms = System.Text.RegularExpressions.Regex.Matches(
            content, @"CreateHistogram<double>").Count;

        Assert.Equal(1, counters);  // dns.lookups
        Assert.Equal(1, histograms); // dns.lookup.duration

        Assert.Contains("IranDirectMetricNames.DnsLookups", content);
        Assert.Contains("IranDirectMetricNames.DnsLookupDuration", content);
        Assert.DoesNotContain("IranDirectMetricNames.DnsRefreshes", content);
        Assert.DoesNotContain("IranDirectMetricNames.DnsRefreshDuration", content);
    }

    [Fact]
    public void CustomRouteRefreshTelemetry_OnlyApprovedTags_NoProhibited()
    {
        string path = Path.Combine(
            RepoRoot(),
            "PathVeer.Core/Observability/Telemetry/CustomRouteRefreshTelemetry.cs");
        string content = File.ReadAllText(path);

        Assert.Contains("IranDirectTagNames.Operation", content);
        Assert.Contains("IranDirectTagNames.Outcome", content);
        Assert.Contains("IranDirectTagNames.Source", content);
        Assert.Contains("IranDirectTagNames.CacheState", content);

        foreach (var tag in IranDirectTagNames.Prohibited)
        {
            Assert.DoesNotContain($"\"{tag}\"", content);
        }
    }

    [Fact]
    public void CustomRouteResolver_OnlyUsesTelemetryHelpers()
    {
        string path = Path.Combine(
            RepoRoot(), "PathVeer.Core/CustomRoutes/CustomRouteResolver.cs");
        string content = File.ReadAllText(path);

        Assert.Contains("CustomRouteRefreshTelemetry.StartRefresh", content);
        Assert.DoesNotContain("StartActivity(", content);
        Assert.DoesNotContain("Meter.Create", content);
        Assert.DoesNotContain("new ActivitySource", content);
    }

    [Fact]
    public void JsonStore_FreeOfTelemetryReferences()
    {
        string storeDir = Path.Combine(
            RepoRoot(), "PathVeer.Core/Persistence/JsonStore");
        if (!Directory.Exists(storeDir))
        {
            storeDir = Path.Combine(
                RepoRoot(), "PathVeer.Core/Persistence");
        }
        foreach (var file in Directory.GetFiles(
                     storeDir, "JsonStore.cs", SearchOption.AllDirectories))
        {
            string content = File.ReadAllText(file);
            Assert.DoesNotContain("Observability.Telemetry", content);
            Assert.DoesNotContain("StartActivity", content);
            Assert.DoesNotContain("Meter", content);
        }
    }

    [Fact]
    public void NoOpenTelemetryPackagesOrExporters()
    {
        // Phase 32.9 intentionally adds OpenTelemetry hosting to the Service
        // host (and its test project). Core, CLI, Tray, Benchmarks, and Testing
        // must remain OpenTelemetry-free so the Core telemetry contracts stay
        // BCL-only and no telemetry is added to CLI/Tray business logic.
        string[] allowed = new[]
        {
            "PathVeer.Service/PathVeer.Service.csproj",
            "PathVeer.Service.Tests/PathVeer.Service.Tests.csproj",
        };
        string[] forbidden = new[]
        {
            "PathVeer.Core/PathVeer.Core.csproj",
            "PathVeer.Cli/PathVeer.Cli.csproj",
            "PathVeer.Tray/PathVeer.Tray.csproj",
            "PathVeer.Benchmarks/PathVeer.Benchmarks.csproj",
            "PathVeer.Testing/PathVeer.Testing.csproj",
        };

        foreach (string proj in allowed)
        {
            string content = File.ReadAllText(
                Path.Combine(RepoRoot(), proj));
            Assert.True(
                content.Contains("OpenTelemetry", StringComparison.OrdinalIgnoreCase),
                $"{proj} should reference OpenTelemetry packages");
        }

        foreach (string proj in forbidden)
        {
            string content = File.ReadAllText(
                Path.Combine(RepoRoot(), proj));
            Assert.False(
                content.Contains("OpenTelemetry", StringComparison.OrdinalIgnoreCase),
                $"{proj} must not reference OpenTelemetry packages");
            Assert.False(
                content.Contains("Exporter", StringComparison.OrdinalIgnoreCase),
                $"{proj} must not reference exporters");
        }
    }

    [Fact]
    public void IpcRequestTelemetry_ExactlyOneRootNoPerHandlerSpans()
    {
        // All Ipc.* activities must originate from IpcRequestTelemetry. Count
        // production files that reference an Ipc activity name AND a
        // StartActivity( call. Exactly one file, exactly two StartActivity
        // calls (the IranDirect.IpcRequest root + the shared StartChild(...)).
        string root = RepoRoot();
        int files = 0;
        int startCalls = 0;

        foreach (var file in Directory.GetFiles(
                     Path.Combine(root, "PathVeer.Core"),
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            string normalized = file.Replace('\\', '/');
            if (normalized.Contains("PathVeer.Core.Tests/"))
                continue;
            if (normalized.EndsWith("IpcDispatchTelemetry.cs"))
                continue;

            string content = File.ReadAllText(file);
            bool referencesIpc = content.Contains("IranDirectActivityNames.IpcRequest") ||
                content.Contains("IranDirectActivityNames.IpcConnect") ||
                content.Contains("IranDirectActivityNames.IpcSend") ||
                content.Contains("IranDirectActivityNames.IpcReceive");
            if (referencesIpc && content.Contains("StartActivity("))
            {
                files++;
                startCalls += System.Text.RegularExpressions.Regex
                    .Matches(content, @"StartActivity\s*\(").Count;
            }
        }

        Assert.Equal(1, files);   // IpcRequestTelemetry.cs only
        Assert.Equal(2, startCalls);
    }

    [Fact]
    public void IpcRequestTelemetry_UsesApprovedActivityConstants()
    {
        string path = Path.Combine(
            RepoRoot(),
            "PathVeer.Core/Observability/Telemetry/IpcRequestTelemetry.cs");
        string content = File.ReadAllText(path);

        Assert.Contains("IranDirectActivityNames.IpcRequest", content);
        Assert.Contains("IranDirectActivityNames.IpcConnect", content);
        Assert.Contains("IranDirectActivityNames.IpcSend", content);
        Assert.Contains("IranDirectActivityNames.IpcReceive", content);
    }

    [Fact]
    public void IpcRequestTelemetry_ExactlyOneCounterAndOneHistogram()
    {
        string path = Path.Combine(
            RepoRoot(),
            "PathVeer.Core/Observability/Telemetry/IpcRequestTelemetry.cs");
        string content = File.ReadAllText(path);

        int counters = System.Text.RegularExpressions.Regex.Matches(
            content, @"CreateCounter<long>").Count;
        int histograms = System.Text.RegularExpressions.Regex.Matches(
            content, @"CreateHistogram<double>").Count;

        Assert.Equal(1, counters);  // irandirect.ipc.requests
        Assert.Equal(1, histograms); // irandirect.ipc.request.duration

        Assert.Contains("IranDirectMetricNames.IpcRequests", content);
        Assert.Contains("IranDirectMetricNames.IpcRequestDuration", content);
    }

    [Fact]
    public void IpcRequestTelemetry_OnlyApprovedTags_NoProhibited()
    {
        string path = Path.Combine(
            RepoRoot(),
            "PathVeer.Core/Observability/Telemetry/IpcRequestTelemetry.cs");
        string content = File.ReadAllText(path);

        Assert.Contains("IranDirectTagNames.Operation", content);
        Assert.Contains("IranDirectTagNames.IpcCommand", content);
        Assert.Contains("IranDirectTagNames.Outcome", content);

        foreach (var tag in IranDirectTagNames.Prohibited)
        {
            Assert.DoesNotContain($"\"{tag}\"", content);
        }
    }

    [Fact]
    public void PathVeerServiceClient_OnlyUsesIpcTelemetryHelpers()
    {
        string path = Path.Combine(
            RepoRoot(), "PathVeer.Core/Ipc/PathVeerServiceClient.cs");
        string content = File.ReadAllText(path);

        Assert.Contains("IpcRequestTelemetry.Start(", content);
        Assert.DoesNotContain("StartActivity(", content);
        Assert.DoesNotContain("Meter.Create", content);
        Assert.DoesNotContain("new ActivitySource", content);
    }

    [Fact]
    public void IpcDispatchTelemetry_ExactlyOneLocationInService()
    {
        string root = RepoRoot();
        int coreFiles = 0;
        int serviceFiles = 0;
        int startCalls = 0;

        foreach (var file in Directory.GetFiles(
                     Path.Combine(root, "PathVeer.Core"),
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            string normalized = file.Replace('\\', '/');
            if (normalized.Contains("PathVeer.Core.Tests/"))
                continue;

            string content = File.ReadAllText(file);
            if (content.Contains("IranDirectActivityNames.IpcDispatch") &&
                content.Contains("StartActivity("))
            {
                coreFiles++;
                startCalls += System.Text.RegularExpressions.Regex
                    .Matches(content, @"StartActivity\s*\(").Count;
            }
        }

        foreach (var file in Directory.GetFiles(
                     Path.Combine(root, "PathVeer.Service"),
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            string normalized = file.Replace('\\', '/');
            if (normalized.Contains("PathVeer.Core.Tests/") ||
                normalized.Contains("PathVeer.Service.Tests/"))
                continue;

            string content = File.ReadAllText(file);
            if (content.Contains("IpcDispatchTelemetry.Start"))
            {
                serviceFiles++;
                startCalls += System.Text.RegularExpressions.Regex
                    .Matches(content, @"StartActivity\s*\(").Count;
            }
        }

        Assert.Equal(1, coreFiles);    // IpcDispatchTelemetry.cs (the helper)
        Assert.Equal(1, serviceFiles); // named-pipe server uses it
        Assert.Equal(1, startCalls);   // helper root only (no per-handler spans)
    }

    [Fact]
    public void IpcDispatchTelemetry_UsesApprovedActivityConstants()
    {
        string path = Path.Combine(
            RepoRoot(),
            "PathVeer.Core/Observability/Telemetry/IpcDispatchTelemetry.cs");
        string content = File.ReadAllText(path);

        Assert.Contains("IranDirectActivityNames.IpcDispatch", content);
    }

    [Fact]
    public void SupportExportTelemetry_ExactlyOneRootNoPerZipSpans()
    {
        string root = RepoRoot();
        int files = 0;
        int startCalls = 0;

        foreach (var file in Directory.GetFiles(
                     Path.Combine(root, "PathVeer.Core"),
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            string normalized = file.Replace('\\', '/');
            if (normalized.Contains("PathVeer.Core.Tests/"))
                continue;

            string content = File.ReadAllText(file);
            bool referencesSupport = content.Contains("SupportBundleExport") ||
                content.Contains("SupportCaptureSnapshot") ||
                content.Contains("SupportSerialize") ||
                content.Contains("SupportWriteJson") ||
                content.Contains("SupportCreateZip");
            if (referencesSupport && content.Contains("StartActivity("))
            {
                files++;
                startCalls += System.Text.RegularExpressions.Regex
                    .Matches(content, @"StartActivity\s*\(").Count;
            }
        }

        Assert.Equal(1, files);   // SupportExportTelemetry.cs only
        Assert.Equal(2, startCalls);
    }

    [Fact]
    public void SupportExportTelemetry_UsesApprovedActivityConstants()
    {
        string path = Path.Combine(
            RepoRoot(),
            "PathVeer.Core/Observability/Telemetry/SupportExportTelemetry.cs");
        string content = File.ReadAllText(path);

        Assert.Contains("IranDirectActivityNames.SupportBundleExport", content);
        Assert.Contains("IranDirectActivityNames.SupportCaptureSnapshot", content);
        Assert.Contains("IranDirectActivityNames.SupportSerialize", content);
        Assert.Contains("IranDirectActivityNames.SupportWriteJson", content);
        Assert.Contains("IranDirectActivityNames.SupportCreateZip", content);
    }

    [Fact]
    public void SupportExportTelemetry_ExactlyTwoCountersAndOneHistogram()
    {
        string path = Path.Combine(
            RepoRoot(),
            "PathVeer.Core/Observability/Telemetry/SupportExportTelemetry.cs");
        string content = File.ReadAllText(path);

        int counters = System.Text.RegularExpressions.Regex.Matches(
            content, @"CreateCounter<long>").Count;
        int histograms = System.Text.RegularExpressions.Regex.Matches(
            content, @"CreateHistogram<double>").Count;

        Assert.Equal(2, counters);  // bundles.exported + bundles.failed
        Assert.Equal(1, histograms); // bundle.duration

        Assert.Contains("IranDirectMetricNames.SupportBundlesExported", content);
        Assert.Contains("IranDirectMetricNames.SupportBundlesFailed", content);
        Assert.Contains("IranDirectMetricNames.SupportBundleDuration", content);
    }

    [Fact]
    public void SupportExportTelemetry_OnlyApprovedTags_NoProhibited()
    {
        string path = Path.Combine(
            RepoRoot(),
            "PathVeer.Core/Observability/Telemetry/SupportExportTelemetry.cs");
        string content = File.ReadAllText(path);

        Assert.Contains("IranDirectTagNames.Operation", content);
        Assert.Contains("IranDirectTagNames.Outcome", content);

        foreach (var tag in IranDirectTagNames.Prohibited)
        {
            Assert.DoesNotContain($"\"{tag}\"", content);
        }
    }

    [Fact]
    public void SupportExporters_OnlyUseTelemetryHelpers()
    {
        string snapshotPath = Path.Combine(
            RepoRoot(), "PathVeer.Core/Support/SupportSnapshotExporter.cs");
        string bundlePath = Path.Combine(
            RepoRoot(), "PathVeer.Core/Support/SupportBundleExporter.cs");

        string snapshot = File.ReadAllText(snapshotPath);
        string bundle = File.ReadAllText(bundlePath);

        Assert.Contains("SupportExportTelemetry.Start(", snapshot);
        Assert.Contains("SupportExportTelemetry.Start(", bundle);
        Assert.DoesNotContain("StartActivity(", snapshot);
        Assert.DoesNotContain("StartActivity(", bundle);
        Assert.DoesNotContain("Meter.Create", snapshot);
        Assert.DoesNotContain("Meter.Create", bundle);
        Assert.DoesNotContain("new ActivitySource", snapshot);
        Assert.DoesNotContain("new ActivitySource", bundle);
        // Orchestration of the nested snapshot export must be explicit
        // (ExportWithinBundleAsync), never derived from ambient telemetry
        // state such as Activity.Current. Doc comments may mention the term,
        // but no executable reference (Activity.Current?. / Activity.Current.)
        // may appear in the exporters.
        Assert.DoesNotContain("Activity.Current?", snapshot);
        Assert.DoesNotContain("Activity.Current.", snapshot);
        Assert.DoesNotContain("Activity.Current?", bundle);
        Assert.DoesNotContain("Activity.Current.", bundle);
    }

    [Fact]
    public void NamedPipeCommandServer_OnlyUsesDispatchHelper()
    {
        string path = Path.Combine(
            RepoRoot(), "PathVeer.Service/Ipc/NamedPipeCommandServer.cs");
        string content = File.ReadAllText(path);

        Assert.Contains("IpcDispatchTelemetry.Start(", content);
        Assert.DoesNotContain("StartActivity(IranDirectActivityNames.", content);
        Assert.DoesNotContain("Meter.Create", content);
        Assert.DoesNotContain("new ActivitySource", content);
    }

    [Fact]
    public void NoNewTelemetryActivityOrMetricNameConstants_ForIpcSupport()
    {
        string activityNames = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "PathVeer.Core/Observability/Telemetry/IranDirectActivityNames.cs"));
        string metricNames = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "PathVeer.Core/Observability/Telemetry/IranDirectMetricNames.cs"));

        Assert.Contains("IpcRequest", activityNames);
        Assert.Contains("IpcConnect", activityNames);
        Assert.Contains("IpcSend", activityNames);
        Assert.Contains("IpcReceive", activityNames);
        Assert.Contains("IpcDispatch", activityNames);
        Assert.Contains("SupportBundleExport", activityNames);
        Assert.Contains("SupportCaptureSnapshot", activityNames);
        Assert.Contains("SupportSerialize", activityNames);
        Assert.Contains("SupportWriteJson", activityNames);
        Assert.Contains("SupportCreateZip", activityNames);

        Assert.Contains("IpcRequests", metricNames);
        Assert.Contains("IpcRequestDuration", metricNames);
        Assert.Contains("SupportBundlesExported", metricNames);
        Assert.Contains("SupportBundlesFailed", metricNames);
        Assert.Contains("SupportBundleDuration", metricNames);
    }

    [Fact]
    public void OpenTelemetryPackages_OnlyInServiceAndServiceTests()
    {
        // OpenTelemetry package references must be confined to the Service
        // host and its test project. Core, CLI, Tray, Benchmarks, Testing must
        // remain OpenTelemetry-free so the Core telemetry contracts stay BCL-only.
        string[] allowed = new[]
        {
            "PathVeer.Service/PathVeer.Service.csproj",
            "PathVeer.Service.Tests/PathVeer.Service.Tests.csproj",
        };
        string[] forbidden = new[]
        {
            "PathVeer.Core/PathVeer.Core.csproj",
            "PathVeer.Cli/PathVeer.Cli.csproj",
            "PathVeer.Tray/PathVeer.Tray.csproj",
            "PathVeer.Benchmarks/PathVeer.Benchmarks.csproj",
            "PathVeer.Testing/PathVeer.Testing.csproj",
        };

        string otelMarker = "OpenTelemetry";

        foreach (string proj in allowed)
        {
            string content = File.ReadAllText(
                Path.Combine(RepoRoot(), proj));
            Assert.True(
                content.Contains(otelMarker),
                $"{proj} should reference OpenTelemetry packages");
        }

        foreach (string proj in forbidden)
        {
            string content = File.ReadAllText(
                Path.Combine(RepoRoot(), proj));
            Assert.False(
                content.Contains("OpenTelemetry"),
                $"{proj} must not reference OpenTelemetry packages");
        }
    }

    [Fact]
    public void OpenTelemetry_NoAutomaticInstrumentationPackages()
    {
        // No auto-instrumentation (HTTP, runtime, process, SQL, ASP.NET) may
        // be introduced. Only the minimal hosting/export packages are allowed.
        string serviceCsproj = File.ReadAllText(Path.Combine(
            RepoRoot(), "PathVeer.Service/PathVeer.Service.csproj"));
        string serviceTestsCsproj = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "PathVeer.Service.Tests/PathVeer.Service.Tests.csproj"));

        string combined = serviceCsproj + "\n" + serviceTestsCsproj;

        Assert.Contains(
            "OpenTelemetry.Extensions.Hosting", combined);
        Assert.Contains(
            "OpenTelemetry.Exporter.OpenTelemetryProtocol", combined);

        Assert.DoesNotContain(
            "OpenTelemetry.Instrumentation", combined);
        Assert.DoesNotContain(
            "OpenTelemetry.Exporter.Prometheus", combined);
    }

    [Fact]
    public void CoreTelemetryContracts_RemainBclOnly()
    {
        // IranDirectTelemetry and the telemetry helpers must not reference
        // OpenTelemetry exporter/provider/sampler namespaces.
        string[] coreFiles =
        {
            "PathVeer.Core/Observability/Telemetry/IranDirectTelemetry.cs",
            "PathVeer.Core/Observability/Telemetry/IranDirectActivityNames.cs",
            "PathVeer.Core/Observability/Telemetry/IranDirectMetricNames.cs",
        };

        foreach (string file in coreFiles)
        {
            string content = File.ReadAllText(
                Path.Combine(RepoRoot(), file));
            Assert.DoesNotContain(
                "OpenTelemetry.Exporter", content);
            Assert.DoesNotContain(
                "OpenTelemetry.Trace", content);
            Assert.DoesNotContain(
                "OpenTelemetry.Metrics", content);
            Assert.DoesNotContain(
                "TracerProvider", content);
            Assert.DoesNotContain(
                "MeterProvider", content);
            Assert.DoesNotContain(
                "Sampler", content);
        }
    }

    [Fact]
    public void ObservabilityHosting_ConfinedToService()
    {
        // The OpenTelemetry registration must live only under
        // PathVeer.Service/Observability. Core business namespaces must not
        // register providers or exporters.
        string serviceDir = Path.Combine(
            RepoRoot(), "PathVeer.Service/Observability");
        Assert.True(Directory.Exists(serviceDir));

        string[] registrationFiles = Directory.GetFiles(
            serviceDir, "*.cs", SearchOption.AllDirectories);
        bool hasRegistration = false;
        foreach (string file in registrationFiles)
        {
            string content = File.ReadAllText(file);
            if (content.Contains("AddOpenTelemetry") ||
                content.Contains("AddOtlpExporter") ||
                content.Contains("AddConsoleExporter"))
            {
                hasRegistration = true;
            }
        }
        Assert.True(hasRegistration);

        // Core business (planner/executor/routing/prefix/dns/ipc/support) must
        // not reference OTel hosting.
        string[] coreNamespaces = new[]
        {
            "PathVeer.Core/Runtime",
            "PathVeer.Core/Routing",
            "PathVeer.Core/Prefixes",
            "PathVeer.Core/Networking",
            "PathVeer.Core/Ipc",
            "PathVeer.Core/Support",
            "PathVeer.Core/CustomRoutes",
        };
        foreach (string ns in coreNamespaces)
        {
            string dir = Path.Combine(RepoRoot(), ns);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (string file in Directory.GetFiles(
                dir, "*.cs", SearchOption.AllDirectories))
            {
                string content = File.ReadAllText(file);
                Assert.DoesNotContain(
                    "OpenTelemetry.Extensions.Hosting", content);
                Assert.DoesNotContain(
                    "AddOpenTelemetry", content);
                Assert.DoesNotContain(
                    "OtlpExporter", content);
            }
        }
    }

    [Fact]
    public void Appsettings_ContainNoSecrets()
    {
        // Committed configuration must not carry OTLP headers/tokens or other
        // secrets. Headers are loaded from environment variables only.
        string[] appsettings =
        {
            "PathVeer.Service/appsettings.json",
            "PathVeer.Service/appsettings.Development.json",
        };
        string[] secretKeys = new[]
        {
            "Token", "Secret", "Password", "ApiKey",
            "Authorization", "Bearer",
        };

        foreach (string file in appsettings)
        {
            string content = File.ReadAllText(
                Path.Combine(RepoRoot(), file));
            // The OTLP headers ENVIRONMENT VARIABLE NAME is a config key with an
            // empty value; the actual secret value is supplied via the env var,
            // never committed. Only the secret material (keys/values below) is
            // forbidden.
            foreach (string key in secretKeys)
            {
                Assert.False(
                    content.Contains(key),
                    $"{file} must not contain secret key '{key}'");
            }
            // OTLP endpoint must be blank by default.
            Assert.DoesNotContain(
                "4317", content);
            Assert.DoesNotContain(
                "4318", content);
            Assert.DoesNotContain(
                "http://", content);
            Assert.DoesNotContain(
                "https://", content);
        }
    }

    [Fact]
    public void ObservabilityResource_HasNoForbiddenAttributes()
    {
        string content = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "PathVeer.Service/Observability/ObservabilityResourceBuilder.cs"));

        string[] forbidden = new[]
        {
            "machine.name", "host.name", "host.id", "host.arch",
            "service.instance.id", "process.command_line",
            "process.executable.path", "cloud.account.id",
            "cloud.region", "cloud.availability_zone",
            "user.name", "local.ip",
        };
        foreach (string attr in forbidden)
        {
            Assert.DoesNotContain(attr, content);
        }
    }

    [Fact]
    public void ServiceRegistration_ExactlyOneExtension()
    {
        string content = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "PathVeer.Service/Observability/" +
            "ObservabilityServiceCollectionExtensions.cs"));

        int addMethods = 0;
        foreach (Match m in Regex.Matches(
            content, @"public static IServiceCollection AddIranDirectObservability\("))
        {
            addMethods++;
        }
        Assert.Equal(1, addMethods);
        Assert.Contains(
            "AddIranDirectObservability",
            File.ReadAllText(Path.Combine(
                RepoRoot(), "PathVeer.Service/Program.cs")));
    }

    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "PathVeer.slnx")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repo root not found");
    }
}
