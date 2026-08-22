using System.Text.Json;
using PathVeer.Core.Prefixes;

namespace PathVeer.Core.Tests.Prefixes;

public sealed class PrefixSourceMetadataRepositoryTests
{
    [Fact]
    public async Task LoadAsync_WhenFileDoesNotExist_ReturnsDefault()
    {
        (PrefixSourceMetadataRepository repository, _) =
            CreateRepository();

        PrefixSourceMetadataDocument document =
            await repository.LoadAsync();

        Assert.Equal(1, document.SchemaVersion);
        Assert.Null(document.Current);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsMetadata()
    {
        (PrefixSourceMetadataRepository repository, _) =
            CreateRepository();

        PrefixSourceMetadataDocument expected =
            CreateDocument(CreateMetadata());

        await repository.SaveAsync(expected);

        PrefixSourceMetadataDocument actual =
            await repository.LoadAsync();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task SaveAsync_LaterSuccess_ReplacesCurrentMetadata()
    {
        (PrefixSourceMetadataRepository repository, _) =
            CreateRepository();

        await repository.SaveAsync(
            CreateDocument(
                CreateMetadata(
                    prefixCount: 3,
                    status: PrefixSourceUpdateStatus.Succeeded)));

        await repository.SaveAsync(
            CreateDocument(
                CreateMetadata(
                    prefixCount: 8,
                    status: PrefixSourceUpdateStatus.Succeeded)));

        PrefixSourceMetadata? current =
            (await repository.LoadAsync()).Current;

        Assert.NotNull(current);
        Assert.Equal(8, current.PrefixCount);
        Assert.Equal(
            PrefixSourceUpdateStatus.Succeeded,
            current.LastStatus);
    }

    [Fact]
    public async Task LoadAsync_CorruptJson_Throws()
    {
        (PrefixSourceMetadataRepository repository, string path) =
            CreateRepository();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{ not valid json !!!");

        await Assert.ThrowsAsync<JsonException>(
            () => repository.LoadAsync());
    }

    // Finding 6: an EXISTING zero-length file is corruption, not a missing
    // file. A fail-closed store surfaces it as JsonException.
    [Fact]
    public async Task LoadAsync_EmptyFile_ThrowsCorruption()
    {
        (PrefixSourceMetadataRepository repository, string path) =
            CreateRepository();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "");

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(
            () => repository.LoadAsync());
    }

    [Fact]
    public async Task LoadAsync_MalformedPersistedMetadata_Throws()
    {
        (PrefixSourceMetadataRepository repository, string path) =
            CreateRepository();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            """
            {
              "SchemaVersion": 1,
              "Current": {
                "SourceId": "",
                "SourceDisplayName": "Test",
                "PrefixCount": 0,
                "LastStatus": "Succeeded"
              }
            }
            """);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.LoadAsync());
    }

    [Fact]
    public async Task SaveAsync_MalformedMetadata_DoesNotPersist()
    {
        (PrefixSourceMetadataRepository repository, string path) =
            CreateRepository();

        PrefixSourceMetadataDocument invalid =
            CreateDocument(
                CreateMetadata() with { SourceId = "" });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SaveAsync(invalid));

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task SaveAsync_InvalidContentHash_Throws()
    {
        (PrefixSourceMetadataRepository repository, _) =
            CreateRepository();

        PrefixSourceMetadataDocument invalid =
            CreateDocument(
                CreateMetadata() with
                {
                    ContentHash = "not-a-hash"
                });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SaveAsync(invalid));
    }

    [Fact]
    public async Task SaveAsync_SucceededAtLaterThanAttemptedAt_Throws()
    {
        (PrefixSourceMetadataRepository repository, _) =
            CreateRepository();

        PrefixSourceMetadataDocument invalid =
            CreateDocument(
                CreateMetadata() with
                {
                    LastAttemptedAt = new DateTimeOffset(
                        2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    LastSucceededAt = new DateTimeOffset(
                        2026, 1, 1, 0, 5, 0, TimeSpan.Zero)
                });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SaveAsync(invalid));
    }

    [Fact]
    public async Task SaveAsync_NegativePrefixCount_Throws()
    {
        (PrefixSourceMetadataRepository repository, _) =
            CreateRepository();

        PrefixSourceMetadataDocument invalid =
            CreateDocument(
                CreateMetadata() with { PrefixCount = -1 });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SaveAsync(invalid));
    }

    [Fact]
    public async Task ConcurrentSaves_DoNotCorruptDocument()
    {
        (PrefixSourceMetadataRepository repository, string path) =
            CreateRepository();

        Task[] tasks = Enumerable.Range(0, 20)
            .Select(i => repository.SaveAsync(
                CreateDocument(
                    CreateMetadata(
                        prefixCount: i))))
            .ToArray();

        await Task.WhenAll(tasks);

        PrefixSourceMetadataDocument loaded =
            await repository.LoadAsync();

        Assert.NotNull(loaded.Current);
        Assert.True(
            loaded.Current.PrefixCount is >= 0 and < 20);

        Assert.DoesNotContain(
            Directory.GetFiles(
                Path.GetDirectoryName(path)!),
            file => file.EndsWith(".tmp"));
    }

    private static PrefixSourceMetadata CreateMetadata(
        int prefixCount = 0,
        PrefixSourceUpdateStatus status =
            PrefixSourceUpdateStatus.Succeeded)
    {
        DateTimeOffset now = new(
            2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        return new PrefixSourceMetadata
        {
            SourceId = "test-source",
            SourceDisplayName = "Test Source",
            SourceUri = "https://example.test/data.json",
            Format = "example-json",
            ParserVersion = "1",
            LastAttemptedAt = now,
            LastSucceededAt =
                status == PrefixSourceUpdateStatus.Succeeded
                    ? now
                    : null,
            PrefixCount = prefixCount,
            LastStatus = status
        };
    }

    private static PrefixSourceMetadataDocument CreateDocument(
        PrefixSourceMetadata metadata) =>
        new()
        {
            SchemaVersion = 1,
            Current = metadata
        };

    private static (PrefixSourceMetadataRepository, string)
        CreateRepository()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"),
            "prefix-source-metadata.json");

        PrefixSourceMetadataRepository repository = new(
            new PrefixSourceMetadataStore(path),
            new PrefixSourceMetadataValidator());

        return (repository, path);
    }
}
