using PathVeer.Core.Configuration;
using PathVeer.Core.CustomRoutes;
using PathVeer.Core.Diagnostics;
using PathVeer.Core.Observability;
using PathVeer.Core.Planning;
using PathVeer.Core.Prefixes;
using PathVeer.Core.Runtime.Profiling;

namespace PathVeer.Core.Support;

public sealed record SupportSnapshot
{
    public required DateTimeOffset CapturedAt { get; init; }

    public RuntimeSnapshot? Runtime { get; init; }

    public DiagnosticReport? Diagnostics { get; init; }

    public ExecutionPreview? ExecutionPreview { get; init; }

    public DesiredConfiguration? Configuration { get; init; }

    public PrefixSourceMetadata? PrefixMetadata { get; init; }

    public IReadOnlyList<PrefixSourceUpdateHistoryEntry> PrefixHistory
        { get; init; } = [];

    public IReadOnlyList<CustomRouteDnsCacheStatus> DnsCache
        { get; init; } = [];

    public RuntimeCyclePerfReport? Performance { get; init; }

    public required SupportSnapshotSummary Summary { get; init; }

    public bool Healthy => Summary.Healthy;

    public bool HasWarnings => Summary.HasWarnings;

    public bool HasErrors => Summary.HasErrors;
}
