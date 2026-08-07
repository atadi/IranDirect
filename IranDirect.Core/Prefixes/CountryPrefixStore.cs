using System.IO;
using IranDirect.Core.Configuration;

namespace IranDirect.Core.Prefixes;

/// <summary>
/// Country-scoped prefix persistence. Every country owns an isolated directory
/// under <c>prefixes/&lt;CC&gt;/</c> containing its IPv4 prefix file, metadata,
/// and update history. Countries never share or overwrite each other's data.
///
/// Existing installations may carry a legacy Iran cache at the store root
/// (<c>iran-ipv4-prefixes.txt</c>, <c>prefix-source-metadata.json</c>,
/// <c>prefix-source-update-history.json</c>). When the selected country is IR
/// and the new IR-scoped location has no data yet, the legacy files are
/// migrated atomically. The legacy files are never interpreted for a
/// non-IR country and are never deleted until migration succeeds.
/// </summary>
public sealed class CountryPrefixStore
{
    private readonly string _rootDirectory;

    public CountryPrefixStore(string rootDirectory)
    {
        _rootDirectory = rootDirectory;
    }

    private string CountryDirectory(DirectCountryCode country) =>
        Path.Combine(_rootDirectory, "prefixes", country.Code);

    // ---- legacy (pre-35.3) Iran root files ----

    private string LegacyPrefixFile =>
        Path.Combine(_rootDirectory, "iran-ipv4-prefixes.txt");
    private string LegacyMetadataFile =>
        Path.Combine(_rootDirectory, "prefix-source-metadata.json");
    private string LegacyHistoryFile =>
        Path.Combine(_rootDirectory, "prefix-source-update-history.json");

    // ---- per-country files ----

    public string PrefixFileFor(DirectCountryCode country) =>
        Path.Combine(CountryDirectory(country), "ipv4-prefixes.txt");

    public string MetadataFileFor(DirectCountryCode country) =>
        Path.Combine(CountryDirectory(country), "metadata.json");

    public string UpdateHistoryFileFor(DirectCountryCode country) =>
        Path.Combine(CountryDirectory(country), "update-history.json");

    public IPrefixSourceMetadataRepository GetMetadataRepository(
        DirectCountryCode country) =>
        new PrefixSourceMetadataRepository(
            new PrefixSourceMetadataStore(
                MetadataFileFor(country)),
            new PrefixSourceMetadataValidator());

    public IPrefixSourceUpdateHistoryRepository
        GetUpdateHistoryRepository(DirectCountryCode country) =>
        new PrefixSourceUpdateHistoryRepository(
            new PrefixSourceUpdateHistoryStore(
                UpdateHistoryFileFor(country)),
            new PrefixSourceUpdateHistoryValidator());

    // ---- prefix file persistence (with legacy IR migration) ----

    public async Task SavePrefixesAsync(
        DirectCountryCode country,
        IEnumerable<string> prefixes,
        CancellationToken cancellationToken = default)
    {
        PrefixFileRepository repo = new(PrefixFileFor(country));
        await repo.SaveAsync(prefixes, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> LoadPrefixesAsync(
        DirectCountryCode country,
        CancellationToken cancellationToken = default)
    {
        string target = PrefixFileFor(country);

        if (File.Exists(target))
        {
            PrefixFileRepository repo = new(target);
            return await repo.LoadAsync(cancellationToken);
        }

        // Legacy migration applies only to the IR country. A non-IR country
        // must never consume the legacy Iran cache.
        if (country == DirectCountryCode.IR
            && File.Exists(LegacyPrefixFile))
        {
            PrefixFileRepository legacy = new(LegacyPrefixFile);
            IReadOnlyList<string> legacyPrefixes =
                await legacy.LoadAsync(cancellationToken);

            if (legacyPrefixes.Count > 0)
            {
                await SavePrefixesAsync(
                    country, legacyPrefixes, cancellationToken);
                return legacyPrefixes;
            }
        }

        return Array.Empty<string>();
    }

    public bool HasPrefixes(DirectCountryCode country)
    {
        if (File.Exists(PrefixFileFor(country)))
        {
            return new PrefixFileRepository(PrefixFileFor(country))
                .LoadAsync(CancellationToken.None)
                .GetAwaiter().GetResult().Count > 0;
        }

        return country == DirectCountryCode.IR
            && File.Exists(LegacyPrefixFile)
            && new PrefixFileRepository(LegacyPrefixFile)
                .LoadAsync(CancellationToken.None)
                .GetAwaiter().GetResult().Count > 0;
    }

    public DateTimeOffset? GetPrefixLastModified(
        DirectCountryCode country)
    {
        string target = PrefixFileFor(country);
        if (File.Exists(target))
        {
            return new PrefixFileRepository(target).GetLastModified();
        }

        if (country == DirectCountryCode.IR
            && File.Exists(LegacyPrefixFile))
        {
            return new PrefixFileRepository(LegacyPrefixFile)
                .GetLastModified();
        }

        return null;
    }

    // ---- legacy metadata migration (IR only) ----

    public async Task MigrateLegacyMetadataIfNeededAsync(
        DirectCountryCode country,
        CancellationToken cancellationToken = default)
    {
        if (country != DirectCountryCode.IR)
        {
            return;
        }

        string target = MetadataFileFor(country);
        if (File.Exists(target))
        {
            return; // already migrated
        }

        if (!File.Exists(LegacyMetadataFile))
        {
            return;
        }

        // Copy legacy metadata into the IR-scoped location. The legacy file is
        // left in place; deletion is intentionally not performed here so a
        // failed migration can be retried safely.
        PrefixSourceMetadataStore legacyStore =
            new(LegacyMetadataFile);
        PrefixSourceMetadataDocument legacy =
            await legacyStore.LoadAsync(cancellationToken);

        PrefixSourceMetadataStore targetStore = new(target);
        await targetStore.SaveAsync(legacy, cancellationToken);
    }

    public async Task MigrateLegacyHistoryIfNeededAsync(
        DirectCountryCode country,
        CancellationToken cancellationToken = default)
    {
        if (country != DirectCountryCode.IR)
        {
            return;
        }

        string target = UpdateHistoryFileFor(country);
        if (File.Exists(target))
        {
            return;
        }

        if (!File.Exists(LegacyHistoryFile))
        {
            return;
        }

        PrefixSourceUpdateHistoryStore legacyStore =
            new(LegacyHistoryFile);
        PrefixSourceUpdateHistoryDocument legacy =
            await legacyStore.LoadAsync(cancellationToken);

        PrefixSourceUpdateHistoryStore targetStore = new(target);
        await targetStore.SaveAsync(legacy, cancellationToken);
    }
}
