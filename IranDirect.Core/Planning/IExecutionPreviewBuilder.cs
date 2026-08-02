using IranDirect.Core.Runtime;

namespace IranDirect.Core.Planning;

public interface IExecutionPreviewBuilder
{
    ExecutionPreview Build(RuntimeDecision decision);
}
