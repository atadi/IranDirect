namespace PathVeer.Core.Runtime.Execution;

/// <summary>
/// Describes the outcome of executing a single <c>RuntimeExecutionStep</c>.
/// </summary>
public enum RuntimeExecutionStepStatus
{
    /// <summary>The step has been defined but not yet executed.</summary>
    Planned,

    /// <summary>
    /// The platform mutation completed and the expected postcondition was verified.
    /// A successful route command without verified platform state is not Succeeded.
    /// Inventory persistence is not included in step success.
    /// </summary>
    Succeeded,

    /// <summary>
    /// The platform mutation failed or the postcondition could not be verified.
    /// </summary>
    Failed,

    /// <summary>
    /// Execution was cancelled before the mutation was applied.
    /// </summary>
    Cancelled,

    /// <summary>
    /// The step was skipped due to a precondition failure.
    /// </summary>
    Skipped
}
