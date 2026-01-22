using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Cognitive.Template;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Cognitive.Execution;

public sealed class CognitiveStepExecutor
{
    private readonly ILogger _logger;
    private readonly TemplateEngine _templateEngine;
    private readonly Dictionary<string, object> _workflowVariables;
    private readonly Func<StepDefinition, string?, string?, Task<PrimitiveResult>> _executeLlmCall;
    private readonly Func<StepDefinition, Task<PrimitiveResult>> _executeConditional;
    private readonly Func<StepDefinition, Task<PrimitiveResult>> _executeFanOut;
    private readonly Func<StepDefinition, Task<PrimitiveResult>> _executeParallel;
    private readonly Func<StepDefinition, Task<PrimitiveResult>> _executeVote;
    private readonly Func<StepDefinition, Task<PrimitiveResult>> _executeWorkflowCall;
    private readonly Func<StepDefinition, Task<PrimitiveResult>> _executeCheckpoint;
    private readonly Func<StepDefinition, Task<PrimitiveResult>> _executeAssign;
    private readonly Func<StepDefinition, Task<PrimitiveResult>> _executeTransform;
    private readonly Func<StepDefinition, Task<PrimitiveResult>> _executeRetrieveFacts;
    private readonly Func<StepDefinition, Task<PrimitiveResult>> _executeWorkspaceReadFile;
    private readonly Func<StepDefinition, Task<PrimitiveResult>> _executeWorkspaceCodeSearch;
    private readonly Func<StepDefinition, Task<PrimitiveResult>> _executeWorkspaceApplyPatch;
    private readonly Func<StepDefinition, Task<PrimitiveResult>> _executeSandboxCommand;
    private readonly Action<StepDefinition, string?, string?> _emitStart;
    private readonly Action<StepDefinition, PrimitiveResult> _emitCompleted;
    private readonly Action<StepDefinition, Exception> _emitError;

    public CognitiveStepExecutor(
        ILogger logger,
        TemplateEngine templateEngine,
        Dictionary<string, object> workflowVariables,
        Func<StepDefinition, string?, string?, Task<PrimitiveResult>> executeLlmCall,
        Func<StepDefinition, Task<PrimitiveResult>> executeConditional,
        Func<StepDefinition, Task<PrimitiveResult>> executeFanOut,
        Func<StepDefinition, Task<PrimitiveResult>> executeParallel,
        Func<StepDefinition, Task<PrimitiveResult>> executeVote,
        Func<StepDefinition, Task<PrimitiveResult>> executeWorkflowCall,
        Func<StepDefinition, Task<PrimitiveResult>> executeCheckpoint,
        Func<StepDefinition, Task<PrimitiveResult>> executeAssign,
        Func<StepDefinition, Task<PrimitiveResult>> executeTransform,
        Func<StepDefinition, Task<PrimitiveResult>> executeRetrieveFacts,
        Func<StepDefinition, Task<PrimitiveResult>> executeWorkspaceReadFile,
        Func<StepDefinition, Task<PrimitiveResult>> executeWorkspaceCodeSearch,
        Func<StepDefinition, Task<PrimitiveResult>> executeWorkspaceApplyPatch,
        Func<StepDefinition, Task<PrimitiveResult>> executeSandboxCommand,
        Action<StepDefinition, string?, string?> emitStart,
        Action<StepDefinition, PrimitiveResult> emitCompleted,
        Action<StepDefinition, Exception> emitError)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _templateEngine = templateEngine ?? throw new ArgumentNullException(nameof(templateEngine));
        _workflowVariables = workflowVariables ?? throw new ArgumentNullException(nameof(workflowVariables));
        _executeLlmCall = executeLlmCall ?? throw new ArgumentNullException(nameof(executeLlmCall));
        _executeConditional = executeConditional ?? throw new ArgumentNullException(nameof(executeConditional));
        _executeFanOut = executeFanOut ?? throw new ArgumentNullException(nameof(executeFanOut));
        _executeParallel = executeParallel ?? throw new ArgumentNullException(nameof(executeParallel));
        _executeVote = executeVote ?? throw new ArgumentNullException(nameof(executeVote));
        _executeWorkflowCall = executeWorkflowCall ?? throw new ArgumentNullException(nameof(executeWorkflowCall));
        _executeCheckpoint = executeCheckpoint ?? throw new ArgumentNullException(nameof(executeCheckpoint));
        _executeAssign = executeAssign ?? throw new ArgumentNullException(nameof(executeAssign));
        _executeTransform = executeTransform ?? throw new ArgumentNullException(nameof(executeTransform));
        _executeRetrieveFacts = executeRetrieveFacts ?? throw new ArgumentNullException(nameof(executeRetrieveFacts));
        _executeWorkspaceReadFile = executeWorkspaceReadFile ?? throw new ArgumentNullException(nameof(executeWorkspaceReadFile));
        _executeWorkspaceCodeSearch = executeWorkspaceCodeSearch ?? throw new ArgumentNullException(nameof(executeWorkspaceCodeSearch));
        _executeWorkspaceApplyPatch = executeWorkspaceApplyPatch ?? throw new ArgumentNullException(nameof(executeWorkspaceApplyPatch));
        _executeSandboxCommand = executeSandboxCommand ?? throw new ArgumentNullException(nameof(executeSandboxCommand));
        _emitStart = emitStart ?? throw new ArgumentNullException(nameof(emitStart));
        _emitCompleted = emitCompleted ?? throw new ArgumentNullException(nameof(emitCompleted));
        _emitError = emitError ?? throw new ArgumentNullException(nameof(emitError));
    }

    public async Task<PrimitiveResult> ExecuteAsync(StepDefinition step)
    {
        string? preRenderedPrompt = null;
        string? preRenderedSystem = null;

        if (step.Type == "llm_call")
        {
            var rawPrompt = step.Parameters.GetValueOrDefault("prompt")?.ToString() ?? "";
            var rawSystem = step.Parameters.GetValueOrDefault("system")?.ToString();

            preRenderedPrompt = _templateEngine.Render(rawPrompt, _workflowVariables);
            if (rawSystem != null)
                preRenderedSystem = _templateEngine.Render(rawSystem, _workflowVariables);

            if (rawPrompt.Contains("{{task}}") &&
                string.IsNullOrWhiteSpace(_workflowVariables.GetValueOrDefault("task")?.ToString()))
            {
                _logger.LogWarning("[{Step}] WARNING: task variable is empty! Available vars: {Vars}",
                    step.Id, string.Join(", ", _workflowVariables.Keys));
            }
        }

        _emitStart(step, preRenderedPrompt, preRenderedSystem);

        try
        {
            var result = step.Type switch
            {
                "llm_call" => await _executeLlmCall(step, preRenderedPrompt, preRenderedSystem),
                "conditional" => await _executeConditional(step),
                "fan_out" => await _executeFanOut(step),
                "parallel" => await _executeParallel(step),
                "vote" => await _executeVote(step),
                "workflow_call" => await _executeWorkflowCall(step),
                "checkpoint" => await _executeCheckpoint(step),
                "assign" => await _executeAssign(step),
                "transform" => await _executeTransform(step),
                "retrieve_facts" => await _executeRetrieveFacts(step),
                "workspace_read_file" => await _executeWorkspaceReadFile(step),
                "workspace_code_search" => await _executeWorkspaceCodeSearch(step),
                "workspace_apply_patch" => await _executeWorkspaceApplyPatch(step),
                "sandbox_command" => await _executeSandboxCommand(step),
                _ => PrimitiveResult.Fail($"Unknown step type: {step.Type}")
            };

            _emitCompleted(step, result);
            return result;
        }
        catch (Exception ex)
        {
            _emitError(step, ex);
            throw;
        }
    }
}
