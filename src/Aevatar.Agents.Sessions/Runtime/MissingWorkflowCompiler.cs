using Aevatar.Agents.Sessions.Abstractions.Workflows;

namespace Aevatar.Agents.Sessions.Runtime;

internal sealed class MissingWorkflowCompiler : IWorkflowCompiler
{
    public WorkflowCompileResult Compile(WorkflowCompileRequest request)
    {
        return new WorkflowCompileResult(
            Ok: false,
            Definition: null,
            Errors: new[]
            {
                new WorkflowValidationError(
                    Code: "workflow.compiler_missing",
                    Message: "IWorkflowCompiler is not registered. Call services.AddCognitiveMeshWorkflowCompiler() (or provide your own implementation).")
            },
            NormalizedJson: null);
    }
}

