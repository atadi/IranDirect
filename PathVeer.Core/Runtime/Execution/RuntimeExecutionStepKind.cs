namespace PathVeer.Core.Runtime.Execution;

/// <summary>
/// Identifies the type of platform operation an execution step represents.
/// Each step is an atomic mutate-and-verify unit.
/// </summary>
/// <remarks>
/// The ordinal value of this enum MUST NOT be used to determine execution order.
/// Execution order is defined by <c>RuntimeExecutionPlanner</c> through an explicit
/// priority policy. The safe ordering is:
/// AddEndpointRoute → RemovePrefixRoute → AddPrefixRoute → RemoveEndpointRoute.
/// </remarks>
public enum RuntimeExecutionStepKind
{
    AddEndpointRoute,
    RemoveEndpointRoute,
    AddPrefixRoute,
    RemovePrefixRoute
}
