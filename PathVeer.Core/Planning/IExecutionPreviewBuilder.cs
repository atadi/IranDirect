using PathVeer.Core.Runtime;

namespace PathVeer.Core.Planning;

public interface IExecutionPreviewBuilder
{
    ExecutionPreview Build(RuntimeDecision decision);
}
