using Aevatar.Agents.Cognitive.Primitives;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Cognitive.Execution;

public sealed class WorkflowOrchestrator
{
    private readonly ILogger _logger;
    private readonly Dictionary<string, object> _variables;
    private readonly Func<StepDefinition, Task<PrimitiveResult>> _executeStep;
    private readonly Action<StepDefinition>? _beforeStep;
    private readonly Func<Dictionary<string, string>, object> _buildOutput;

    public WorkflowOrchestrator(
        ILogger logger,
        Dictionary<string, object> variables,
        Func<StepDefinition, Task<PrimitiveResult>> executeStep,
        Action<StepDefinition>? beforeStep,
        Func<Dictionary<string, string>, object> buildOutput)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _variables = variables ?? throw new ArgumentNullException(nameof(variables));
        _executeStep = executeStep ?? throw new ArgumentNullException(nameof(executeStep));
        _beforeStep = beforeStep;
        _buildOutput = buildOutput ?? throw new ArgumentNullException(nameof(buildOutput));
    }

    public async Task ExecuteAsync(WorkflowDefinition workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        _logger.LogInformation("[DEBUG][Workflow] Executing workflow '{Name}' with {Count} steps: [{Steps}]",
            workflow.Name, workflow.Steps.Count, string.Join(", ", workflow.Steps.Select(s => s.Id)));

        for (int stepIndex = 0; stepIndex < workflow.Steps.Count; stepIndex++)
        {
            var step = workflow.Steps[stepIndex];
            _logger.LogInformation("[DEBUG][Workflow] >>> Executing step {Index}/{Total}: '{StepId}' (type={Type})",
                stepIndex + 1, workflow.Steps.Count, step.Id, step.Type);

            _beforeStep?.Invoke(step);

            var result = await _executeStep(step);

            if (!result.Success)
            {
                throw new Exception($"Step '{step.Id}' failed: {result.Error}");
            }

            if (!string.IsNullOrEmpty(step.Store))
            {
                _logger.LogInformation(
                    "[DEBUG][Workflow] Step '{StepId}' store='{Store}', result.Value is null: {IsNull}",
                    step.Id, step.Store, result.Value == null);

                if (result.Value != null)
                {
                    _variables[step.Store] = result.Value;
                    _logger.LogInformation("[DEBUG][Workflow] Stored '{Store}' type: {Type}",
                        step.Store, result.Value.GetType().FullName);
                }
                else
                {
                    _logger.LogWarning("[DEBUG][Workflow] ⚠️ Step '{StepId}' returned null, NOT storing to '{Store}'",
                        step.Id, step.Store);
                }
            }
        }

        _variables["_output"] = _buildOutput(workflow.Output);
    }
}
