using IranDirect.Core.Diagnostics;
using IranDirect.Core.Diagnostics.Configuration;
using IranDirect.Core.Prefixes;

namespace IranDirect.Core.Tests.Diagnostics.Configuration;

public sealed class PrefixHistoryDiagnosticCheckTests
{
    private static PrefixSourceUpdateHistoryDocument ValidDocument() =>
        new()
        {
            SchemaVersion = 1,
            Entries =
            [
                new PrefixSourceUpdateHistoryEntry
                {
                    CompletedAt = DateTimeOffset.UtcNow.AddHours(-1),
                    Status = PrefixSourceUpdateStatus.Succeeded
                },
                new PrefixSourceUpdateHistoryEntry
                {
                    CompletedAt = DateTimeOffset.UtcNow,
                    Status = PrefixSourceUpdateStatus.Succeeded
                }
            ]
        };

    private sealed class FakeHistoryRepository :
        IPrefixSourceUpdateHistoryRepository
    {
        private readonly PrefixSourceUpdateHistoryDocument _document;

        public FakeHistoryRepository(
            PrefixSourceUpdateHistoryDocument document)
        {
            _document = document;
        }

        public Task<PrefixSourceUpdateHistoryDocument> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_document);
        }

        public Task SaveAsync(
            PrefixSourceUpdateHistoryDocument document,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PrefixSourceUpdateHistoryEntry>>
            GetRecentAsync(
                int limit,
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PrefixSourceUpdateHistoryEntry>>(
                Array.Empty<PrefixSourceUpdateHistoryEntry>());
        }

        public Task AppendAsync(
            PrefixSourceUpdateHistoryEntry entry,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ClearAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task CheckAsync_ValidHistory_ReturnsPassed()
    {
        var check = new PrefixHistoryDiagnosticCheck(
            new FakeHistoryRepository(ValidDocument()),
            new PrefixSourceHistoryOptions { RetentionCount = 100 });

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
    }

    [Fact]
    public async Task CheckAsync_UnsupportedSchemaVersion_ReturnsFailed()
    {
        var doc = ValidDocument() with { SchemaVersion = 2 };

        var check = new PrefixHistoryDiagnosticCheck(
            new FakeHistoryRepository(doc),
            new PrefixSourceHistoryOptions { RetentionCount = 100 });

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Equal(DiagnosticSeverity.Error, result.Severity);
    }

    [Fact]
    public async Task CheckAsync_NonMonotonicTimestamps_ReturnsFailed()
    {
        var doc = ValidDocument();
        doc = doc with
        {
            Entries =
            [
                new PrefixSourceUpdateHistoryEntry
                {
                    CompletedAt = DateTimeOffset.UtcNow,
                    Status = PrefixSourceUpdateStatus.Succeeded
                },
                new PrefixSourceUpdateHistoryEntry
                {
                    CompletedAt = DateTimeOffset.UtcNow.AddHours(-1),
                    Status = PrefixSourceUpdateStatus.Succeeded
                }
            ]
        };

        var check = new PrefixHistoryDiagnosticCheck(
            new FakeHistoryRepository(doc),
            new PrefixSourceHistoryOptions { RetentionCount = 100 });

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
            entries.Add(
                new PrefixSourceUpdateHistoryEntry
                {
                    CompletedAt = DateTimeOffset.UtcNow.AddHours(i),
                    Status = PrefixSourceUpdateStatus.Succeeded
                });
        }

        var doc = ValidDocument() with { Entries = entries };

        var check = new PrefixHistoryDiagnosticCheck(
            new FakeHistoryRepository(doc),
            new PrefixSourceHistoryOptions { RetentionCount = 100 });

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
            new FakeHistoryRepository(doc),
            new PrefixSourceHistoryOptions { RetentionCount = 100 });

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
    }

    [Fact]
    public async Task CheckAsync_RepoThrows_ReturnsFailed()
    {
        var check = new PrefixHistoryDiagnosticCheck(
            new ThrowingHistoryRepository(),
            new PrefixSourceHistoryOptions { RetentionCount = 100 });

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Equal(DiagnosticSeverity.Error, result.Severity);
        Assert.NotNull(result.SuggestedAction);
    }

    private sealed class ThrowingHistoryRepository :
        IPrefixSourceUpdateHistoryRepository
    {
        public Task<PrefixSourceUpdateHistoryDocument> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            throw new IOException("file not found");
        }

        public Task SaveAsync(
            PrefixSourceUpdateHistoryDocument document,
            CancellationToken cancellationToken = default)
        {
            throw new IOException("file not found");
        }

        public Task<IReadOnlyList<PrefixSourceUpdateHistoryEntry>>
            GetRecentAsync(
                int limit,
                CancellationToken cancellationToken = default)
        {
            throw new IOException("file not found");
        }

        public Task AppendAsync(
            PrefixSourceUpdateHistoryEntry entry,
            CancellationToken cancellationToken = default)
        {
            throw new IOException("file not found");
        }

        public Task ClearAsync(
            CancellationToken cancellationToken = default)
        {
            throw new IOException("file not found");
        }
    }
}