namespace IranDirect.Core.SystemTools;

public sealed record CommandResult
{
    public required int ExitCode { get; init; }

    public required string StandardOutput { get; init; }

    public required string StandardError { get; init; }

    public bool Succeeded => ExitCode == 0;
}