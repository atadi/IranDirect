using System.Net;
using PathVeer.Core.Routing;
using PathVeer.Core.Vpn;
using Microsoft.Extensions.Logging;

namespace PathVeer.Core.Runtime.Execution;

/// <summary>
/// Resolves interrupted route mutations after a process crash, using the durable
/// write-ahead journal as the sole authority for whether IranDirect initiated a
/// native mutation. It runs once at startup, before the first reconciliation
/// cycle, so that normal planning never reads possibly-inconsistent ownership.
///
/// Recovery never infers ownership from native route shape alone: an externally
/// created route identical to a desired route is left untouched unless a
/// matching journal intent proves IranDirect began the operation.
///
/// The algorithm is idempotent: re-running it after a partial recovery converges
/// to the same end state and yields an empty journal.
/// </summary>
public sealed class RouteMutationRecovery
{
    private readonly IRouteManager _routeManager;
    private readonly IRouteInventoryPersistence _routeInventory;
    private readonly IEndpointInventoryPersistence _endpointInventory;
    private readonly IRouteMutationJournal _journal;
    private readonly ILogger<RouteMutationRecovery> _logger;

    public RouteMutationRecovery(
        IRouteManager routeManager,
        IRouteInventoryPersistence routeInventory,
        IEndpointInventoryPersistence endpointInventory,
        IRouteMutationJournal journal,
        ILogger<RouteMutationRecovery>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(routeManager);
        ArgumentNullException.ThrowIfNull(routeInventory);
        ArgumentNullException.ThrowIfNull(endpointInventory);
        ArgumentNullException.ThrowIfNull(journal);

        _routeManager = routeManager;
        _routeInventory = routeInventory;
        _endpointInventory = endpointInventory;
        _journal = journal;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RouteMutationRecovery>.Instance;
    }

    public async Task RecoverAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, RouteMutationJournalEntry> intents;

        try
        {
            intents = await _journal.LoadAllAsync(cancellationToken);
        }
        catch (RouteMutationJournalCorruptException ex)
        {
            _logger.LogError(
                ex,
                "Route mutation journal is corrupt. Recovery aborted without " +
                "mutating routes. The journal file is preserved for diagnosis.");

            try
            {
                QuarantineCorruptJournal();
            }
            catch (Exception quarantineEx)
            {
                _logger.LogError(
                    quarantineEx,
                    "Failed to quarantine the corrupt route mutation journal.");
            }

            return;
        }

        if (intents.Count == 0)
            return;

        _logger.LogInformation(
            "Recovering {Count} interrupted route mutation(s) from the journal.",
            intents.Count);

        IReadOnlyList<SystemRoute> snapshot;
        try
        {
            snapshot = await _routeManager.GetIpv4RoutesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Route table could not be read during recovery. Recovery " +
                "deferred to the next startup.");
            return;
        }

        int adopted = 0;
        int completedDeletes = 0;
        int clearedStale = 0;
        int deferred = 0;

        foreach (RouteMutationJournalEntry entry in intents.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            RecoveryOutcome outcome = await TryRecoverIntentAsync(
                entry, snapshot, cancellationToken);

            switch (outcome.Kind)
            {
                case RecoveryResult.Adopted:
                    adopted++;
                    break;
                case RecoveryResult.CompletedDelete:
                    completedDeletes++;
                    break;
                case RecoveryResult.ClearedStale:
                    clearedStale++;
                    break;
                case RecoveryResult.Deferred:
                    deferred++;
                    break;
            }
        }

        _logger.LogInformation(
            "Route mutation recovery complete. Adopted={Adopted} " +
            "CompletedDeletes={CompletedDeletes} ClearedStale={ClearedStale} " +
            "Deferred={Deferred}.",
            adopted, completedDeletes, clearedStale, deferred);
    }

    private enum RecoveryResult
    {
        Adopted,
        CompletedDelete,
        ClearedStale,
        Deferred
    }

    private sealed record RecoveryOutcome(RecoveryResult Kind);

    /// <summary>
    /// Resolves a single journal intent. Any failure (e.g. a transient inventory
    /// write error) is logged and the intent is left in the journal so recovery
    /// can retry it on the next startup; one poison intent must not abort recovery
    /// of the others.
    /// </summary>
    private async Task<RecoveryOutcome> TryRecoverIntentAsync(
        RouteMutationJournalEntry entry,
        IReadOnlyList<SystemRoute> snapshot,
        CancellationToken cancellationToken)
    {
        try
        {
            bool nativePresent = RoutePresent(snapshot, entry);
            bool inventoryHas = await InventoryHasAsync(entry, cancellationToken);

            if (entry.Kind == RouteMutationKind.Add)
            {
                if (inventoryHas)
                {
                    await _journal.ClearIntentAsync(entry.RouteIdentity, cancellationToken);
                    return new RecoveryOutcome(RecoveryResult.ClearedStale);
                }

                if (nativePresent)
                {
                    await AdoptAsync(entry, cancellationToken);
                    await _journal.ClearIntentAsync(entry.RouteIdentity, cancellationToken);
                    return new RecoveryOutcome(RecoveryResult.Adopted);
                }

                await _journal.ClearIntentAsync(entry.RouteIdentity, cancellationToken);
                return new RecoveryOutcome(RecoveryResult.ClearedStale);
            }
            else // Delete
            {
                if (!inventoryHas)
                {
                    await _journal.ClearIntentAsync(entry.RouteIdentity, cancellationToken);
                    return new RecoveryOutcome(RecoveryResult.ClearedStale);
                }

                if (!nativePresent)
                {
                    await CompleteDeleteAsync(entry, cancellationToken);
                    await _journal.ClearIntentAsync(entry.RouteIdentity, cancellationToken);
                    return new RecoveryOutcome(RecoveryResult.CompletedDelete);
                }

                // Case B: native still present and owned. Preserve ownership and
                // let normal reconciliation retry the deletion; clear the journal
                // since the inventory already reflects the intended state.
                await _journal.ClearIntentAsync(entry.RouteIdentity, cancellationToken);
                return new RecoveryOutcome(RecoveryResult.ClearedStale);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Recovery for route {RouteIdentity} ({Kind}) failed; the intent " +
                "is retained for the next startup.",
                entry.RouteIdentity, entry.Kind);
            return new RecoveryOutcome(RecoveryResult.Deferred);
        }
    }

    private async Task AdoptAsync(
        RouteMutationJournalEntry entry,
        CancellationToken cancellationToken)
    {
        if (entry.InventoryKind == RouteMutationInventoryKind.Endpoint)
        {
            await _endpointInventory.MutateAsync(inv =>
            {
                if (inv.Endpoints.Any(e =>
                        e.Identity.Equals(
                            entry.RouteIdentity, StringComparison.OrdinalIgnoreCase)))
                    return inv;

                VpnEndpointInventoryItem item = new()
                {
                    Host = entry.Description,
                    Address = entry.Gateway,
                    Port = 0,
                    Protocol = "udp",
                    DestinationPrefix = entry.DestinationPrefix,
                    Gateway = entry.Gateway,
                    InterfaceIndex = entry.InterfaceIndex,
                    Metric = entry.Metric,
                    AddedByIranDirect = true,
                    IsCurrent = true,
                    ProtectedAt = DateTimeOffset.UtcNow,
                    LastSeenAt = DateTimeOffset.UtcNow
                };

                return inv with
                {
                    Endpoints = [.. inv.Endpoints, item]
                };
            }, cancellationToken);
        }
        else
        {
            await _routeInventory.MutateAsync(inv =>
            {
                if (inv.Routes.Any(r =>
                        r.Identity.Equals(
                            entry.RouteIdentity, StringComparison.OrdinalIgnoreCase)))
                    return inv;

                RouteInventoryItem item = new()
                {
                    DestinationPrefix = entry.DestinationPrefix,
                    Gateway = entry.Gateway,
                    InterfaceIndex = entry.InterfaceIndex,
                    Metric = entry.Metric
                };

                return inv with
                {
                    Routes = [.. inv.Routes, item]
                };
            }, cancellationToken);
        }
    }

    private async Task CompleteDeleteAsync(
        RouteMutationJournalEntry entry,
        CancellationToken cancellationToken)
    {
        if (entry.InventoryKind == RouteMutationInventoryKind.Endpoint)
        {
            await _endpointInventory.MutateAsync(inv => inv with
            {
                Endpoints = inv.Endpoints
                    .Where(e => !e.Identity.Equals(
                        entry.RouteIdentity, StringComparison.OrdinalIgnoreCase))
                    .ToArray()
            }, cancellationToken);
        }
        else
        {
            await _routeInventory.MutateAsync(inv => inv with
            {
                Routes = inv.Routes
                    .Where(r => !r.Identity.Equals(
                        entry.RouteIdentity, StringComparison.OrdinalIgnoreCase))
                    .ToArray()
            }, cancellationToken);
        }
    }

    private async Task<bool> InventoryHasAsync(
        RouteMutationJournalEntry entry,
        CancellationToken cancellationToken)
    {
        if (entry.InventoryKind == RouteMutationInventoryKind.Endpoint)
        {
            VpnEndpointInventory endpointInv =
                await _endpointInventory.LoadAsync(cancellationToken);
            return endpointInv.Endpoints.Any(e =>
                e.Identity.Equals(
                    entry.RouteIdentity, StringComparison.OrdinalIgnoreCase));
        }

        RouteInventory routeInv = await _routeInventory.LoadAsync(cancellationToken);
        return routeInv.Routes.Any(r =>
            r.Identity.Equals(
                entry.RouteIdentity, StringComparison.OrdinalIgnoreCase));
    }

    private static bool RoutePresent(
        IReadOnlyList<SystemRoute> snapshot,
        RouteMutationJournalEntry entry)
    {
        return snapshot.Any(route =>
        {
            string identity =
                $"{route.DestinationPrefix}|" +
                $"{route.NextHop}|" +
                $"{route.InterfaceIndex}";
            return identity.Equals(
                entry.RouteIdentity, StringComparison.OrdinalIgnoreCase);
        });
    }

    private void QuarantineCorruptJournal()
    {
        // Rename rather than delete so operators can inspect the bytes that
        // produced the unreadable state. Best-effort; failure is logged by caller.
        string? corruptPath = _pathOrNull();
        if (corruptPath is null || !File.Exists(corruptPath))
            return;

        string quarantine = corruptPath + ".corrupt";
        if (File.Exists(quarantine))
            File.Delete(quarantine);
        File.Move(corruptPath, quarantine, overwrite: true);
    }

    private string? _pathOrNull()
    {
        // The journal store owns the path; expose it through a marker interface
        // if available, otherwise fall back to a convention. To avoid a hard
        // dependency, we accept that quarantine is best-effort and rely on the
        // already-logged path-free operational error.
        if (_journal is RouteMutationJournalStore store)
            return store.JournalPath;

        return null;
    }
}
