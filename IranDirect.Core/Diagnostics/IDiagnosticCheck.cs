using System.Threading;
using System.Threading.Tasks;

namespace IranDirect.Core.Diagnostics;

public interface IDiagnosticCheck
{
    string Id { get; }
    string Title { get; }
    Task<DiagnosticResult> CheckAsync(
        CancellationToken cancellationToken);
}