using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Cognitive.Template;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Cognitive.Execution.Run;

public sealed record WorkflowRunOptions
{
    public bool StopOnFailure { get; init; } = true;
}

public sealed record WorkflowRunFailure(string StepId, string StepType, string Error);

public sealed record WorkflowRunResult
{
    public required bool Success { get; init; }
    public required Dictionary<string, object> Variables { get; init; }
    public object? Output { get; init; }
    public IReadOnlyList<WorkflowRunFailure> Failures { get; init; } = Array.Empty<WorkflowRunFailure>();
    public object? ResultPayload { get; init; }
}

public sealed class WorkflowRunExecutor
{
    private readonly TemplateEngine _templateEngine;
    private readonly IReadOnlyList<IWorkflowRunStepModule> _modules;
    private readonly ILogger<WorkflowRunExecutor> _logger;

    public WorkflowRunExecutor(
        TemplateEngine templateEngine,
        IEnumerable<IWorkflowRunStepModule> modules,
        ILogger<WorkflowRunExecutor> logger)
    {
        _templateEngine = templateEngine ?? throw new ArgumentNullException(nameof(templateEngine));
        _modules = (modules ?? Array.Empty<IWorkflowRunStepModule>())
            .OrderBy(m => m.Priority)
            .ToList();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<WorkflowRunResult> ExecuteAsync(
        WorkflowDefinition workflow,
        WorkflowRunContext context,
        WorkflowRunOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(context);

        var opts = options ?? new WorkflowRunOptions();
        var emitter = new AgUiRunEventEmitter(context.Events);
        var failures = new List<WorkflowRunFailure>();

        var lockGate = context.RunLock;
        var lockAcquired = false;
        try
        {
            if (lockGate != null)
            {
                await lockGate.WaitAsync(ct);
                lockAcquired = true;
            }

            EnsureInputs(workflow, context.Variables);
            emitter.EmitRunStarted(context);

            _logger.LogInformation(
                "[WorkflowRun] start '{Name}' steps={Count} runId={RunId}",
                workflow.Name, workflow.Steps.Count, context.RunId);

            foreach (var step in workflow.Steps)
            {
                ct.ThrowIfCancellationRequested();

                var (prompt, system) = PreRender(step, context.Variables, _templateEngine);
                emitter.EmitStepStarted(context, step);

                PrimitiveResult result;
                try
                {
                    var module = ResolveModule(step);
                    if (module == null)
                    {
                        result = PrimitiveResult.Fail($"no step module for type '{step.Type}'");
                    }
                    else
                    {
                        result = await module.ExecuteAsync(context, step, prompt, system, ct);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    emitter.EmitStepError(context, step, ex);
                    result = PrimitiveResult.Fail(ex.Message);
                }

                emitter.EmitStepFinished(context, step, result);

                if (!result.Success)
                {
                    failures.Add(new WorkflowRunFailure(step.Id ?? string.Empty, step.Type ?? string.Empty, result.Error ?? string.Empty));
                    if (opts.StopOnFailure)
                        break;
                }

                StoreStepOutput(step, result, context.Variables, _logger);
            }

            var output = BuildOutput(workflow, context.Variables, _templateEngine);
            context.Variables["_output"] = output;

            var ok = failures.Count == 0;
            var runResult = new WorkflowRunResult
            {
                Success = ok,
                Variables = context.Variables,
                Output = output,
                Failures = failures
            };

            emitter.EmitRunFinished(context, runResult);
            return runResult;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            emitter.EmitRunError(context, new OperationCanceledException("workflow run canceled"));
            throw;
        }
        catch (Exception ex)
        {
            emitter.EmitRunError(context, ex);
            return new WorkflowRunResult
            {
                Success = false,
                Variables = context.Variables,
                Output = context.Variables.GetValueOrDefault("_output"),
                Failures = failures
            };
        }
        finally
        {
            if (lockGate != null && lockAcquired)
                lockGate.Release();
        }
    }

    private IWorkflowRunStepModule? ResolveModule(StepDefinition step)
    {
        foreach (var module in _modules)
        {
            if (module.CanHandle(step))
                return module;
        }
        return null;
    }

    private static void EnsureInputs(WorkflowDefinition workflow, Dictionary<string, object> variables)
    {
        foreach (var input in workflow.Inputs)
        {
            var key = (input.Name ?? string.Empty).Trim();
            if (key.Length == 0) continue;

            if (!variables.ContainsKey(key) && input.DefaultValue != null)
            {
                variables[key] = input.DefaultValue;
            }
        }
    }

    private static (string? Prompt, string? System) PreRender(
        StepDefinition step,
        Dictionary<string, object> variables,
        TemplateEngine template)
    {
        if (!string.Equals(step.Type, "llm_call", StringComparison.OrdinalIgnoreCase))
            return (null, null);

        var rawPrompt = step.Parameters.GetValueOrDefault("prompt")?.ToString() ?? string.Empty;
        var rawSystem = step.Parameters.GetValueOrDefault("system")?.ToString();

        var prompt = template.Render(rawPrompt, variables);
        var system = rawSystem == null ? null : template.Render(rawSystem, variables);
        return (prompt, system);
    }

    private static void StoreStepOutput(
        StepDefinition step,
        PrimitiveResult result,
        Dictionary<string, object> variables,
        ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(step.Store))
            return;

        if (result.Value == null)
        {
            logger.LogWarning(
                "[WorkflowRun] step '{StepId}' result null; skip store '{Store}'",
                step.Id, step.Store);
            return;
        }

        variables[step.Store] = result.Value;
    }

    private static object BuildOutput(
        WorkflowDefinition workflow,
        Dictionary<string, object> variables,
        TemplateEngine template)
    {
        if (workflow.Output.Count == 0)
            return variables;

        var output = new Dictionary<string, object?>();
        foreach (var (key, expression) in workflow.Output)
        {
            output[key] = template.ResolveValue(expression, variables);
        }

        return output;
    }
}
