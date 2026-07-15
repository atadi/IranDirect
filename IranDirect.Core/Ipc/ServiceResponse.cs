namespace IranDirect.Core.Ipc;

public sealed record ServiceResponse
{
    public bool Success { get; init; }

    public string Message { get; init; } = "";

    public IranDirectStatus? Status { get; init; }
}