using System.Text.Json;
using IranDirect.Core.Configuration;
using IranDirect.Core.CustomRoutes;
using IranDirect.Core.Diagnostics;
using IranDirect.Core.Observability;
using IranDirect.Core.Planning;
using IranDirect.Core.Prefixes;
using IranDirect.Core.Runtime.Profiling;
using IranDirect.Core.Support;

namespace IranDirect.Core.Tests.Support;

public sealed class SupportSnapshotSerializerTests
{
    private static readonly DateTimeOffset FixedTime =
        new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly SupportSnapshotSerializer _serializer = new();

    [Fact]
    public void Serialize_NullSnapshot_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => _serializer.Serialize(null!));
    }

    [Fact]
    public void Serialize_MinimalSnapshot_Deterministic()
    {
        SupportSnapshot snapshot = Build(
            runtime: null,
            diagnostics: null,
            preview: null,
            configuration: null,
            prefixMetadata: null,
            history: [],
            dnsCache: [],
            performance: null);

        string first = _serializer.Serialize(snapshot);
        string second = _serializer.Serialize(snapshot);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Serialize_PopulatedSnapshot_RepeatedIdentical()
    {
        SupportSnapshot snapshot = BuildPopulated();

        string first = _serializer.Serialize(snapshot);
        string second = _serializer.Serialize(snapshot);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Serialize_OutputIsValidJson()
    {
        SupportSnapshot snapshot = BuildPopulated();

        string output = _serializer.Serialize(snapshot);

        using JsonDocument document = JsonDocument.Parse(output);

        Assert.Equal(
            JsonValueKind.Object,
            document.RootElement.ValueKind);
    }

    [Fact]
    public void Serialize_EmptyCollections_PreservedAsEmpty()
    {
        SupportSnapshot snapshot = Build(
            runtime: null,
            diagnostics: null,
            preview: null,
            configuration: null,
            prefixMetadata: null,
            history: [],
            dnsCache: [],
            performance: null);

        string output = _serializer.Serialize(snapshot);

        JsonElement root =
            JsonDocument.Parse(output).RootElement;

        Assert.Equal(
            JsonValueKind.Array,
            root.GetProperty("PrefixHistory").ValueKind);
        Assert.Equal(
            0,
            root.GetProperty("PrefixHistory").GetArrayLength());

        Assert.Equal(
            JsonValueKind.Array,
            root.GetProperty("DnsCache").ValueKind);
        Assert.Equal(
            0,
            root.GetProperty("DnsCache").GetArrayLength());
    }

    [Fact]
    public void Serialize_NullableProperties_PreservedAsNull()
    {
        SupportSnapshot snapshot = Build(
            runtime: null,
            diagnostics: null,
            preview: null,
            configuration: null,
            prefixMetadata: null,
            history: [],
            dnsCache: [],
            performance: null);

        string output = _serializer.Serialize(snapshot);

        JsonElement root =
            JsonDocument.Parse(output).RootElement;

        Assert.Equal(
            JsonValueKind.Null,
            root.GetProperty("Runtime").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            root.GetProperty("Diagnostics").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            root.GetProperty("ExecutionPreview").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            root.GetProperty("Configuration").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            root.GetProperty("PrefixMetadata").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            root.GetProperty("Performance").ValueKind);
    }

    [Fact]
    public void Serialize_CapturedAtIso8601()
    {
        SupportSnapshot snapshot = Build(
            runtime: null,
            diagnostics: null,
            preview: null,
            configuration: null,
            prefixMetadata: null,
            history: [],
            dnsCache: [],
            performance: null);

        string output = _serializer.Serialize(snapshot);

        JsonElement root =
            JsonDocument.Parse(output).RootElement;

        DateTimeOffset capturedAt =
            root.GetProperty("CapturedAt").GetDateTimeOffset();

        Assert.Equal(
            FixedTime.UtcDateTime,
            capturedAt.UtcDateTime,
            precision: TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Serialize_PropertyOrderMatchesDeclaration()
    {
        SupportSnapshot snapshot = Build(
            runtime: null,
            diagnostics: null,
            preview: null,
            configuration: null,
            prefixMetadata: null,
            history: [],
            dnsCache: [],
            performance: null);

        string output = _serializer.Serialize(snapshot);

        JsonElement root =
            JsonDocument.Parse(output).RootElement;

        List<string> names = root.EnumerateObject()
            .Select(p => p.Name)
            .ToList();

        int capturedAt = names.IndexOf("CapturedAt");
        int runtime = names.IndexOf("Runtime");
        int diagnostics = names.IndexOf("Diagnostics");
        int preview = names.IndexOf("ExecutionPreview");
        int config = names.IndexOf("Configuration");
        int prefixMeta = names.IndexOf("PrefixMetadata");
        int prefixHist = names.IndexOf("PrefixHistory");
        int dns = names.IndexOf("DnsCache");
        int perf = names.IndexOf("Performance");
        int summary = names.IndexOf("Summary");

        Assert.True(capturedAt >= 0);
        Assert.True(runtime > capturedAt);
        Assert.True(diagnostics > runtime);
        Assert.True(preview > diagnostics);
        Assert.True(config > preview);
        Assert.True(prefixMeta > config);
        Assert.True(prefixHist > prefixMeta);
        Assert.True(dns > prefixHist);
        Assert.True(perf > dns);
        Assert.True(summary > perf);
    }

    [Fact]
    public void Serialize_RuntimeSnapshotPresent()
    {
        RuntimeSnapshot runtime = new()
        {
            CapturedAt = FixedTime,
            PrefixCount = 12
        };

        SupportSnapshot snapshot = Build(
            runtime,
            diagnostics: null,
            preview: null,
            configuration: null,
            prefixMetadata: null,
            history: [],
            dnsCache: [],
            performance: null);

        string output = _serializer.Serialize(snapshot);

        JsonElement root =
            JsonDocument.Parse(output).RootElement;

        JsonElement runtimeProperty =
            root.GetProperty("Runtime");

        Assert.NotEqual(
            JsonValueKind.Null,
            runtimeProperty.ValueKind);

        Assert.Equal(
            12,
            runtimeProperty
                .GetProperty("PrefixCount")
                .GetInt32());
    }

    [Fact]
    public void Serialize_DiagnosticsPresent()
    {
        DiagnosticReport report = new(
            FixedTime,
            [
                new DiagnosticResult(
                    Id: "p1",
                    Title: "Pass 1",
                    Status: DiagnosticStatus.Passed,
                    Severity: DiagnosticSeverity.Pass,
                    Message: "ok",
                    SuggestedAction: null)
            ]);

        SupportSnapshot snapshot = Build(
            runtime: null,
            diagnostics: report,
            preview: null,
            configuration: null,
            prefixMetadata: null,
            history: [],
            dnsCache: [],
            performance: null);

        string output = _serializer.Serialize(snapshot);

        JsonElement root =
            JsonDocument.Parse(output).RootElement;

        JsonElement diagnosticsProperty =
            root.GetProperty("Diagnostics");

        Assert.NotEqual(
            JsonValueKind.Null,
            diagnosticsProperty.ValueKind);
        Assert.Equal(
            1,
            diagnosticsProperty
                .GetProperty("PassedCount")
                .GetInt32());
    }

    [Fact]
    public void Serialize_ExecutionPreviewPresent()
    {
        ExecutionPreview preview = new()
        {
            CapturedAt = FixedTime,
            Summary = new ExecutionPreviewSummary
            {
                CreateCount = 2,
                DeleteCount = 1,
                VerifyCount = 0,
                InventoryUpdates = 2,
                CustomRouteUpdates = 0,
                VpnEndpointUpdates = 1
            },
            Steps =
            [
                new ExecutionPreviewStep
                {
                    Category = ExecutionPreviewCategory.Route,
                    Operation =
                        ExecutionPreviewOperation.Create,
                    Target = "10.0.0.0/24",
                    Reason = "missing"
                }
            ]
        };

        SupportSnapshot snapshot = Build(
            runtime: null,
            diagnostics: null,
            preview,
            configuration: null,
            prefixMetadata: null,
            history: [],
            dnsCache: [],
            performance: null);

        string output = _serializer.Serialize(snapshot);

        JsonElement root =
            JsonDocument.Parse(output).RootElement;

        JsonElement previewProperty =
            root.GetProperty("ExecutionPreview");

        Assert.NotEqual(
            JsonValueKind.Null,
            previewProperty.ValueKind);
        Assert.True(previewProperty
            .GetProperty("HasChanges")
            .GetBoolean());
        Assert.Equal(
            1,
            previewProperty
                .GetProperty("Steps")
                .GetArrayLength());
    }

    [Fact]
    public void Serialize_PrefixHistoryPresent()
    {
        PrefixSourceUpdateHistoryEntry entry = new()
        {
            SourceId = "src1",
            SourceDisplayName = "Source 1",
            Format = "txt",
            ParserVersion = "1",
            Status = PrefixSourceUpdateStatus.Succeeded,
            StartedAt = FixedTime,
            CompletedAt = FixedTime,
            AttemptedAt = FixedTime,
            PrefixCount = 50,
            AddedCount = 5,
            RemovedCount = 0,
            UnchangedCount = 45,
            HasChanges = true
        };

        SupportSnapshot snapshot = Build(
            runtime: null,
            diagnostics: null,
            preview: null,
            configuration: null,
            prefixMetadata: null,
            history: [entry],
            dnsCache: [],
            performance: null);

        string output = _serializer.Serialize(snapshot);

        JsonElement root =
            JsonDocument.Parse(output).RootElement;

        JsonElement historyProperty =
            root.GetProperty("PrefixHistory");

        Assert.Equal(1, historyProperty.GetArrayLength());
        Assert.Equal(
            "src1",
            historyProperty[0]
                .GetProperty("SourceId")
                .GetString());
    }

    [Fact]
    public void Serialize_DnsCachePresent()
    {
        CustomRouteDnsCacheStatus status = new()
        {
            CustomRouteEntryId = Guid.NewGuid(),
            Domain = "example.com",
            Enabled = true,
            State = CustomRouteDnsCacheState.Fresh,
            IPv4Addresses = ["1.2.3.4"]
        };

        SupportSnapshot snapshot = Build(
            runtime: null,
            diagnostics: null,
            preview: null,
            configuration: null,
            prefixMetadata: null,
            history: [],
            dnsCache: [status],
            performance: null);

        string output = _serializer.Serialize(snapshot);

        JsonElement root =
            JsonDocument.Parse(output).RootElement;

        JsonElement dnsProperty =
            root.GetProperty("DnsCache");

        Assert.Equal(1, dnsProperty.GetArrayLength());
        Assert.Equal(
            "example.com",
            dnsProperty[0]
                .GetProperty("Domain")
                .GetString());
    }

    [Fact]
    public void Serialize_PerformancePresent()
    {
        RuntimeCyclePerfReport report = new()
        {
            Trigger = "enable",
            StartedAt = FixedTime,
            CompletedAt = FixedTime.AddSeconds(1),
            TotalMs = 1000,
            Categories = []
        };

        SupportSnapshot snapshot = Build(
            runtime: null,
            diagnostics: null,
            preview: null,
            configuration: null,
            prefixMetadata: null,
            history: [],
            dnsCache: [],
            performance: report);

        string output = _serializer.Serialize(snapshot);

        JsonElement root =
            JsonDocument.Parse(output).RootElement;

        JsonElement performanceProperty =
            root.GetProperty("Performance");

        Assert.NotEqual(
            JsonValueKind.Null,
            performanceProperty.ValueKind);
        Assert.Equal(
            "enable",
            performanceProperty
                .GetProperty("Trigger")
                .GetString());
    }

    [Fact]
    public void Serialize_IndentedOutput_UsesFourSpaceIndent()
    {
        SupportSnapshot snapshot = Build(
            runtime: null,
            diagnostics: null,
            preview: null,
            configuration: null,
            prefixMetadata: null,
            history: [],
            dnsCache: [],
            performance: null);

        string output = _serializer.Serialize(snapshot);

        Assert.Contains("    \"", output);
    }

    private static SupportSnapshot Build(
        RuntimeSnapshot? runtime,
        DiagnosticReport? diagnostics,
        ExecutionPreview? preview,
        DesiredConfiguration? configuration,
        PrefixSourceMetadata? prefixMetadata,
        IReadOnlyList<PrefixSourceUpdateHistoryEntry> history,
        IReadOnlyList<CustomRouteDnsCacheStatus> dnsCache,
        RuntimeCyclePerfReport? performance)
    {
        return new SupportSnapshot
        {
            CapturedAt = FixedTime,
            Runtime = runtime,
            Diagnostics = diagnostics,
            ExecutionPreview = preview,
            Configuration = configuration,
            PrefixMetadata = prefixMetadata,
            PrefixHistory = history,
            DnsCache = dnsCache,
            Performance = performance,
            Summary = new SupportSnapshotSummary
            {
                DiagnosticPassedCount = 0,
                DiagnosticWarningCount = 0,
                DiagnosticFailedCount = 0,
                PrefixCount = 0,
                DnsDomainCount = 0,
                ExecutionPreviewHasChanges = false,
                RuntimeAvailable = false,
                PerformanceAvailable = false
            }
        };
    }

    private static SupportSnapshot BuildPopulated()
    {
        RuntimeSnapshot runtime = new()
        {
            CapturedAt = FixedTime,
            PrefixCount = 5
        };

        DiagnosticReport diagnostics = new(
            FixedTime,
            [
                new DiagnosticResult(
                    Id: "p1",
                    Title: "Pass 1",
                    Status: DiagnosticStatus.Passed,
                    Severity: DiagnosticSeverity.Pass,
                    Message: "ok",
                    SuggestedAction: null),
                new DiagnosticResult(
                    Id: "w1",
                    Title: "Warn 1",
                    Status: DiagnosticStatus.Warning,
                    Severity: DiagnosticSeverity.Warning,
                    Message: "watch",
                    SuggestedAction: null)
            ]);

        ExecutionPreview preview = new()
        {
            CapturedAt = FixedTime,
            Summary = new ExecutionPreviewSummary
            {
                CreateCount = 1,
                DeleteCount = 0,
                VerifyCount = 0,
                InventoryUpdates = 1,
                CustomRouteUpdates = 0,
                VpnEndpointUpdates = 0
            },
            Steps =
            [
                new ExecutionPreviewStep
                {
                    Category = ExecutionPreviewCategory.Route,
                    Operation =
                        ExecutionPreviewOperation.Create,
                    Target = "10.0.0.0/24",
                    Reason = "missing"
                }
            ]
        };

        DesiredConfiguration configuration = new();

        PrefixSourceMetadata metadata = new()
        {
            SourceId = "src",
            SourceDisplayName = "Source",
            Format = "txt",
            ParserVersion = "1",
            LastAttemptedAt = FixedTime,
            LastSucceededAt = FixedTime,
            LastStatus = PrefixSourceUpdateStatus.Succeeded,
            PrefixCount = 5
        };

        PrefixSourceUpdateHistoryEntry entry = new()
        {
            SourceId = "src",
            SourceDisplayName = "Source",
            Format = "txt",
            ParserVersion = "1",
            Status = PrefixSourceUpdateStatus.Succeeded,
            StartedAt = FixedTime,
            CompletedAt = FixedTime,
            AttemptedAt = FixedTime,
            PrefixCount = 5,
            HasChanges = true
        };

        CustomRouteDnsCacheStatus status = new()
        {
            CustomRouteEntryId = Guid.NewGuid(),
            Domain = "example.com",
            Enabled = true,
            State = CustomRouteDnsCacheState.Fresh
        };

        RuntimeCyclePerfReport performance = new()
        {
            Trigger = "enable",
            StartedAt = FixedTime,
            CompletedAt = FixedTime,
            TotalMs = 100,
            Categories = []
        };

        return new SupportSnapshot
        {
            CapturedAt = FixedTime,
            Runtime = runtime,
            Diagnostics = diagnostics,
            ExecutionPreview = preview,
            Configuration = configuration,
            PrefixMetadata = metadata,
            PrefixHistory = [entry],
            DnsCache = [status],
            Performance = performance,
            Summary = new SupportSnapshotSummary
            {
                DiagnosticPassedCount = 1,
                DiagnosticWarningCount = 1,
                DiagnosticFailedCount = 0,
                PrefixCount = 5,
                DnsDomainCount = 1,
                ExecutionPreviewHasChanges = true,
                RuntimeAvailable = true,
                PerformanceAvailable = true
            }
        };
    }
}
