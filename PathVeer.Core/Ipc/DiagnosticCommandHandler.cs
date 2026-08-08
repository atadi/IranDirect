using PathVeer.Core.Diagnostics;

namespace PathVeer.Core.Ipc;

public sealed class DiagnosticCommandHandler
{
    private readonly IDiagnosticRunner _runner;

    public DiagnosticCommandHandler(IDiagnosticRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);

        _runner = runner;
    }

    public async Task<ServiceResponse> RunAsync(
        CancellationToken cancellationToken = default)
    {
        DiagnosticReport report =
            await _runner.RunAllAsync(cancellationToken);

        return new ServiceResponse
        {
            Success = true,
            Message =
                $"Diagnostics completed: " +
                $"{report.HighestSeverity}.",
            Report = report
        };
    }
}
