using System;
using System.Collections.Generic;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Cognitive.Template;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Cognitive.Execution;

public interface IWorkflowCoordinatorRuntime
{
    Dictionary<string, object> WorkflowVariables { get; }
    bool TryGetWorkflowVariable(string key, out object? value);

    ILogger Logger { get; }
    TemplateEngine TemplateEngine { get; }

    void EmitStepStart(StepDefinition step, string? userPrompt, string? systemPrompt);
    void EmitStepCompleted(StepDefinition step, PrimitiveResult result);
    void EmitStepError(StepDefinition step, Exception ex);

    Task<PrimitiveResult> ExecuteLlmCallDirectAsync(StepDefinition step, string? preRenderedPrompt, string? preRenderedSystem);
    Task<PrimitiveResult> ExecuteConditionalAsync(StepDefinition step);
    Task<PrimitiveResult> ExecuteFanOutAsync(StepDefinition step);
    Task<PrimitiveResult> ExecuteParallelAsync(StepDefinition step);
    Task<PrimitiveResult> ExecuteVoteAsync(StepDefinition step);
    Task<PrimitiveResult> ExecuteToolCallAsync(StepDefinition step);
    Task<PrimitiveResult> ExecuteToolValidateAsync(StepDefinition step);
    Task<PrimitiveResult> ExecuteToolEvolveAsync(StepDefinition step);
    Task<PrimitiveResult> ExecuteWorkflowCallAsync(StepDefinition step);
    Task<PrimitiveResult> ExecuteCheckpointAsync(StepDefinition step);
    Task<PrimitiveResult> ExecuteAssignAsync(StepDefinition step);
    Task<PrimitiveResult> ExecuteTransformAsync(StepDefinition step);
    Task<PrimitiveResult> ExecuteRetrieveFactsAsync(StepDefinition step);
    Task<PrimitiveResult> ExecuteWorkspaceReadFileAsync(StepDefinition step);
    Task<PrimitiveResult> ExecuteWorkspaceCodeSearchAsync(StepDefinition step);
    Task<PrimitiveResult> ExecuteWorkspaceApplyPatchAsync(StepDefinition step);
    Task<PrimitiveResult> ExecuteSandboxCommandAsync(StepDefinition step);
}
