using System.Linq;
using System.Reflection;
using PathVeer.Core.Observability.Telemetry;
using Xunit;

namespace PathVeer.Core.Tests.Observability.Telemetry;

public sealed class TelemetryNameCatalogTests
{
    private static readonly IReadOnlyDictionary<string, string> ExpectedSpans =
        new Dictionary<string, string>
        {
            ["RuntimeCycle"] = "PathVeer.RuntimeCycle",
            ["PrefixUpdateCheck"] = "PathVeer.PrefixUpdateCheck",
            ["CustomRouteRefresh"] = "PathVeer.CustomRouteRefresh",
            ["IpcRequest"] = "PathVeer.IpcRequest",
            ["SupportBundleExport"] = "PathVeer.SupportBundleExport",
            ["RuntimeObserve"] = "Runtime.Observe",
            ["RuntimeBuildDecision"] = "Runtime.BuildDecision",
            ["RuntimeBuildPreview"] = "Runtime.BuildPreview",
            ["RuntimePlanChanges"] = "Runtime.PlanChanges",
            ["RuntimeExecute"] = "Runtime.Execute",
            ["RuntimePersistInventory"] = "Runtime.PersistInventory",
            ["RoutesEnumerate"] = "Routes.Enumerate",
            ["RoutesCreate"] = "Routes.Create",
            ["RoutesDelete"] = "Routes.Delete",
            ["PrefixHttpHead"] = "Prefix.HttpHead",
            ["PrefixHttpGet"] = "Prefix.HttpGet",
            ["PrefixCompare"] = "Prefix.Compare",
            ["PrefixPersistMetadata"] = "Prefix.PersistMetadata",
            ["DnsCacheRead"] = "Dns.CacheRead",
            ["DnsResolve"] = "Dns.Resolve",
            ["DnsCacheWrite"] = "Dns.CacheWrite",
            ["IpcConnect"] = "Ipc.Connect",
            ["IpcSend"] = "Ipc.Send",
            ["IpcDispatch"] = "Ipc.Dispatch",
            ["IpcReceive"] = "Ipc.Receive",
            ["SupportCaptureSnapshot"] = "Support.CaptureSnapshot",
            ["SupportSerialize"] = "Support.Serialize",
            ["SupportWriteJson"] = "Support.WriteJson",
            ["SupportCreateZip"] = "Support.CreateZip",
        };

    [Fact]
    public void EveryApprovedSpanName_ExactAndNoDuplicates()
    {
        var type = typeof(PathVeerActivityNames);
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly).ToArray();

        var seen = new HashSet<string>();
        foreach (var kvp in ExpectedSpans)
        {
            var field = type.GetField(kvp.Key);
            Assert.NotNull(field);
            Assert.Equal(kvp.Value, (string)field!.GetValue(null)!);
            Assert.True(seen.Add(kvp.Value), $"duplicate span name {kvp.Value}");
        }

        // No names beyond the approved set.
        Assert.Equal(ExpectedSpans.Count, fields.Length);
    }

    [Fact]
    public void SpanNames_ContainNoForbiddenDataPatterns()
    {
        foreach (var name in ExpectedSpans.Values)
        {
            Assert.DoesNotContain("|", name);
            Assert.DoesNotContain("/", name, StringComparison.Ordinal);
            Assert.DoesNotContain("::", name);
            // Root spans carry the IranDirect. prefix; child spans a dotted
            // category. None embed an identity or prefix.
            Assert.False(name.Contains('{'));
            Assert.False(name.Contains('}'));
        }
    }

    [Fact]
    public void MetricNames_ExactAndNoDuplicates()
    {
        var type = typeof(PathVeerMetricNames);
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly).ToArray();

        var expected = typeof(TelemetryNameCatalogTests)
            .GetMethod(nameof(MetricNameSets))!;
        var sets = MetricNameSets();

        var seen = new HashSet<string>();
        foreach (var kvp in sets)
        {
            var field = type.GetField(kvp.Key);
            Assert.NotNull(field);
            Assert.Equal(kvp.Value, (string)field!.GetValue(null)!);
            Assert.True(seen.Add(kvp.Value), $"duplicate metric name {kvp.Value}");
        }

        Assert.Equal(sets.Count, fields.Length);
    }

    [Fact]
    public void MetricNames_ShareIrandirectPrefix_AndDurationSuffixWhereApplicable()
    {
        foreach (var name in MetricNameSets().Values)
        {
            Assert.StartsWith("pathveer.", name);
            Assert.DoesNotContain("destination_prefix", name);
            Assert.DoesNotContain("gateway", name);
        }

        Assert.EndsWith(".duration", PathVeerMetricNames.RuntimeCycleDuration);
        Assert.EndsWith(".duration", PathVeerMetricNames.RuntimeObserveDuration);
        Assert.EndsWith(".duration", PathVeerMetricNames.RuntimePlanningDuration);
        Assert.EndsWith(".duration", PathVeerMetricNames.RuntimeExecutionDuration);
        Assert.EndsWith(".duration", PathVeerMetricNames.RoutesSystemCallDuration);
        Assert.EndsWith(".duration", PathVeerMetricNames.PrefixCheckDuration);
        Assert.EndsWith(".duration", PathVeerMetricNames.DnsLookupDuration);
        Assert.EndsWith(".duration", PathVeerMetricNames.IpcRequestDuration);
        Assert.EndsWith(".duration", PathVeerMetricNames.SupportBundleDuration);
    }

    private static IReadOnlyDictionary<string, string> MetricNameSets() => new Dictionary<string, string>
    {
        ["RuntimeCyclesStarted"] = "pathveer.runtime.cycles.started",
        ["RuntimeCyclesCompleted"] = "pathveer.runtime.cycles.completed",
        ["RuntimeCyclesFailed"] = "pathveer.runtime.cycles.failed",
        ["RuntimeCyclesCancelled"] = "pathveer.runtime.cycles.cancelled",
        ["RuntimeRepairsWithChanges"] = "pathveer.runtime.repairs.with_changes",
        ["RuntimeRepairsNoChanges"] = "pathveer.runtime.repairs.no_changes",
        ["RoutesOperationsRequested"] = "pathveer.routes.operations.requested",
        ["RoutesOperationsSucceeded"] = "pathveer.routes.operations.succeeded",
        ["RoutesOperationsFailed"] = "pathveer.routes.operations.failed",
        ["PrefixChecks"] = "pathveer.prefix.checks",
        ["DnsLookups"] = "pathveer.dns.lookups",
        ["IpcRequests"] = "pathveer.ipc.requests",
        ["SupportBundlesExported"] = "pathveer.support.bundles.exported",
        ["SupportBundlesFailed"] = "pathveer.support.bundles.failed",
        ["RuntimeCycleDuration"] = "pathveer.runtime.cycle.duration",
        ["RuntimeObserveDuration"] = "pathveer.runtime.observe.duration",
        ["RuntimePlanningDuration"] = "pathveer.runtime.planning.duration",
        ["RuntimeExecutionDuration"] = "pathveer.runtime.execution.duration",
        ["RoutesSystemCallDuration"] = "pathveer.routes.system_call.duration",
        ["PrefixCheckDuration"] = "pathveer.prefix.check.duration",
        ["DnsLookupDuration"] = "pathveer.dns.lookup.duration",
        ["IpcRequestDuration"] = "pathveer.ipc.request.duration",
        ["SupportBundleDuration"] = "pathveer.support.bundle.duration",
        ["RuntimeOperationsPerCycle"] = "pathveer.runtime.operations.per_cycle",
        ["RuntimeChangedRoutes"] = "pathveer.runtime.changed_routes",
        ["ServiceEnabled"] = "pathveer.service.enabled",
        ["RuntimeWorkerActive"] = "pathveer.runtime.worker.active",
        ["PrefixKnownCount"] = "pathveer.prefix.known_count",
        ["RoutesInventoryCount"] = "pathveer.routes.inventory_count",
        ["DnsCacheRecordCount"] = "pathveer.dns.cache_record_count",
        ["PrefixConsecutiveFailures"] = "pathveer.prefix.consecutive_failures",
    };
}
