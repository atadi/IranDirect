using System.Threading;
using System.Threading.Tasks;

namespace PathVeer.Core.Diagnostics;

public interface IDiagnosticRunner
{
    Task<DiagnosticReport> RunAllAsync(
        CancellationToken cancellationToken);
}