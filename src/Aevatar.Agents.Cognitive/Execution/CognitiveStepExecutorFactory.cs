using Aevatar.Agents.Cognitive.Agents;
using Aevatar.Agents.Cognitive.Primitives;

namespace Aevatar.Agents.Cognitive.Execution;

public static class CognitiveStepExecutorFactory
{
    public static CognitiveStepExecutor CreateForCoordinator(
        CognitiveCoordinatorGAgent coordinator,
        ICognitiveStepModule[] stepModules)
    {
        ArgumentNullException.ThrowIfNull(coordinator);

        var modules = stepModules ?? Array.Empty<ICognitiveStepModule>();
        var handlers = BuildHandlers(coordinator, modules);

        return new CognitiveStepExecutor(new CognitiveStepExecutorOptions
        {
            Logger = coordinator.CoordinatorLogger,
            TemplateEngine = coordinator.CoordinatorTemplateEngine,
            WorkflowVariables = coordinator.WorkflowVariables,
            StepHandlers = handlers,
            EmitStart = coordinator.EmitStepStart,
            EmitCompleted = coordinator.EmitStepCompleted,
            EmitError = coordinator.EmitStepError
        });
    }

    private static Dictionary<string, Func<StepDefinition, string?, string?, Task<PrimitiveResult>>> BuildHandlers(
        CognitiveCoordinatorGAgent coordinator,
        ICognitiveStepModule[] modules)
    {
        Func<StepDefinition, string?, string?, Task<PrimitiveResult>> WrapNoPrompt(
            Func<StepDefinition, Task<PrimitiveResult>> fallback)
            => (step, _, _) => Dispatch(modules, coordinator, step, fallback);

        Func<StepDefinition, string?, string?, Task<PrimitiveResult>> WrapLlm(
            Func<StepDefinition, string?, string?, Task<PrimitiveResult>> fallback)
            => (step, prompt, system) => Dispatch(modules, coordinator, step, prompt, system, fallback);

        return new Dictionary<string, Func<StepDefinition, string?, string?, Task<PrimitiveResult>>>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["llm_call"] = WrapLlm(coordinator.ExecuteLlmCallDirectAsync),
            ["conditional"] = WrapNoPrompt(coordinator.ExecuteConditionalAsync),
            ["fan_out"] = WrapNoPrompt(coordinator.ExecuteFanOutAsync),
            ["parallel"] = WrapNoPrompt(coordinator.ExecuteParallelAsync),
            ["vote"] = WrapNoPrompt(coordinator.ExecuteVoteAsync),
            ["tool_call"] = WrapNoPrompt(coordinator.ExecuteToolCallAsync),
            ["tool_validate"] = WrapNoPrompt(coordinator.ExecuteToolValidateAsync),
            ["tool_evolve"] = WrapNoPrompt(coordinator.ExecuteToolEvolveAsync),
            ["workflow_call"] = WrapNoPrompt(coordinator.ExecuteWorkflowCallAsync),
            ["checkpoint"] = WrapNoPrompt(coordinator.ExecuteCheckpointAsync),
            ["assign"] = WrapNoPrompt(coordinator.ExecuteAssignAsync),
            ["transform"] = WrapNoPrompt(coordinator.ExecuteTransformAsync),
            ["retrieve_facts"] = WrapNoPrompt(coordinator.ExecuteRetrieveFactsAsync),
            ["workspace_read_file"] = WrapNoPrompt(coordinator.ExecuteWorkspaceReadFileAsync),
            ["workspace_code_search"] = WrapNoPrompt(coordinator.ExecuteWorkspaceCodeSearchAsync),
            ["workspace_apply_patch"] = WrapNoPrompt(coordinator.ExecuteWorkspaceApplyPatchAsync),
            ["sandbox_command"] = WrapNoPrompt(coordinator.ExecuteSandboxCommandAsync)
        };
    }

    private static Task<PrimitiveResult> Dispatch(
        ICognitiveStepModule[] modules,
        CognitiveCoordinatorGAgent coordinator,
        StepDefinition step,
        Func<StepDefinition, Task<PrimitiveResult>> fallback)
    {
        foreach (var module in modules)
        {
            if (!module.CanHandle(step))
                continue;

            return module.ExecuteAsync(coordinator, step, null, null, CancellationToken.None);
        }

        return fallback(step);
    }

    private static Task<PrimitiveResult> Dispatch(
        ICognitiveStepModule[] modules,
        CognitiveCoordinatorGAgent coordinator,
        StepDefinition step,
        string? preRenderedPrompt,
        string? preRenderedSystem,
        Func<StepDefinition, string?, string?, Task<PrimitiveResult>> fallback)
    {
        foreach (var module in modules)
        {
            if (!module.CanHandle(step))
                continue;

            return module.ExecuteAsync(coordinator, step, preRenderedPrompt, preRenderedSystem, CancellationToken.None);
        }

        return fallback(step, preRenderedPrompt, preRenderedSystem);
    }
}
