using System.Threading;
using System.Threading.Tasks;

namespace PathVeer.Core.Diagnostics;

public interface IDiagnosticCheck
{
    string Id { get; }
    string Title { get; }
    Task<DiagnosticResult> CheckAsync(
        CancellationToken cancellationToken);
}