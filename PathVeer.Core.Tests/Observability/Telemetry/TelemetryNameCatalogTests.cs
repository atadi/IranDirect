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
            ["RuntimeCycle"] = "IranDirect.RuntimeCycle",
            ["PrefixUpdateCheck"] = "IranDirect.PrefixUpdateCheck",
            ["CustomRouteRefresh"] = "IranDirect.CustomRouteRefresh",
            ["IpcRequest"] = "IranDirect.IpcRequest",
            ["SupportBundleExport"] = "IranDirect.SupportBundleExport",
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
        var type = typeof(IranDirectActivityNames);
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
        var type = typeof(IranDirectMetricNames);
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
            Assert.StartsWith("irandirect.", name);
            Assert.DoesNotContain("destination_prefix", name);
            Assert.DoesNotContain("gateway", name);
        }

        Assert.EndsWith(".duration", IranDirectMetricNames.RuntimeCycleDuration);
        Assert.EndsWith(".duration", IranDirectMetricNames.RuntimeObserveDuration);
        Assert.EndsWith(".duration", IranDirectMetricNames.RuntimePlanningDuration);
        Assert.EndsWith(".duration", IranDirectMetricNames.RuntimeExecutionDuration);
        Assert.EndsWith(".duration", IranDirectMetricNames.RoutesSystemCallDuration);
        Assert.EndsWith(".duration", IranDirectMetricNames.PrefixCheckDuration);
        Assert.EndsWith(".duration", IranDirectMetricNames.DnsLookupDuration);
        Assert.EndsWith(".duration", IranDirectMetricNames.IpcRequestDuration);
        Assert.EndsWith(".duration", IranDirectMetricNames.SupportBundleDuration);
    }

    private static IReadOnlyDictionary<string, string> MetricNameSets() => new Dictionary<string, string>
    {
        ["RuntimeCyclesStarted"] = "irandirect.runtime.cycles.started",
        ["RuntimeCyclesCompleted"] = "irandirect.runtime.cycles.completed",
        ["RuntimeCyclesFailed"] = "irandirect.runtime.cycles.failed",
        ["RuntimeCyclesCancelled"] = "irandirect.runtime.cycles.cancelled",
        ["RuntimeRepairsWithChanges"] = "irandirect.runtime.repairs.with_changes",
        ["RuntimeRepairsNoChanges"] = "irandirect.runtime.repairs.no_changes",
        ["RoutesOperationsRequested"] = "irandirect.routes.operations.requested",
        ["RoutesOperationsSucceeded"] = "irandirect.routes.operations.succeeded",
        ["RoutesOperationsFailed"] = "irandirect.routes.operations.failed",
        ["PrefixChecks"] = "irandirect.prefix.checks",
        ["DnsLookups"] = "irandirect.dns.lookups",
        ["IpcRequests"] = "irandirect.ipc.requests",
        ["SupportBundlesExported"] = "irandirect.support.bundles.exported",
        ["SupportBundlesFailed"] = "irandirect.support.bundles.failed",
        ["RuntimeCycleDuration"] = "irandirect.runtime.cycle.duration",
        ["RuntimeObserveDuration"] = "irandirect.runtime.observe.duration",
        ["RuntimePlanningDuration"] = "irandirect.runtime.planning.duration",
        ["RuntimeExecutionDuration"] = "irandirect.runtime.execution.duration",
        ["RoutesSystemCallDuration"] = "irandirect.routes.system_call.duration",
        ["PrefixCheckDuration"] = "irandirect.prefix.check.duration",
        ["DnsLookupDuration"] = "irandirect.dns.lookup.duration",
        ["IpcRequestDuration"] = "irandirect.ipc.request.duration",
        ["SupportBundleDuration"] = "irandirect.support.bundle.duration",
        ["RuntimeOperationsPerCycle"] = "irandirect.runtime.operations.per_cycle",
        ["RuntimeChangedRoutes"] = "irandirect.runtime.changed_routes",
        ["ServiceEnabled"] = "irandirect.service.enabled",
        ["RuntimeWorkerActive"] = "irandirect.runtime.worker.active",
        ["PrefixKnownCount"] = "irandirect.prefix.known_count",
        ["RoutesInventoryCount"] = "irandirect.routes.inventory_count",
        ["DnsCacheRecordCount"] = "irandirect.dns.cache_record_count",
        ["PrefixConsecutiveFailures"] = "irandirect.prefix.consecutive_failures",
    };
}
