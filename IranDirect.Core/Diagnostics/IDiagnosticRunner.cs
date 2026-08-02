using System.Threading;
using System.Threading.Tasks;

namespace IranDirect.Core.Diagnostics;

public interface IDiagnosticRunner
{
    Task<DiagnosticReport> RunAllAsync(
        CancellationToken cancellationToken);
}