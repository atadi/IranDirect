using IranDirect.Core.Configuration;
using IranDirect.Core.CustomRoutes;
using IranDirect.Core.Persistence;
using IranDirect.Core.Prefixes;
using IranDirect.Core.Routing;
using IranDirect.Core.Runtime.Profiling;
using IranDirect.Core.State;
using IranDirect.Core.Vpn;

namespace IranDirect.Testing.Performance.Persistence;

/// <summary>
/// Deterministic endurance cycles for each persisted component. The
/// runners throw <see cref="InvalidOperationException"/> on any contract
/// violation so the infrastructure stays free of any test framework
/// dependency.
/// </summary>
public static class PersistenceEnduranceRunner
{
    public static async Task RunJsonStoreAsync(
        string path,
        int cycles,
        CancellationToken cancellationToken = default)
    {
        JsonStore<PersistenceEnduranceDocument> store = new(path);

        for (int i = 0; i < cycles; i++)
        {
            PersistenceEnduranceDocument document =
                PersistenceEnduranceFixtures.Document(i);

            await store.SaveAsync(document, cancellationToken);
            await PersistenceEnduranceVerifier.VerifyRoundTripAsync(
                store, document, cancellationToken);
        }
    }

    public static async Task RunRouteInventoryAsync(
        string path,
        int cycles,
        CancellationToken cancellationToken = default)
    {
        RouteInventoryStore store = new(path);

        for (int i = 0; i < cycles; i++)
        {
            RouteInventoryItem item =
                PersistenceEnduranceFixtures.RouteItem(i);

            await store.MutateAsync(
                inventory => inventory with
                {
                    Routes = inventory.Routes.Append(item).ToArray()
                },
                cancellationToken);

            if (i % 25 == 0 || i == cycles - 1)
            {
                RouteInventory spot =
                    await store.LoadAsync(cancellationToken);

                if (spot.Routes.Count != i + 1)
                {
                    throw new InvalidOperationException(
                        $"Expected {i + 1} routes after cycle {i}, " +
                        $"got {spot.Routes.Count}.");
                }
            }
        }

        RouteInventory expected = PersistenceEnduranceFixtures
            .RouteInventory(
                PersistenceEnduranceFixtures.DefaultSeed,
                cycles);

        RouteInventory final = await store.LoadAsync(cancellationToken);

        if (!SequenceEqual(expected.Routes, final.Routes))
        {
            throw new InvalidOperationException(
                "Final route inventory does not match the expected " +
                "deterministic document.");
        }
    }

    public static async Task RunVpnEndpointsAsync(
        string path,
        int cycles,
        CancellationToken cancellationToken = default)
    {
        VpnEndpointInventoryStore store = new(path);

        for (int i = 0; i < cycles; i++)
        {
            VpnEndpointInventoryItem item =
                PersistenceEnduranceFixtures.EndpointItem(i);

            await store.MutateAsync(
                inventory => inventory with
                {
                    Endpoints = inventory.Endpoints.Append(item).ToArray()
                },
                cancellationToken);

            if (i % 25 == 0 || i == cycles - 1)
            {
                VpnEndpointInventory spot =
                    await store.LoadAsync(cancellationToken);

                if (spot.Endpoints.Count != i + 1)
                {
                    throw new InvalidOperationException(
                        $"Expected {i + 1} endpoints after cycle {i}, " +
                        $"got {spot.Endpoints.Count}.");
                }
            }
        }

        VpnEndpointInventory expected = PersistenceEnduranceFixtures
            .VpnEndpointInventory(
                PersistenceEnduranceFixtures.DefaultSeed,
                cycles);

        VpnEndpointInventory final = await store.LoadAsync(cancellationToken);

        if (!SequenceEqual(expected.Endpoints, final.Endpoints))
        {
            throw new InvalidOperationException(
                "Final endpoint inventory does not match the expected " +
                "deterministic document.");
        }
    }

    public static async Task RunStateAsync(
        string path,
        int cycles,
        CancellationToken cancellationToken = default)
    {
        StateRepository store = new(path);

        for (int i = 0; i < cycles; i++)
        {
            IranDirectState state = PersistenceEnduranceFixtures.State(i);

            await store.SaveAsync(state, cancellationToken);

            IranDirectState loaded =
                await store.LoadAsync(cancellationToken);

            if (state != loaded)
            {
                throw new InvalidOperationException(
                    $"Runtime state at cycle {i} did not round-trip.");
            }
        }
    }

    public static async Task RunCustomRoutesAsync(
        string path,
        int cycles,
        CancellationToken cancellationToken = default)
    {
        CustomRouteStore store = new(path);

        for (int i = 0; i < cycles; i++)
        {
            CustomRouteCollection collection =
                PersistenceEnduranceFixtures.CustomRoutes(
                    PersistenceEnduranceFixtures.DefaultSeed,
                    i + 1);

            await store.SaveAsync(collection, cancellationToken);

            if (i % 25 == 0 || i == cycles - 1)
            {
                CustomRouteCollection spot =
                    await store.LoadAsync(cancellationToken);

                if (!SequenceEqual(collection.Entries, spot.Entries))
                {
                    throw new InvalidOperationException(
                        $"Custom routes at cycle {i} did not round-trip.");
                }
            }
        }
    }

    public static async Task RunDnsCacheAsync(
        string path,
        int cycles,
        CancellationToken cancellationToken = default)
    {
        CustomRouteDnsCacheStore store = new(path);

        for (int i = 0; i < cycles; i++)
        {
            CustomRouteDnsCacheCollection collection =
                PersistenceEnduranceFixtures.DnsCache(
                    PersistenceEnduranceFixtures.DefaultSeed,
                    i + 1);

            await store.SaveAsync(collection, cancellationToken);

            if (i % 25 == 0 || i == cycles - 1)
            {
                CustomRouteDnsCacheCollection spot =
                    await store.LoadAsync(cancellationToken);

                if (!DnsCacheSequenceEqual(collection.Entries, spot.Entries))
                {
                    throw new InvalidOperationException(
                        $"DNS cache at cycle {i} did not round-trip.");
                }
            }
        }
    }

    public static async Task RunPrefixMetadataAsync(
        string path,
        int cycles,
        CancellationToken cancellationToken = default)
    {
        PrefixSourceMetadataStore store = new(path);

        for (int i = 0; i < cycles; i++)
        {
            PrefixSourceMetadataDocument document =
                PersistenceEnduranceFixtures.MetadataDocument(
                    PersistenceEnduranceFixtures.DefaultSeed,
                    i);

            await store.SaveAsync(document, cancellationToken);

            PrefixSourceMetadataDocument loaded =
                await store.LoadAsync(cancellationToken);

            if (document != loaded)
            {
                throw new InvalidOperationException(
                    $"Prefix metadata at cycle {i} did not round-trip.");
            }
        }
    }

    public static async Task RunDesiredConfigAsync(
        string path,
        int cycles,
        CancellationToken cancellationToken = default)
    {
        DesiredConfigurationStore store = new(
            path,
            new DesiredConfigurationValidator());

        for (int i = 0; i < cycles; i++)
        {
            DesiredConfiguration configuration =
                PersistenceEnduranceFixtures.Configuration(i);

            await store.SaveAsync(configuration, cancellationToken);

            DesiredConfiguration loaded =
                await store.LoadAsync(cancellationToken);

            if (configuration != loaded)
            {
                throw new InvalidOperationException(
                    $"Desired configuration at cycle {i} did not " +
                    $"round-trip.");
            }
        }
    }

    public static async Task RunHistoryAsync(
        string path,
        int cycles,
        CancellationToken cancellationToken = default)
    {
        PrefixSourceUpdateHistoryStore store = new(path);
        PrefixSourceUpdateHistoryRepository repository = new(
            store,
            new PrefixSourceUpdateHistoryValidator());

        for (int i = 0; i < cycles; i++)
        {
            await repository.AppendAsync(
                PersistenceEnduranceFixtures.HistoryEntry(i),
                cancellationToken);
        }

        int retention = new PrefixSourceHistoryOptions().RetentionCount;

        PrefixSourceUpdateHistoryDocument document =
            await repository.LoadAsync(cancellationToken);

        if (document.Entries.Count != Math.Min(cycles, retention))
        {
            throw new InvalidOperationException(
                $"History retention was not enforced: expected " +
                $"{Math.Min(cycles, retention)} entries after " +
                $"{cycles} appends, got {document.Entries.Count}.");
        }

        IReadOnlyList<PrefixSourceUpdateHistoryEntry> recent =
            await repository.GetRecentAsync(
                limit: 10,
                cancellationToken: cancellationToken);

        if (recent.Count != Math.Min(10, document.Entries.Count)
            || recent.Count == 0)
        {
            throw new InvalidOperationException(
                "History did not return the expected recent entries.");
        }

        if (recent[0].Id
            != PersistenceEnduranceFixtures.DeterministicGuid(cycles - 1))
        {
            throw new InvalidOperationException(
                "History is not newest-first.");
        }
    }

    public static async Task RunPerfReportsAsync(
        string directory,
        int cycles,
        CancellationToken cancellationToken = default)
    {
        RuntimePerfReportStore store = new(directory);

        string[] triggers = ["enable", "disable", "repair"];
        DateTimeOffset baseTime = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);

        for (int i = 0; i < cycles; i++)
        {
            string trigger = triggers[i % triggers.Length];

            store.Write(PersistenceEnduranceFixtures.PerfReport(
                trigger,
                baseTime.AddSeconds(i),
                i));
        }

        RuntimeCyclePerfReport? latest = store.ReadLatest();
        if (latest is null
            || latest.CompletedAt != baseTime.AddSeconds(cycles - 1))
        {
            throw new InvalidOperationException(
                "Performance report latest-by-trigger ordering is not " +
                "deterministic.");
        }
    }

    private static bool DnsCacheSequenceEqual(
        IReadOnlyList<CustomRouteDnsCacheEntry> left,
        IReadOnlyList<CustomRouteDnsCacheEntry> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int i = 0; i < left.Count; i++)
        {
            CustomRouteDnsCacheEntry a = left[i];
            CustomRouteDnsCacheEntry b = right[i];

            if (a.CustomRouteEntryId != b.CustomRouteEntryId
                || a.Domain != b.Domain
                || a.LastAttemptedAt != b.LastAttemptedAt
                || a.LastSucceededAt != b.LastSucceededAt
                || a.ExpiresAt != b.ExpiresAt
                || a.StaleUntil != b.StaleUntil
                || a.LastError != b.LastError
                || !a.IPv4Addresses.SequenceEqual(b.IPv4Addresses))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SequenceEqual<T>(
        IReadOnlyList<T> left,
        IReadOnlyList<T> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        return left.SequenceEqual(right);
    }
}
