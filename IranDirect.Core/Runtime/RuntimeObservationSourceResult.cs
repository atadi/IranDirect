namespace IranDirect.Core.Runtime;

public sealed record RuntimeObservationSourceResult<T>
{
    public bool Succeeded { get; init; }

    public T? Value { get; init; }

    public string? Error { get; init; }

    public static RuntimeObservationSourceResult<T> Success(
        T value) =>
        new()
        {
            Succeeded = true,
            Value = value
        };

    public static RuntimeObservationSourceResult<T> Failure(
        string error) =>
        new()
        {
            Succeeded = false,
            Error = error
        };
}