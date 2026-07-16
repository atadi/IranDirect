namespace IranDirect.Core.Configuration;

public sealed record ConfigurationValidationResult
{
    public bool IsValid => Errors.Count == 0;

    public IReadOnlyList<string> Errors
        { get; init; } = [];
}