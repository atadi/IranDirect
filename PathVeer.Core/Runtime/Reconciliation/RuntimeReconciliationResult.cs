namespace PathVeer.Core.Runtime.Reconciliation;

public sealed record RuntimeReconciliationResult
{
    public required RuntimeReconciliationStatus Status
        { get; init; }

    public RuntimeChangeSet ChangeSet
        { get; init; } = new();

    public IReadOnlyList<string> Messages
        { get; init; } = [];

    public bool Succeeded =>
        Status is
            RuntimeReconciliationStatus.NoChangesRequired or
            RuntimeReconciliationStatus.ChangesApplied or
            RuntimeReconciliationStatus.ChangesPlanned;

    public bool MutatedInfrastructure =>
        Status == RuntimeReconciliationStatus.ChangesApplied;

    public static RuntimeReconciliationResult NoChanges(
        params string[] messages) =>
        new()
        {
            Status =
                RuntimeReconciliationStatus.NoChangesRequired,
            Messages = messages
        };

    public static RuntimeReconciliationResult Blocked(
        IEnumerable<string> messages) =>
        new()
        {
            Status =
                RuntimeReconciliationStatus.Blocked,
            Messages = messages.ToArray()
        };

    public static RuntimeReconciliationResult Applied(
        RuntimeChangeSet changeSet,
        params string[] messages)
    {
        ArgumentNullException.ThrowIfNull(changeSet);

        if (changeSet.IsEmpty)
        {
            throw new ArgumentException(
                "An applied reconciliation result requires " +
                "at least one change.",
                nameof(changeSet));
        }

        return new RuntimeReconciliationResult
        {
            Status =
                RuntimeReconciliationStatus.ChangesApplied,
            ChangeSet = changeSet,
            Messages = messages
        };
    }

    public static RuntimeReconciliationResult Planned(
        RuntimeChangeSet changeSet,
        params string[] messages)
    {
        ArgumentNullException.ThrowIfNull(changeSet);

        if (changeSet.IsEmpty)
        {
            throw new ArgumentException(
                "A planned reconciliation result requires " +
                "at least one change.",
                nameof(changeSet));
        }

        return new RuntimeReconciliationResult
        {
            Status =
                RuntimeReconciliationStatus.ChangesPlanned,
            ChangeSet = changeSet,
            Messages = messages
        };
    }

    public static RuntimeReconciliationResult Failed(
        IEnumerable<string> messages) =>
        new()
        {
            Status =
                RuntimeReconciliationStatus.Failed,
            Messages = messages.ToArray()
        };
}