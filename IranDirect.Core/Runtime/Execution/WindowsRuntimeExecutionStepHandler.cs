namespace IranDirect.Core.Runtime.Execution;

using System.Net;
using IranDirect.Core.Routing;
using IranDirect.Core.Vpn;

public sealed class WindowsRuntimeExecutionStepHandler : IRuntimeExecutionStepHandler
{
    private readonly IRouteManager _routeManager;
    private readonly IRouteInventoryPersistence _routeInventory;
    private readonly IEndpointInventoryPersistence _endpointInventory;

    public WindowsRuntimeExecutionStepHandler(
        IRouteManager routeManager,
        IRouteInventoryPersistence routeInventory,
        IEndpointInventoryPersistence endpointInventory)
    {
        ArgumentNullException.ThrowIfNull(routeManager);
        ArgumentNullException.ThrowIfNull(routeInventory);
        ArgumentNullException.ThrowIfNull(endpointInventory);

        _routeManager = routeManager;
        _routeInventory = routeInventory;
        _endpointInventory = endpointInventory;
    }

    public async Task<RuntimeExecutionStepResult> ExecuteAndVerifyAsync(
        RuntimeExecutionStep step,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(step);

        ValidationException.ThrowIfInvalid(step);

        return step.Kind switch
        {
            RuntimeExecutionStepKind.AddEndpointRoute =>
                await ExecuteAddEndpointRouteAsync(step, cancellationToken),

            RuntimeExecutionStepKind.RemoveEndpointRoute =>
                await ExecuteRemoveEndpointRouteAsync(step, cancellationToken),

            RuntimeExecutionStepKind.AddPrefixRoute =>
                await ExecuteAddPrefixRouteAsync(step, cancellationToken),

            RuntimeExecutionStepKind.RemovePrefixRoute =>
                await ExecuteRemovePrefixRouteAsync(step, cancellationToken),

            _ => throw new ArgumentOutOfRangeException(
                nameof(step.Kind), step.Kind, null)
        };
    }

    private async Task<RuntimeExecutionStepResult> ExecuteAddEndpointRouteAsync(
        RuntimeExecutionStep step,
        CancellationToken cancellationToken)
    {
        ManagedRoute managedRoute = ToManagedRoute(step);

        try
        {
            await _routeManager.AddRoutesAsync(
                [managedRoute], cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException
                                   and not ArgumentNullException)
        {
            return CreateFailedResult(step.Identity,
                $"Failed to add endpoint route: {ex.Message}");
        }

        if (!await RouteExistsAsync(step, cancellationToken))
        {
            return CreateFailedResult(step.Identity,
                "Endpoint route was not found after add.");
        }

        try
        {
            await _endpointInventory.MutateAsync(
                inventory =>
                {
                    VpnEndpointInventoryItem? existing = inventory.Endpoints
                        .FirstOrDefault(e =>
                            e.Identity.Equals(
                                step.Identity,
                                StringComparison.OrdinalIgnoreCase));

                    VpnEndpointInventoryItem item = existing is null
                        ? new VpnEndpointInventoryItem
                        {
                            Host = step.Description,
                            Address = step.Gateway,
                            Port = 0,
                            Protocol = "udp",
                            DestinationPrefix = step.DestinationPrefix,
                            Gateway = step.Gateway,
                            InterfaceIndex = step.InterfaceIndex,
                            Metric = step.Metric,
                            AddedByIranDirect = true,
                            IsCurrent = true,
                            ProtectedAt = DateTimeOffset.UtcNow,
                            LastSeenAt = DateTimeOffset.UtcNow
                        }
                        : existing with
                        {
                            AddedByIranDirect = true,
                            IsCurrent = true,
                            LastSeenAt = DateTimeOffset.UtcNow
                        };

                    VpnEndpointInventoryItem[] updated = existing is null
                        ? [.. inventory.Endpoints, item]
                        : inventory.Endpoints
                            .Select(e => e.Identity.Equals(
                                step.Identity,
                                StringComparison.OrdinalIgnoreCase) ? item : e)
                            .ToArray();

                    return inventory with { Endpoints = updated };
                },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException
                                   and not ArgumentNullException)
        {
            return CreateFailedResult(step.Identity,
                $"Endpoint route was added but inventory " +
                $"persistence failed: {ex.Message}");
        }

        return CreateSucceededResult(step.Identity);
    }

    private async Task<RuntimeExecutionStepResult> ExecuteRemoveEndpointRouteAsync(
        RuntimeExecutionStep step,
        CancellationToken cancellationToken)
    {
        bool routeOnPlatform = await RouteExistsAsync(step, cancellationToken);

        if (routeOnPlatform)
        {
            VpnEndpointInventoryItem? item;
            try
            {
                VpnEndpointInventory snapshot = await _endpointInventory.LoadAsync(cancellationToken);
                item = snapshot.Endpoints
                    .FirstOrDefault(e =>
                        e.Identity.Equals(
                            step.Identity,
                            StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex) when (ex is not OperationCanceledException
                                       and not ArgumentNullException)
            {
                return CreateFailedResult(step.Identity,
                    $"Failed to load endpoint inventory: {ex.Message}");
            }

            if (item is null)
            {
                return CreateFailedResult(step.Identity,
                    "Cannot remove endpoint route: route is not " +
                    "in the endpoint inventory.");
            }

            if (!item.AddedByIranDirect)
            {
                return CreateFailedResult(step.Identity,
                    "Cannot remove endpoint route: route was not " +
                    "created by IranDirect and may be a pre-existing " +
                    "VPN endpoint route.");
            }

            ManagedRoute managedRoute = ToManagedRoute(step);

            try
            {
                await _routeManager.DeleteRoutesAsync(
                    [managedRoute], cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException
                                       and not ArgumentNullException)
            {
                return CreateFailedResult(step.Identity,
                    $"Failed to remove endpoint route: {ex.Message}");
            }

            if (await RouteExistsAsync(step, cancellationToken))
            {
                return CreateFailedResult(step.Identity,
                    "Endpoint route still exists after removal.");
            }

            try
            {
                await _endpointInventory.MutateAsync(
                    inv => inv with
                    {
                        Endpoints = inv.Endpoints
                            .Where(e => !e.Identity.Equals(
                                step.Identity,
                                StringComparison.OrdinalIgnoreCase))
                            .ToArray()
                    },
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException
                                       and not ArgumentNullException)
            {
                return CreateFailedResult(step.Identity,
                    $"Endpoint route was removed but inventory " +
                    $"persistence failed: {ex.Message}");
            }

            return CreateSucceededResult(step.Identity);
        }

        try
        {
            await _endpointInventory.MutateAsync(
                inv =>
                {
                    VpnEndpointInventoryItem? stale = inv.Endpoints
                        .FirstOrDefault(e =>
                            e.Identity.Equals(
                                step.Identity,
                                StringComparison.OrdinalIgnoreCase) &&
                            e.AddedByIranDirect);

                    if (stale is null)
                        return inv;

                    return inv with
                    {
                        Endpoints = inv.Endpoints
                            .Where(e => !e.Identity.Equals(
                                step.Identity,
                                StringComparison.OrdinalIgnoreCase))
                            .ToArray()
                    };
                },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException
                                   and not ArgumentNullException)
        {
            return CreateFailedResult(step.Identity,
                $"Endpoint route was already absent but inventory " +
                $"cleanup failed: {ex.Message}");
        }

        return CreateSucceededResult(step.Identity);
    }

    private async Task<RuntimeExecutionStepResult> ExecuteAddPrefixRouteAsync(
        RuntimeExecutionStep step,
        CancellationToken cancellationToken)
    {
        ManagedRoute managedRoute = ToManagedRoute(step);

        try
        {
            await _routeManager.AddRoutesAsync(
                [managedRoute], cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException
                                   and not ArgumentNullException)
        {
            return CreateFailedResult(step.Identity,
                $"Failed to add prefix route: {ex.Message}");
        }

        if (!await RouteExistsAsync(step, cancellationToken))
        {
            return CreateFailedResult(step.Identity,
                "Prefix route was not found after add.");
        }

        try
        {
            await _routeInventory.MutateAsync(
                inventory =>
                {
                    bool exists = inventory.Routes.Any(r =>
                        r.Identity.Equals(
                            step.Identity,
                            StringComparison.OrdinalIgnoreCase));

                    if (exists)
                        return inventory;

                    RouteInventoryItem[] updated =
                        [.. inventory.Routes, ToInventoryItem(step)];

                    return inventory with { Routes = updated };
                },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException
                                   and not ArgumentNullException)
        {
            return CreateFailedResult(step.Identity,
                $"Prefix route was added but inventory " +
                $"persistence failed: {ex.Message}");
        }

        return CreateSucceededResult(step.Identity);
    }

    private async Task<RuntimeExecutionStepResult> ExecuteRemovePrefixRouteAsync(
        RuntimeExecutionStep step,
        CancellationToken cancellationToken)
    {
        bool routeOnPlatform = await RouteExistsAsync(step, cancellationToken);

        if (routeOnPlatform)
        {
            bool owned;
            try
            {
                RouteInventory snapshot = await _routeInventory.LoadAsync(cancellationToken);
                owned = snapshot.Routes.Any(r =>
                    r.Identity.Equals(
                        step.Identity,
                        StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex) when (ex is not OperationCanceledException
                                       and not ArgumentNullException)
            {
                return CreateFailedResult(step.Identity,
                    $"Failed to load route inventory: {ex.Message}");
            }

            if (!owned)
            {
                return CreateFailedResult(step.Identity,
                    "Cannot remove prefix route: route exists on the " +
                    "platform but is not owned by IranDirect.");
            }

            ManagedRoute managedRoute = ToManagedRoute(step);

            try
            {
                await _routeManager.DeleteRoutesAsync(
                    [managedRoute], cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException
                                       and not ArgumentNullException)
            {
                return CreateFailedResult(step.Identity,
                    $"Failed to remove prefix route: {ex.Message}");
            }

            if (await RouteExistsAsync(step, cancellationToken))
            {
                return CreateFailedResult(step.Identity,
                    "Prefix route still exists after removal.");
            }

            try
            {
                await _routeInventory.MutateAsync(
                    inv => inv with
                    {
                        Routes = inv.Routes
                            .Where(r => !r.Identity.Equals(
                                step.Identity,
                                StringComparison.OrdinalIgnoreCase))
                            .ToArray()
                    },
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException
                                       and not ArgumentNullException)
            {
                return CreateFailedResult(step.Identity,
                    $"Prefix route was removed but inventory " +
                    $"persistence failed: {ex.Message}");
            }

            return CreateSucceededResult(step.Identity);
        }

        try
        {
            await _routeInventory.MutateAsync(
                inv =>
                {
                    bool hasStaleOwned = inv.Routes.Any(r =>
                        r.Identity.Equals(
                            step.Identity,
                            StringComparison.OrdinalIgnoreCase));

                    if (!hasStaleOwned)
                        return inv;

                    return inv with
                    {
                        Routes = inv.Routes
                            .Where(r => !r.Identity.Equals(
                                step.Identity,
                                StringComparison.OrdinalIgnoreCase))
                            .ToArray()
                    };
                },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException
                                   and not ArgumentNullException)
        {
            return CreateFailedResult(step.Identity,
                $"Prefix route was already absent but inventory " +
                $"cleanup failed: {ex.Message}");
        }

        return CreateSucceededResult(step.Identity);
    }

    private async Task<bool> RouteExistsAsync(
        RuntimeExecutionStep step,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SystemRoute> routes =
            await _routeManager.GetIpv4RoutesAsync(
                cancellationToken);

        string expectedIdentity = step.Identity;

        return routes.Any(route =>
        {
            string routeIdentity =
                $"{route.DestinationPrefix}|" +
                $"{route.NextHop}|" +
                $"{route.InterfaceIndex}";

            return routeIdentity.Equals(
                expectedIdentity,
                StringComparison.OrdinalIgnoreCase);
        });
    }

    private static ManagedRoute ToManagedRoute(
        RuntimeExecutionStep step)
    {
        return new ManagedRoute
        {
            DestinationPrefix = step.DestinationPrefix,
            Gateway = IPAddress.Parse(step.Gateway),
            InterfaceIndex = step.InterfaceIndex,
            Metric = step.Metric
        };
    }

    private static RouteInventoryItem ToInventoryItem(
        RuntimeExecutionStep step)
    {
        return new RouteInventoryItem
        {
            DestinationPrefix = step.DestinationPrefix,
            Gateway = step.Gateway,
            InterfaceIndex = step.InterfaceIndex,
            Metric = step.Metric
        };
    }

    private static RuntimeExecutionStepResult CreateSucceededResult(
        string identity) =>
        new()
        {
            StepIdentity = identity,
            Status = RuntimeExecutionStepStatus.Succeeded
        };

    private static RuntimeExecutionStepResult CreateFailedResult(
        string identity, string errorMessage) =>
        new()
        {
            StepIdentity = identity,
            Status = RuntimeExecutionStepStatus.Failed,
            ErrorMessage = errorMessage
        };

    private static class ValidationException
    {
        public static void ThrowIfInvalid(RuntimeExecutionStep step)
        {
            if (string.IsNullOrWhiteSpace(step.Identity))
                throw new ArgumentException(
                    "Step Identity must not be empty.", nameof(step));

            if (string.IsNullOrWhiteSpace(step.DestinationPrefix))
                throw new ArgumentException(
                    "Step DestinationPrefix must not be empty.", nameof(step));

            if (string.IsNullOrWhiteSpace(step.Gateway))
                throw new ArgumentException(
                    "Step Gateway must not be empty.", nameof(step));

            if (step.InterfaceIndex == 0)
                throw new ArgumentException(
                    "Step InterfaceIndex must not be zero.", nameof(step));

            if (!IPAddress.TryParse(step.Gateway, out _))
                throw new ArgumentException(
                    "Step Gateway must be a valid IP address.", nameof(step));
        }
    }
}
