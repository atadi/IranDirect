namespace PathVeer.Core.Planning;

public sealed record ExecutionPreviewSummary
{
    public required int CreateCount { get; init; }

    public required int DeleteCount { get; init; }

    public required int VerifyCount { get; init; }

    public required int InventoryUpdates { get; init; }

    public required int CustomRouteUpdates { get; init; }

    public required int VpnEndpointUpdates { get; init; }

    public int EstimatedOperations =>
        CreateCount + DeleteCount + VerifyCount +
        InventoryUpdates + CustomRouteUpdates +
        VpnEndpointUpdates;

    public bool HasChanges =>
        CreateCount > 0 || DeleteCount > 0 ||
        InventoryUpdates > 0 || CustomRouteUpdates > 0 ||
        VpnEndpointUpdates > 0;
}
