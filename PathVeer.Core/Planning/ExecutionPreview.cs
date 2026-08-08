using System.Collections.ObjectModel;

namespace PathVeer.Core.Planning;

public sealed record ExecutionPreview
{
    public required DateTimeOffset CapturedAt { get; init; }

    public required ExecutionPreviewSummary Summary
        { get; init; }

    public IReadOnlyList<ExecutionPreviewStep> Steps
        { get; init; } = [];

    public bool HasChanges => Summary.HasChanges;

    // Cached on first access. Steps is immutable after construction, so the
    // grouping is computed once and reused for every subsequent read,
    // including repeated IPC serialization and Tray/CLI consumers. The cached
    // dictionary preserves first-seen category order and per-category step
    // order, so the serialized shape is identical to a fresh grouping.
    private IReadOnlyDictionary<ExecutionPreviewCategory,
        IReadOnlyList<ExecutionPreviewStep>>? _categories;

    public IReadOnlyDictionary<ExecutionPreviewCategory,
        IReadOnlyList<ExecutionPreviewStep>> Categories
    {
        get
        {
            if (_categories is null)
            {
                _categories = GroupByCategory(Steps);
            }

            return _categories;
        }
    }

    private static IReadOnlyDictionary<ExecutionPreviewCategory,
        IReadOnlyList<ExecutionPreviewStep>> GroupByCategory(
        IReadOnlyList<ExecutionPreviewStep> steps)
    {
        var groups =
            new Dictionary<ExecutionPreviewCategory,
                List<ExecutionPreviewStep>>();

        foreach (ExecutionPreviewStep step in steps)
        {
            if (!groups.TryGetValue(
                    step.Category,
                    out List<ExecutionPreviewStep>?
                        list))
            {
                list = [];
                groups[step.Category] = list;
            }

            list.Add(step);
        }

        var result =
            new Dictionary<ExecutionPreviewCategory,
                IReadOnlyList<ExecutionPreviewStep>>();

        foreach (var kvp in groups)
        {
            result[kvp.Key] =
                new ReadOnlyCollection<ExecutionPreviewStep>(
                    kvp.Value);
        }

        return result;
    }
}
