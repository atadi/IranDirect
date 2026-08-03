using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using IranDirect.Core.Testing.FaultInjection;

namespace IranDirect.Core.Diagnostics;

public sealed class DiagnosticRunner :
    IDiagnosticRunner
{
    private readonly IReadOnlyList<IDiagnosticCheck> _checks;
    private readonly IFaultInjectionPolicy _faultPolicy;

    public DiagnosticRunner(
        IReadOnlyList<IDiagnosticCheck> checks,
        IFaultInjectionPolicy? faultPolicy = null)
    {
        _checks = checks;
        _faultPolicy = faultPolicy ?? FaultInjectionPolicy.Never;
    }

    public async Task<DiagnosticReport> RunAllAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ShouldFailAt(FaultInjectionPoint.DiagnosticsRun))
        {
            throw new FaultInjectionException(
                FaultInjectionPoint.DiagnosticsRun);
        }

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

    private bool ShouldFailAt(FaultInjectionPoint point) =>
        FaultInjectionResolver.ShouldFail(_faultPolicy, point);
}