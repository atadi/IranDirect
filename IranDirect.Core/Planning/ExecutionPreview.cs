using System.Collections.ObjectModel;

namespace IranDirect.Core.Planning;

public sealed record ExecutionPreview
{
    public required DateTimeOffset CapturedAt { get; init; }

    public required ExecutionPreviewSummary Summary
        { get; init; }

    public IReadOnlyList<ExecutionPreviewStep> Steps
        { get; init; } = [];

    public bool HasChanges => Summary.HasChanges;

    public IReadOnlyDictionary<ExecutionPreviewCategory,
        IReadOnlyList<ExecutionPreviewStep>> Categories
    {
        get
        {
            var groups =
                new Dictionary<ExecutionPreviewCategory,
                    List<ExecutionPreviewStep>>();

            foreach (ExecutionPreviewStep step in Steps)
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
                    new ReadOnlyCollection<
                        ExecutionPreviewStep>(kvp.Value);
            }

            return result;
        }
    }
}
