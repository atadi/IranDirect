namespace IranDirect.Core.Runtime.Execution;

using System.Net;
using IranDirect.Core.Routing;
using IranDirect.Core.Runtime.Profiling;
using IranDirect.Core.Vpn;

public sealed class WindowsRuntimeExecutionStepHandler :
    IRuntimeExecutionStepHandler,
    IPrefixGroupExecutionHandler
{
    private readonly IRouteManager _routeManager;
    private readonly IRouteInventoryPersistence _routeInventory;
    private readonly IEndpointInventoryPersistence _endpointInventory;
    private readonly RuntimeCycleProfiler _profiler;

    public WindowsRuntimeExecutionStepHandler(
        IRouteManager routeManager,
        IRouteInventoryPersistence routeInventory,
        IEndpointInventoryPersistence endpointInventory,
        RuntimeCycleProfiler? profiler = null)
    {
        ArgumentNullException.ThrowIfNull(routeManager);
        ArgumentNullException.ThrowIfNull(routeInventory);
        ArgumentNullException.ThrowIfNull(endpointInventory);

        _routeManager = routeManager;
        _routeInventory = routeInventory;
        _endpointInventory = endpointInventory;
        _profiler = profiler ?? RuntimeCycleProfiler.Noop;
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
                await ExecutePrefixRouteStepAsync(step, cancellationToken),

            RuntimeExecutionStepKind.RemovePrefixRoute =>
                await ExecutePrefixRouteStepAsync(step, cancellationToken),

            _ => throw new ArgumentOutOfRangeException(
                nameof(step.Kind), step.Kind, null)
        };
    }

    public async Task<PrefixMutationResult> MutatePrefixRouteAsync(
        RuntimeExecutionStep step,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(step);

        ValidationException.ThrowIfInvalid(step);

        return step.Kind switch
        {
            RuntimeExecutionStepKind.AddPrefixRoute =>
                await MutateAddPrefixRouteAsync(step, cancellationToken),

            RuntimeExecutionStepKind.RemovePrefixRoute =>
                await MutateRemovePrefixRouteAsync(step, cancellationToken),

            _ => throw new ArgumentOutOfRangeException(
                nameof(step.Kind), step.Kind, null)
        };
    }

    public async Task<IReadOnlyList<RuntimeExecutionStepResult>> VerifyPrefixRouteGroupAsync(
        IReadOnlyList<RuntimeExecutionStep> steps,
        IReadOnlyList<PrefixMutationResult> mutationResults,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(mutationResults);

        if (steps.Count == 0)
            return [];

        if (mutationResults.Count != steps.Count)
            throw new ArgumentException(
                "mutationResults must contain one entry per step.",
                nameof(mutationResults));

        RuntimeExecutionStepKind kind = steps[0].Kind;
        if (kind is not (RuntimeExecutionStepKind.AddPrefixRoute
                         or RuntimeExecutionStepKind.RemovePrefixRoute))
        {
            throw new ArgumentOutOfRangeException(
                nameof(steps), steps[0].Kind,
                "A prefix route group must contain only prefix steps.");
        }

        foreach (RuntimeExecutionStep step in steps)
        {
            ValidationException.ThrowIfInvalid(step);

            if (step.Kind != kind)
            {
                throw new ArgumentException(
                    $"A prefix route group must be homogeneous; " +
                    $"found {step.Kind} in a {kind} group.",
                    nameof(steps));
            }
        }

        RuntimePerfCategory verificationCategory =
            kind == RuntimeExecutionStepKind.AddPrefixRoute
                ? RuntimePerfCategory.ExecutionPrefixAddGroupVerification
                : RuntimePerfCategory.ExecutionPrefixRemoveGroupVerification;

        IReadOnlyList<SystemRoute> snapshot;
        using (_profiler.Measure(verificationCategory))
        {
            snapshot =
                await _routeManager.GetIpv4RoutesAsync(
                    cancellationToken);
        }

        RouteInventory inventory =
            await _routeInventory.LoadAsync(cancellationToken);

        RuntimeExecutionStepResult[] results =
            new RuntimeExecutionStepResult[steps.Count];

        for (int i = 0; i < steps.Count; i++)
        {
            results[i] = kind == RuntimeExecutionStepKind.AddPrefixRoute
                ? await ClassifyAddPrefixRouteAsync(
                    steps[i], snapshot, inventory,
                    mutationResults[i], cancellationToken)
                : await ClassifyRemovePrefixRouteAsync(
                    steps[i], snapshot, inventory,
                    mutationResults[i], cancellationToken);
        }

        return results;
    }

    private async Task<RuntimeExecutionStepResult> ExecutePrefixRouteStepAsync(
        RuntimeExecutionStep step,
        CancellationToken cancellationToken)
    {
        PrefixMutationResult mutation =
            await MutatePrefixRouteAsync(step, cancellationToken);

        IReadOnlyList<RuntimeExecutionStepResult> verified =
            await VerifyPrefixRouteGroupAsync(
                [step], [mutation], cancellationToken);

        return verified[0];
    }

    private async Task<PrefixMutationResult> MutateAddPrefixRouteAsync(
        RuntimeExecutionStep step,
        CancellationToken cancellationToken)
    {
        ManagedRoute managedRoute = ToManagedRoute(step);

        try
        {
            using (_profiler.Measure(
                RuntimePerfCategory.ExecutionPrefixAddMutation))
            {
                await _routeManager.AddRoutesAsync(
                    [managedRoute], cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return PrefixMutationResult.Failure(
                $"Failed to add prefix route: {ex.Message}");
        }

        return PrefixMutationResult.Success();
    }

    private async Task<PrefixMutationResult> MutateRemovePrefixRouteAsync(
        RuntimeExecutionStep step,
        CancellationToken cancellationToken)
    {
        bool owned;
        try
        {
            RouteInventory inventory =
                await _routeInventory.LoadAsync(cancellationToken);

            owned = inventory.Routes.Any(r =>
                r.Identity.Equals(
                    step.Identity,
                    StringComparison.OrdinalIgnoreCase));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return PrefixMutationResult.Failure(
                $"Failed to load route inventory: {ex.Message}");
        }

        if (!owned)
        {
            return PrefixMutationResult.Failure(
                "Cannot remove prefix route: route exists on the " +
                "platform but is not owned by IranDirect.");
        }

        ManagedRoute managedRoute = ToManagedRoute(step);

        try
        {
            using (_profiler.Measure(
                RuntimePerfCategory.ExecutionPrefixRemoveMutation))
            {
                await _routeManager.DeleteRoutesAsync(
                    [managedRoute], cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return PrefixMutationResult.Failure(
                $"Failed to remove prefix route: {ex.Message}");
        }

        return PrefixMutationResult.Success();
    }

    private async Task<RuntimeExecutionStepResult> ClassifyAddPrefixRouteAsync(
        RuntimeExecutionStep step,
        IReadOnlyList<SystemRoute> snapshot,
        RouteInventory inventory,
        PrefixMutationResult mutation,
        CancellationToken cancellationToken)
    {
        bool exactPresent =
            snapshot.Any(route => MatchesExact(step, route));

        if (!exactPresent)
        {
            if (!mutation.Succeeded)
            {
                return CreateFailedResult(step,
                    mutation.ErrorMessage ?? "Failed to add prefix route.");
            }

            return CreateFailedResult(step,
                "Prefix route was not found after add.");
        }

        bool owned = inventory.Routes.Any(r =>
            r.Identity.Equals(
                step.Identity,
                StringComparison.OrdinalIgnoreCase));

        if (owned)
            return CreateSucceededResult(step);

        if (mutation.Succeeded)
        {
            try
            {
                await MutateRouteInventoryAsync(
                    inventory =>
                    {
                        if (inventory.Routes.Any(r =>
                                r.Identity.Equals(
                                    step.Identity,
                                    StringComparison.OrdinalIgnoreCase)))
                            return inventory;

                        return inventory with
                        {
                            Routes =
                                [.. inventory.Routes, ToInventoryItem(step)]
                        };
                    },
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await CompensatePrefixRouteCreationAsync(
                    ToManagedRoute(step), step,
                    "Operation was cancelled during route inventory persistence.");
                throw;
            }
            catch (Exception ex) when (ex is not ArgumentNullException)
            {
                string msg = await CompensatePrefixRouteCreationAsync(
                    ToManagedRoute(step), step, ex.Message);
                return CreateFailedResult(step, msg);
            }

            return CreateSucceededResult(step);
        }

        return CreateFailedResult(step,
            "Cannot add prefix route: an exact matching route already " +
            "exists on the platform but is not owned by IranDirect.");
    }

    private async Task<RuntimeExecutionStepResult> ClassifyRemovePrefixRouteAsync(
        RuntimeExecutionStep step,
        IReadOnlyList<SystemRoute> snapshot,
        RouteInventory inventory,
        PrefixMutationResult mutation,
        CancellationToken cancellationToken)
    {
        bool present =
            snapshot.Any(route => MatchesIdentity(step, route));

        bool owned = inventory.Routes.Any(r =>
            r.Identity.Equals(
                step.Identity,
                StringComparison.OrdinalIgnoreCase));

        if (!present)
        {
            if (owned)
            {
                try
                {
                    await MutateRouteInventoryAsync(
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
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is not ArgumentNullException)
                {
                    return CreateFailedResult(step,
                        $"Prefix route was already absent but inventory " +
                        $"cleanup failed: {ex.Message}");
                }
            }

            return CreateSucceededResult(step);
        }

        if (owned)
        {
            return CreateFailedResult(step,
                "Prefix route still exists after removal.");
        }

        return CreateFailedResult(step,
            "Cannot remove prefix route: route exists on the " +
            "platform but is not owned by IranDirect.");
    }

    private static bool MatchesExact(
        RuntimeExecutionStep step,
        SystemRoute route)
    {
        return route.DestinationPrefix.Equals(
                   step.DestinationPrefix,
                   StringComparison.OrdinalIgnoreCase)
               && route.NextHop.ToString().Equals(
                   step.Gateway,
                   StringComparison.OrdinalIgnoreCase)
               && route.InterfaceIndex == step.InterfaceIndex
               && route.RouteMetric == step.Metric;
    }

    private static bool MatchesIdentity(
        RuntimeExecutionStep step,
        SystemRoute route)
    {
        string routeIdentity =
            $"{route.DestinationPrefix}|" +
            $"{route.NextHop}|" +
            $"{route.InterfaceIndex}";

        return routeIdentity.Equals(
            step.Identity,
            StringComparison.OrdinalIgnoreCase);
    }

    private async Task<RuntimeExecutionStepResult> ExecuteAddEndpointRouteAsync(
        RuntimeExecutionStep step,
        CancellationToken cancellationToken)
    {
        ManagedRoute managedRoute = ToManagedRoute(step);

        if (await RouteExistsAsync(step, cancellationToken))
            return await CheckExistingEndpointOwnershipAsync(step, cancellationToken);

        try
        {
            using (_profiler.Measure(
                RuntimePerfCategory.ExecutionRouteCreate))
            {
                await _routeManager.AddRoutesAsync(
                    [managedRoute], cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException
                                   and not ArgumentNullException)
        {
            return CreateFailedResult(step,
                $"Failed to add endpoint route: {ex.Message}");
        }

        if (!await RouteExistsAsync(step, cancellationToken))
        {
            return CreateFailedResult(step,
                "Endpoint route was not found after add.");
        }

        try
        {
            await MutateEndpointInventoryAsync(
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
        catch (OperationCanceledException)
        {
            await CompensateEndpointRouteCreationAsync(managedRoute, step,
                "Operation was cancelled during endpoint inventory persistence.");
            throw;
        }
        catch (Exception ex) when (ex is not ArgumentNullException)
        {
            string msg = await CompensateEndpointRouteCreationAsync(managedRoute, step, ex.Message);
            return CreateFailedResult(step, msg);
        }

        return CreateSucceededResult(step);
    }

    private async Task<RuntimeExecutionStepResult> CheckExistingEndpointOwnershipAsync(
        RuntimeExecutionStep step,
        CancellationToken cancellationToken)
    {
        try
        {
            VpnEndpointInventory snapshot = await _endpointInventory.LoadAsync(cancellationToken);
            VpnEndpointInventoryItem? item = snapshot.Endpoints
                .FirstOrDefault(e =>
                    e.Identity.Equals(step.Identity, StringComparison.OrdinalIgnoreCase));

            if (item is not null)
            {
                if (item.AddedByIranDirect)
                    return CreateSucceededResult(step);

                return CreateFailedResult(step,
                    "Cannot add endpoint route: an exact matching route already exists " +
                    "on the platform but is not owned by IranDirect.");
            }

            return CreateFailedResult(step,
                "Cannot add endpoint route: an exact matching route already exists " +
                "on the platform but is not in the endpoint inventory.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return CreateFailedResult(step,
                $"Failed to check endpoint inventory for existing route: {ex.Message}");
        }
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
                return CreateFailedResult(step,
                    $"Failed to load endpoint inventory: {ex.Message}");
            }

            if (item is null)
            {
                return CreateFailedResult(step,
                    "Cannot remove endpoint route: route is not " +
                    "in the endpoint inventory.");
            }

            if (!item.AddedByIranDirect)
            {
                return CreateFailedResult(step,
                    "Cannot remove endpoint route: route was not " +
                    "created by IranDirect and may be a pre-existing " +
                    "VPN endpoint route.");
            }

            ManagedRoute managedRoute = ToManagedRoute(step);

            try
            {
                using (_profiler.Measure(
                    RuntimePerfCategory.ExecutionRouteDelete))
                {
                    await _routeManager.DeleteRoutesAsync(
                        [managedRoute], cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException
                                       and not ArgumentNullException)
            {
                return CreateFailedResult(step,
                    $"Failed to remove endpoint route: {ex.Message}");
            }

            if (await RouteExistsAsync(step, cancellationToken))
            {
                return CreateFailedResult(step,
                    "Endpoint route still exists after removal.");
            }

            try
            {
                await MutateEndpointInventoryAsync(
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
                return CreateFailedResult(step,
                    $"Endpoint route was removed but inventory " +
                    $"persistence failed: {ex.Message}");
            }

            return CreateSucceededResult(step);
        }

        try
        {
            await MutateEndpointInventoryAsync(
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
            return CreateFailedResult(step,
                $"Endpoint route was already absent but inventory " +
                $"cleanup failed: {ex.Message}");
        }

        return CreateSucceededResult(step);
    }

    private async Task<string> CompensatePrefixRouteCreationAsync(
        ManagedRoute managedRoute,
        RuntimeExecutionStep step,
        string originalError)
    {
        try
        {
            using (_profiler.Measure(
                RuntimePerfCategory.ExecutionRouteDelete))
            {
                await _routeManager.DeleteRoutesAsync(
                    [managedRoute], CancellationToken.None);
            }
        }
        catch (Exception compEx)
        {
            return $"Prefix route was created but inventory persistence failed: " +
                   $"{originalError}. Compensation removal also failed: " +
                   $"{compEx.Message}. Orphaned route: {step.Identity}.";
        }

        if (await RouteExistsAsync(step, CancellationToken.None))
        {
            return $"Prefix route was created but inventory persistence failed: " +
                   $"{originalError}. Compensation removal was attempted but the " +
                   $"route still exists. Orphaned route: {step.Identity}.";
        }

        return $"Prefix route was created but inventory persistence failed: " +
               $"{originalError}. Route was removed as compensation.";
    }

    private async Task<string> CompensateEndpointRouteCreationAsync(
        ManagedRoute managedRoute,
        RuntimeExecutionStep step,
        string originalError)
    {
        try
        {
            using (_profiler.Measure(
                RuntimePerfCategory.ExecutionRouteDelete))
            {
                await _routeManager.DeleteRoutesAsync(
                    [managedRoute], CancellationToken.None);
            }
        }
        catch (Exception compEx)
        {
            return $"Endpoint route was created but inventory persistence failed: " +
                   $"{originalError}. Compensation removal also failed: " +
                   $"{compEx.Message}. Orphaned route: {step.Identity}.";
        }

        if (await RouteExistsAsync(step, CancellationToken.None))
        {
            return $"Endpoint route was created but inventory persistence failed: " +
                   $"{originalError}. Compensation removal was attempted but the " +
                   $"route still exists. Orphaned route: {step.Identity}.";
        }

        return $"Endpoint route was created but inventory persistence failed: " +
               $"{originalError}. Route was removed as compensation.";
    }

    private async Task<bool> RouteExistsAsync(
        RuntimeExecutionStep step,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SystemRoute> routes;

        using (_profiler.Measure(
            RuntimePerfCategory.ExecutionRouteVerify))
        {
            routes =
                await _routeManager.GetIpv4RoutesAsync(
                    cancellationToken);
        }

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

    private async Task MutateRouteInventoryAsync(
        Func<RouteInventory, RouteInventory> transform,
        CancellationToken cancellationToken)
    {
        using (_profiler.Measure(
            RuntimePerfCategory.ExecutionInventoryMutation))
        {
            await _routeInventory.MutateAsync(
                transform, cancellationToken);
        }
    }

    private async Task MutateEndpointInventoryAsync(
        Func<VpnEndpointInventory, VpnEndpointInventory> transform,
        CancellationToken cancellationToken)
    {
        using (_profiler.Measure(
            RuntimePerfCategory.ExecutionInventoryMutation))
        {
            await _endpointInventory.MutateAsync(
                transform, cancellationToken);
        }
    }

    private static RuntimeExecutionStepResult CreateSucceededResult(
        RuntimeExecutionStep step) =>
        new()
        {
            StepIdentity = step.Identity,
            Kind = step.Kind,
            DestinationPrefix = step.DestinationPrefix,
            Status = RuntimeExecutionStepStatus.Succeeded
        };

    private static RuntimeExecutionStepResult CreateFailedResult(
        RuntimeExecutionStep step, string errorMessage) =>
        new()
        {
            StepIdentity = step.Identity,
            Kind = step.Kind,
            DestinationPrefix = step.DestinationPrefix,
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
