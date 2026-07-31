namespace IranDirect.Core.Runtime.Profiling;

/// <summary>
/// Identifies a measured operation category within a runtime cycle.
/// Categories are purely additive instrumentation labels; they do
/// not affect execution behavior.
/// </summary>
public enum RuntimePerfCategory
{
    ObservationGatewayDetection,
    ObservationPrefixLoad,
    ObservationRouteTableRead,
    ObservationVpnEndpointLoad,

    PlanningDecisionBuild,

    ExecutionTotal,
    ExecutionRouteCreate,
    ExecutionRouteDelete,
    ExecutionRouteVerify,
    ExecutionInventoryMutation,

    ExecutionPrefixAddMutation,
    ExecutionPrefixRemoveMutation,
    ExecutionPrefixAddGroupVerification,
    ExecutionPrefixRemoveGroupVerification,

    PersistenceStateSave,
    PersistenceInventorySave
}
