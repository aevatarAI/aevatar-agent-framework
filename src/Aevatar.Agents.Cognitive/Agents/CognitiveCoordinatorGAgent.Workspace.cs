using Aevatar.Agents.Cognitive.Execution;
using Aevatar.Agents.Cognitive.Primitives;

namespace Aevatar.Agents.Cognitive.Agents;

// ============================================================
//  Workspace primitives (coordinator-only)
//
//  WHY:
//  - Workers only support `llm_call` (tool calling disabled by design).
//  - Deterministic primitives must run in coordinator to remain auditable and bounded.
//
//  SECURITY:
//  - All filesystem/command side effects must be constrained by WorkspacePathGuard.
// ============================================================

public partial class CognitiveCoordinatorGAgent
{
    private WorkspaceReadFileExecutor? _workspaceReadFileExecutor;
    private WorkspaceCodeSearchExecutor? _workspaceCodeSearchExecutor;
    private WorkspaceApplyPatchExecutor? _workspaceApplyPatchExecutor;
    private SandboxCommandExecutor? _sandboxCommandExecutor;

    internal Task<PrimitiveResult> ExecuteWorkspaceReadFileAsync(StepDefinition step)
    {
        if (!WorkspacePathGuard.TryGetWorkspaceRoot(out var root, out var rootError))
            return Task.FromResult(PrimitiveResult.Fail(rootError!));

        _workspaceReadFileExecutor ??= new WorkspaceReadFileExecutor(_templateEngine, Logger);
        var result = _workspaceReadFileExecutor.Execute(step, _workflowVariables, root!);
        return Task.FromResult(result);
    }

    internal Task<PrimitiveResult> ExecuteWorkspaceCodeSearchAsync(StepDefinition step)
    {
        if (!WorkspacePathGuard.TryGetWorkspaceRoot(out var root, out var rootError))
            return Task.FromResult(PrimitiveResult.Fail(rootError!));

        _workspaceCodeSearchExecutor ??= new WorkspaceCodeSearchExecutor(_templateEngine, Logger);
        var result = _workspaceCodeSearchExecutor.Execute(step, _workflowVariables, root!);
        return Task.FromResult(result);
    }

    internal Task<PrimitiveResult> ExecuteWorkspaceApplyPatchAsync(StepDefinition step)
    {
        if (!WorkspacePathGuard.TryGetWorkspaceRoot(out var root, out var rootError))
            return Task.FromResult(PrimitiveResult.Fail(rootError!));

        _workspaceApplyPatchExecutor ??= new WorkspaceApplyPatchExecutor(_templateEngine, Logger);
        var result = _workspaceApplyPatchExecutor.Execute(step, _workflowVariables, root!);
        return Task.FromResult(result);
    }

    internal Task<PrimitiveResult> ExecuteSandboxCommandAsync(StepDefinition step)
    {
        if (!WorkspacePathGuard.TryGetWorkspaceRoot(out var root, out var rootError))
            return Task.FromResult(PrimitiveResult.Fail(rootError!));

        _sandboxCommandExecutor ??= new SandboxCommandExecutor(_templateEngine, Logger);
        var result = _sandboxCommandExecutor.Execute(step, _workflowVariables, root!);
        return Task.FromResult(result);
    }
}


