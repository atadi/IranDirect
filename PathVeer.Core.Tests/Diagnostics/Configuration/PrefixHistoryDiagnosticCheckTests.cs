using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PathVeer.Core.Configuration;
using PathVeer.Core.Diagnostics;
using PathVeer.Core.Diagnostics.Configuration;
using PathVeer.Core.Prefixes;

namespace PathVeer.Core.Tests.Diagnostics.Configuration;

public sealed class PrefixHistoryDiagnosticCheckTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    static PrefixHistoryDiagnosticCheckTests()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    private static PrefixSourceUpdateHistoryEntry ValidEntry(
        DateTimeOffset completedAt)
    {
        DateTimeOffset started = completedAt.AddHours(-1);
        return new PrefixSourceUpdateHistoryEntry
        {
            Id = Guid.NewGuid(),
            SourceId = "official",
            SourceDisplayName = "Official",
            Status = PrefixSourceUpdateStatus.Succeeded,
            StartedAt = started,
            CompletedAt = completedAt,
            AttemptedAt = completedAt,
            PrefixCount = 2,
            AddedCount = 2,
            RemovedCount = 0,
            UnchangedCount = 0,
            HasChanges = true
        };
    }

    private static PrefixSourceUpdateHistoryDocument ValidDocument() =>
        new()
        {
            SchemaVersion = 1,
            Entries =
            [
                ValidEntry(DateTimeOffset.UtcNow.AddHours(-1)),
                ValidEntry(DateTimeOffset.UtcNow)
            ]
        };

    private static CountryPrefixStore SeedStore(
        PrefixSourceUpdateHistoryDocument document)
    {
        string dir = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        CountryPrefixStore store = new(dir);
        store.GetUpdateHistoryRepository(DirectCountryCode.IR)
            .SaveAsync(document).GetAwaiter().GetResult();
        return store;
    }

    private static PrefixSourceHistoryOptions Options(int retention) =>
        new() { RetentionCount = retention };

    [Fact]
    public async Task CheckAsync_ValidHistory_ReturnsPassed()
    {
        var check = new PrefixHistoryDiagnosticCheck(
            SeedStore(ValidDocument()),
            () => DirectCountryCode.IR,
            Options(100));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
    }

    [Fact]
    public async Task CheckAsync_UnsupportedSchemaVersion_ReturnsFailed()
    {
        var doc = ValidDocument() with { SchemaVersion = 2 };

        var check = new PrefixHistoryDiagnosticCheck(
            SeedStore(doc),
            () => DirectCountryCode.IR,
            Options(100));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Equal(DiagnosticSeverity.Error, result.Severity);
    }

    [Fact]
    public async Task CheckAsync_NonMonotonicTimestamps_ReturnsFailed()
    {
        var entries = new List<PrefixSourceUpdateHistoryEntry>
        {
            ValidEntry(DateTimeOffset.UtcNow),
            ValidEntry(DateTimeOffset.UtcNow.AddHours(-1))
        };

        var doc = ValidDocument() with { Entries = entries };

        var check = new PrefixHistoryDiagnosticCheck(
            SeedStore(doc),
            () => DirectCountryCode.IR,
            Options(100));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Equal(DiagnosticSeverity.Error, result.Severity);
    }

    [Fact]
    public async Task CheckAsync_ExceedsRetentionCount_ReturnsWarning()
    {
        var entries = new List<PrefixSourceUpdateHistoryEntry>();

        for (int i = 0; i < 101; i++)
        {
            entries.Add(ValidEntry(DateTimeOffset.UtcNow.AddHours(i)));
        }

        var doc = ValidDocument() with { Entries = entries };

        var check = new PrefixHistoryDiagnosticCheck(
            SeedStore(doc),
            () => DirectCountryCode.IR,
            Options(100));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Equal(DiagnosticSeverity.Warning, result.Severity);
    }

    [Fact]
    public async Task CheckAsync_EmptyHistory_ReturnsPassed()
    {
        var doc = ValidDocument() with { Entries = [] };

        var check = new PrefixHistoryDiagnosticCheck(
            SeedStore(doc),
            () => DirectCountryCode.IR,
            Options(100));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
    }

    [Fact]
    public async Task CheckAsync_RepoThrows_ReturnsFailed()
    {
        string dir = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        string historyFile = Path.Combine(
            dir, "prefixes", "IR", "update-history.json");
        Directory.CreateDirectory(Path.GetDirectoryName(historyFile));
        File.WriteAllText(historyFile, "{ not valid json ");

        var check = new PrefixHistoryDiagnosticCheck(
            new CountryPrefixStore(dir),
            () => DirectCountryCode.IR,
            Options(100));

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Equal(DiagnosticSeverity.Error, result.Severity);
        Assert.NotNull(result.SuggestedAction);
    }
}
