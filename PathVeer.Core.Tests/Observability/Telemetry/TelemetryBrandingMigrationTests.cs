using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Reflection;
using PathVeer.Core.Observability.Telemetry;
using Xunit;

namespace PathVeer.Core.Tests.Observability.Telemetry;

/// <summary>
/// Phase 36.6 telemetry-identity migration proof.
///
/// The product rebrand performs a HARD CUTOVER of the telemetry identity:
/// the single ActivitySource/Meter is <c>PathVeer.Core</c>, the OTel resource
/// service.name is <c>PathVeer.Service</c>, and every production metric
/// instrument name carries the <c>pathveer.</c> prefix. No legacy
/// <c>IranDirect.Core</c> source and no <c>irandirect.</c> instrument is
/// published in parallel — old Prometheus/Tempo series remain historical only.
/// </summary>
public sealed class TelemetryBrandingMigrationTests
{
    private const string LegacySourceName = "IranDirect.Core";
    private const string LegacyMetricPrefix = "irandirect.";

    [Fact]
    public void SourceAndMeter_AreExactlyPathVeerCore()
    {
        Assert.Equal("PathVeer.Core", PathVeerTelemetry.SourceName);
        Assert.Equal("PathVeer.Core", PathVeerTelemetry.ActivitySource.Name);
        Assert.Equal("PathVeer.Core", PathVeerTelemetry.Meter.Name);
    }

    [Fact]
    public void NoLegacyIranDirectSourceIdentity_Remains()
    {
        Assert.NotEqual(LegacySourceName, PathVeerTelemetry.SourceName);
        Assert.NotEqual(LegacySourceName, PathVeerTelemetry.ActivitySource.Name);
        Assert.NotEqual(LegacySourceName, PathVeerTelemetry.Meter.Name);
    }

    [Fact]
    public void ExactlyOneActivitySourceAndOneMeter_AreDefined()
    {
        PropertyInfo[] sources = typeof(PathVeerTelemetry)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(ActivitySource))
            .ToArray();
        PropertyInfo[] meters = typeof(PathVeerTelemetry)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(Meter))
            .ToArray();

        Assert.Single(sources);
        Assert.Single(meters);
        Assert.Same(
            PathVeerTelemetry.ActivitySource,
            PathVeerTelemetry.ActivitySource);
        Assert.Same(PathVeerTelemetry.Meter, PathVeerTelemetry.Meter);
    }

    [Fact]
    public void EveryMetricName_UsesPathVeerPrefix()
    {
        foreach (string name in MetricNames())
            Assert.StartsWith("pathveer.", name, System.StringComparison.Ordinal);
    }

    [Fact]
    public void NoMetricName_UsesLegacyIranDirectPrefix()
    {
        foreach (string name in MetricNames())
            Assert.DoesNotContain(LegacyMetricPrefix, name, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MetricNames_AreUnique_NoDualPublication()
    {
        string[] names = MetricNames().ToArray();
        Assert.Equal(names.Length, names.Distinct().Count());
    }

    [Fact]
    public void BrandedSpanNames_UsePathVeer_AndNeutralSpansUnchanged()
    {
        Assert.Equal("PathVeer.RuntimeCycle", PathVeerActivityNames.RuntimeCycle);
        Assert.Equal("PathVeer.PrefixUpdateCheck", PathVeerActivityNames.PrefixUpdateCheck);
        Assert.Equal("PathVeer.CustomRouteRefresh", PathVeerActivityNames.CustomRouteRefresh);
        Assert.Equal("PathVeer.IpcRequest", PathVeerActivityNames.IpcRequest);
        Assert.Equal("PathVeer.SupportBundleExport", PathVeerActivityNames.SupportBundleExport);

        // Brand-neutral semantic operation spans are NOT renamed.
        Assert.Equal("Runtime.PlanChanges", PathVeerActivityNames.RuntimePlanChanges);
        Assert.Equal("Runtime.Execute", PathVeerActivityNames.RuntimeExecute);
        Assert.Equal("Prefix.HttpHead", PathVeerActivityNames.PrefixHttpHead);
        Assert.Equal("Dns.Resolve", PathVeerActivityNames.DnsResolve);
        Assert.Equal("Ipc.Connect", PathVeerActivityNames.IpcConnect);
        Assert.Equal("Support.WriteJson", PathVeerActivityNames.SupportWriteJson);
    }

    [Fact]
    public void NoSpanName_ContainsLegacyBranding()
    {
        foreach (string name in Literals(typeof(PathVeerActivityNames)))
            Assert.DoesNotContain("IranDirect", name, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TagContract_IsUnchangedByTheRename()
    {
        // Semantic tag keys are brand-neutral and must survive the rebrand.
        Assert.Equal("operation", PathVeerTagNames.Operation);
        Assert.Equal("outcome", PathVeerTagNames.Outcome);
        Assert.Equal("failure_category", PathVeerTagNames.FailureCategory);
        Assert.Equal("trigger", PathVeerTagNames.Trigger);
        Assert.Equal("ipc_command", PathVeerTagNames.IpcCommand);
        Assert.Equal("cache_state", PathVeerTagNames.CacheState);

        foreach (string tag in Literals(typeof(PathVeerTagNames)))
            Assert.DoesNotContain("pathveer", tag, System.StringComparison.OrdinalIgnoreCase);
        foreach (string value in Literals(typeof(PathVeerTagValues)))
            Assert.DoesNotContain("pathveer", value, System.StringComparison.OrdinalIgnoreCase);

        // Privacy / cardinality prohibitions remain intact.
        Assert.Contains("destination_prefix", PathVeerTagNames.Prohibited);
        Assert.Contains("gateway", PathVeerTagNames.Prohibited);
        Assert.Contains("domain", PathVeerTagNames.Prohibited);
        Assert.Contains("exception_message", PathVeerTagNames.Prohibited);
        Assert.Contains("url", PathVeerTagNames.Prohibited);
        Assert.Contains("route_identity", PathVeerTagNames.Prohibited);
    }

    private static IEnumerable<string> MetricNames() =>
        Literals(typeof(PathVeerMetricNames));

    private static IEnumerable<string> Literals(System.Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly &&
                        f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);
}
