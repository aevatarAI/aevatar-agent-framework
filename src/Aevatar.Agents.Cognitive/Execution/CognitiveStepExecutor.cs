using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Cognitive.Template;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Cognitive.Execution;

public sealed class CognitiveStepExecutor
{
    private readonly ILogger _logger;
    private readonly TemplateEngine _templateEngine;
    private readonly Dictionary<string, object> _workflowVariables;
    private readonly IReadOnlyDictionary<string, Func<StepDefinition, string?, string?, Task<PrimitiveResult>>> _stepHandlers;
    private readonly Action<StepDefinition, string?, string?> _emitStart;
    private readonly Action<StepDefinition, PrimitiveResult> _emitCompleted;
    private readonly Action<StepDefinition, Exception> _emitError;

    public CognitiveStepExecutor(CognitiveStepExecutorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = options.Logger ?? throw new ArgumentNullException(nameof(options.Logger));
        _templateEngine = options.TemplateEngine ?? throw new ArgumentNullException(nameof(options.TemplateEngine));
        _workflowVariables = options.WorkflowVariables ?? throw new ArgumentNullException(nameof(options.WorkflowVariables));
        _stepHandlers = options.StepHandlers ?? throw new ArgumentNullException(nameof(options.StepHandlers));
        _emitStart = options.EmitStart ?? throw new ArgumentNullException(nameof(options.EmitStart));
        _emitCompleted = options.EmitCompleted ?? throw new ArgumentNullException(nameof(options.EmitCompleted));
        _emitError = options.EmitError ?? throw new ArgumentNullException(nameof(options.EmitError));
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
            var stepType = (step.Type ?? string.Empty).Trim();
            if (!_stepHandlers.TryGetValue(stepType, out var handler))
            {
                return PrimitiveResult.Fail($"Unknown step type: {step.Type}");
            }

            var result = await handler(step, preRenderedPrompt, preRenderedSystem);

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

public sealed record CognitiveStepExecutorOptions
{
    public required ILogger Logger { get; init; }
    public required TemplateEngine TemplateEngine { get; init; }
    public required Dictionary<string, object> WorkflowVariables { get; init; }
    public required IReadOnlyDictionary<string, Func<StepDefinition, string?, string?, Task<PrimitiveResult>>> StepHandlers
    {
        get;
        init;
    }
    public required Action<StepDefinition, string?, string?> EmitStart { get; init; }
    public required Action<StepDefinition, PrimitiveResult> EmitCompleted { get; init; }
    public required Action<StepDefinition, Exception> EmitError { get; init; }
}
