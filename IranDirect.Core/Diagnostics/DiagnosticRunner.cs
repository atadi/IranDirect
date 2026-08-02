using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace IranDirect.Core.Diagnostics;

public sealed class DiagnosticRunner :
    IDiagnosticRunner
{
    private readonly IReadOnlyList<IDiagnosticCheck> _checks;

    public DiagnosticRunner(
        IReadOnlyList<IDiagnosticCheck> checks)
    {
        _checks = checks;
    }

    public async Task<DiagnosticReport> RunAllAsync(
        CancellationToken cancellationToken)
    {
        List<DiagnosticResult> results = new();

        foreach (IDiagnosticCheck check in _checks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            DiagnosticResult result;

            try
            {
                result = await check.CheckAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                result = new DiagnosticResult(
                    Id: check.Id,
                    Title: check.Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message: exception.Message,
                    SuggestedAction: null);
            }

            results.Add(result);
        }

        return new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results: new ReadOnlyCollection<DiagnosticResult>(results));
    }
}