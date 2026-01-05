using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Cognitive.Execution;
using Aevatar.Agents.Cognitive.Engine;
using Aevatar.Agents.Cognitive.Messages;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Cognitive.Template;
using Aevatar.Agents.Cognitive.Utilities;
using Aevatar.Agents.Maker;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

using WorkflowDefinition = Aevatar.Agents.Cognitive.Primitives.WorkflowDefinition;
using StepDefinition = Aevatar.Agents.Cognitive.Primitives.StepDefinition;
using VoteResult = Aevatar.Agents.Maker.VoteResult;

namespace Aevatar.Agents.Cognitive.Agents;

// ============================================================
//  Cognitive Coordinator Agent
//  DSL Workflow Coordinator - True Distributed Parallelism
// ============================================================

/// <summary>
/// Cognitive Coordinator Agent - Workflow Coordinator
/// 
/// Parallelism model:
/// - Simple steps (single LLM call): Coordinator executes directly
/// - Parallel steps (fan_out): Distributed to Worker Actors via Protobuf events
/// 
/// This follows MAKER design pattern:
/// - MakerCoordinatorGAgent → CognitiveCoordinatorGAgent
/// - MakerWorkerGAgent → CognitiveWorkerGAgent
/// </summary>
public partial class CognitiveCoordinatorGAgent : CognitiveAIGAgentBase<CognitiveCoordinatorState>
{
    // ============================================================
    //  Components
    // ============================================================

    private readonly TemplateEngine _templateEngine = new();
    private readonly OutputParserFactory _parserFactory = new();
    private readonly InMemoryWorkflowRegistry _workflowRegistry = new();

    // ============================================================
    //  Runtime State
    // ============================================================

    private readonly Dictionary<string, object> _workflowVariables = new();

    // Fan-out result collection
    private readonly ConcurrentDictionary<string, StepCompletedEventProto> _collectedResults = new();
    private readonly ConcurrentDictionary<string, string> _fanOutChildTypes = new();
    private readonly ConcurrentDictionary<string, string> _fanOutUserPrompts = new();
    private readonly ConcurrentDictionary<string, string> _fanOutSystemPrompts = new();
    private readonly ConcurrentDictionary<string, string> _fanOutOutputTypes = new();
    private int _expectedResults;
    private TaskCompletionSource<bool>? _fanOutCompletionSource;
    private StepDefinition? _currentFanOutStep;

    // Worker management (injected externally)
    private IGAgentActorManager? _actorManager;
    private readonly List<string> _workerIds = [];
    
    // Semantic clustering voting (optional)
    private IEmbeddingGenerator<string, Embedding<float>>? _embeddingGenerator;
    private float _semanticSimilarityThreshold = 0.85f;

    // Red-Flagging (optional, pluggable)
    private IRedFlagStrategy? _redFlagStrategy;
    private IRedFlagHandler _redFlagHandler = new DefaultRedFlagHandler();

    // Token-free primitives
    private TransformExecutor? _transformExecutor;
    private RetrieveFactsExecutor? _retrieveFactsExecutor;
    private HpaExecutor? _hpaExecutor;

    // Step event tracking (for frontend visualization)
    private readonly List<WorkflowStepEvent> _stepEvents = [];
    private readonly ConcurrentDictionary<string, DateTime> _stepStartTimes = new();
    private Action<WorkflowStepEvent>? _onStepEvent;
    private readonly object _stepEventsLock = new(); // May have concurrent writes during vote parallel generation
    private readonly object _statsLock = new(); // Accumulate statistics under multiple parallel tasks, avoid loss/confusion
    
    // ============================================================
    //  Constructor
    // ============================================================

    public CognitiveCoordinatorGAgent()
    {
    }

    protected override string AgentKind => "cognitive_coordinator";

    protected override void AppendAgentHistoryMetadata(Dictionary<string, string> metadata)
    {
        metadata["execution_id"] = CustomState.ExecutionId ?? string.Empty;
    }

    // ============================================================
    //  Lifecycle
    // ============================================================

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        // ============================================================
        //  Recursion depth limit (default value)
        //
        //  NOTE:
        //  - Final limit should be determined by workflow input variable `max_depth` (see HandleStartWorkflowRequest).
        //  - This only provides a "startup default value" to avoid infinite recursion when not configured.
        // ============================================================
        CustomState.MaxDepth = 50;
        CustomState.Status = ExecutionStatus.EsPending;

        // Enable Red-Flag strategy by default, limit max content length to 102400
        _redFlagStrategy ??= new DefaultEnglishRedFlagStrategy(new RedFlagOptions
        {
            MaxContentLength = 102400,
            EnableLengthValidation = true
        });
        
        Logger.LogDebug("CognitiveCoordinatorGAgent activated. Id={Id}", Id);
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult(
            $"CognitiveCoordinator [{CustomState.ExecutionId}] - " +
            $"Phase: {CustomState.CurrentPhase}, Workers: {_workerIds.Count}");
    }

    // ============================================================
    //  Public API
    // ============================================================

    /// <summary>
    /// Set Actor Manager (for creating Workers)
    /// </summary>
    public void SetActorManager(IGAgentActorManager actorManager)
    {
        _actorManager = actorManager;
    }

    /// <summary>
    /// Set Embedding Generator (for semantic clustering voting)
    /// If not set, voting will use exact hash matching
    /// </summary>
    public void SetEmbeddingGenerator(
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator,
        float semanticSimilarityThreshold = 0.85f)
    {
        _embeddingGenerator = embeddingGenerator;
        _semanticSimilarityThreshold = Math.Clamp(semanticSimilarityThreshold, 0.1f, 1.0f);
    }

    /// <summary>
    /// Set Red-Flag strategy (for filtering invalid LLM responses)
    /// Pluggable design:
    /// - null: Disable Red-Flag detection (default)
    /// - DefaultEnglishRedFlagStrategy: English content validation
    /// - ChineseRedFlagStrategy: Chinese content validation
    /// - CodeAwareRedFlagStrategy: Code content validation
    /// - Custom implementation of IRedFlagStrategy
    /// </summary>
    public void SetRedFlagStrategy(IRedFlagStrategy? strategy, IRedFlagHandler? handler = null)
    {
        _redFlagStrategy = strategy;
        _redFlagHandler = handler ?? new DefaultRedFlagHandler();

        Logger.LogInformation(
            "Red-Flag strategy configured: {Strategy}",
            strategy?.GetType().Name ?? "disabled");
    }

    /// <summary>
    /// Create Worker pool
    /// </summary>
    public async Task CreateWorkerPoolAsync(int poolSize = 5, IReadOnlyList<Guid>? workerIds = null)
    {
        if (_actorManager == null)
        {
            throw new InvalidOperationException("ActorManager not set. Call SetActorManager first.");
        }

        _workerIds.Clear();

        // Worker needs the same LLM provider as coordinator.
        // Coordinator is expected to be initialized by CognitiveStrategy before creating the pool.
        var providerName = ActiveProviderConfig?.Name;
        if (string.IsNullOrWhiteSpace(providerName))
        {
            Logger.LogWarning("Coordinator is not initialized with an LLM provider yet. Workers will NOT be initialized and fan_out will fail.");
        }

        // Stable worker ids (optional):
        // - When provided, this enables reconnect-friendly deterministic identity.
        // - When not provided, fall back to random ids (old behavior).
        var ids = new List<Guid>(capacity: Math.Max(0, poolSize));
        if (workerIds != null && workerIds.Count > 0)
        {
            for (var i = 0; i < workerIds.Count && ids.Count < poolSize; i++)
            {
                ids.Add(workerIds[i]);
            }
        }

        while (ids.Count < poolSize)
        {
            ids.Add(Guid.NewGuid());
        }

        for (var i = 0; i < ids.Count; i++)
        {
            // Create Worker Actor
            var rawWorkerId = ids[i].ToString("D");
            var workerActor = await _actorManager.CreateAndRegisterAsync<CognitiveWorkerGAgent>(rawWorkerId);
            var workerActorId = workerActor.Id; // Normalized full ActorId: "CognitiveWorkerGAgent:RawId"

            if (workerActor.GetAgent() is CognitiveWorkerGAgent worker)
            {
                // Reuse AIGAgentBase history switch (default off)
                worker.EnableChatHistoryInState = EnableChatHistoryInState;
                worker.EnableChatHistoryCompaction = EnableChatHistoryCompaction;
                worker.ChatHistoryMaxMessages = ChatHistoryMaxMessages;
                worker.ChatHistorySummaryMaxChars = ChatHistorySummaryMaxChars;
                worker.ArchiveCompactedHistoryToAIMemory = ArchiveCompactedHistoryToAIMemory;

                // Initialize Worker's LLM Provider (otherwise Worker.LLMProvider will throw exception)
                if (!string.IsNullOrWhiteSpace(providerName))
                {
                    await worker.InitializeAsync(providerName!, cancellationToken: CancellationToken.None);
                }
            }

            // Set parent-child relationship (Worker subscribes to Coordinator's stream)
            await _actorManager.LinkParentChildAsync(Id, workerActorId);

            _workerIds.Add(workerActorId);
        }

        CustomState.ActiveWorkers = _workerIds.Count;

        Logger.LogInformation("Created worker pool with {Count} workers", poolSize);
    }

    /// <summary>
    /// Register workflow
    /// </summary>
    public void RegisterWorkflow(WorkflowDefinition workflow)
    {
        _workflowRegistry.Register(workflow);
    }

    /// <summary>
    /// Get execution result
    /// </summary>
    public WorkflowResult GetResult() => new()
    {
        Success = CustomState.Status == ExecutionStatus.EsCompleted,
        Output = _workflowVariables.GetValueOrDefault("_output"),
        Error = string.IsNullOrWhiteSpace(CustomState.Error) ? null : CustomState.Error,
        TotalTokens = CustomState.TotalTokensUsed,
        TotalLlmCalls = CustomState.TotalLlmCalls
    };

    // ============================================================
    //  Event Handling
    // ============================================================

    private async Task<PrimitiveResult> ExecuteStepAsync(StepDefinition step)
    {
        // Pre-render prompt for start event (ensure WORKERS panel can display complete prompt)
        string? preRenderedPrompt = null;
        string? preRenderedSystem = null;

        if (step.Type == "llm_call")
        {
            var rawPrompt = step.Parameters.GetValueOrDefault("prompt")?.ToString() ?? "";
            var rawSystem = step.Parameters.GetValueOrDefault("system")?.ToString();

            preRenderedPrompt = _templateEngine.Render(rawPrompt, _workflowVariables);
            if (rawSystem != null)
                preRenderedSystem = _templateEngine.Render(rawSystem, _workflowVariables);

            // Debug: If task variable is empty, log warning
            if (rawPrompt.Contains("{{task}}") &&
                string.IsNullOrWhiteSpace(_workflowVariables.GetValueOrDefault("task")?.ToString()))
            {
                Logger.LogWarning("[{Step}] WARNING: task variable is empty! Available vars: {Vars}",
                    step.Id, string.Join(", ", _workflowVariables.Keys));
            }
        }

        // Send start event (includes pre-rendered prompt)
        EmitStepEvent(step, StepStatus.Running,
            userPrompt: preRenderedPrompt,
            systemPrompt: preRenderedSystem);

        try
        {
            var result = step.Type switch
            {
                // Simple step: Coordinator executes directly (pass pre-rendered prompt to avoid duplicate rendering)
                "llm_call" => await ExecuteLlmCallDirectAsync(step, preRenderedPrompt, preRenderedSystem),
                "conditional" => await ExecuteConditionalAsync(step),

                // Parallel step: Distribute to Workers (true Actor parallelism)
                "fan_out" => await ExecuteFanOutAsync(step),
                "parallel" => await ExecuteParallelAsync(step),

                // Others
                "vote" => await ExecuteVoteAsync(step),
                "workflow_call" => await ExecuteWorkflowCallAsync(step),
                "checkpoint" => await ExecuteCheckpointAsync(step),
                "assign" => await ExecuteAssignAsync(step),
                "transform" => await ExecuteTransformAsync(step),
                "retrieve_facts" => await ExecuteRetrieveFactsAsync(step),
                "hpa" => await ExecuteHpaAsync(step),

                _ => PrimitiveResult.Fail($"Unknown step type: {step.Type}")
            };

            // Send completion/failure event (includes conversation history)
            EmitStepEvent(step,
                result.Success ? StepStatus.Completed : StepStatus.Failed,
                result.Success ? null : result.Error,
                progress: 1.0f,
                systemPrompt: result.SystemPrompt,
                userPrompt: result.UserPrompt,
                assistantResponse: result.AssistantResponse);

            return result;
        }
        catch (Exception ex)
        {
            EmitStepEvent(step, StepStatus.Failed, ex.Message);
            throw;
        }
    }

    private Task<PrimitiveResult> ExecuteTransformAsync(StepDefinition step)
    {
        _transformExecutor ??= new TransformExecutor(_templateEngine, Logger);
        var result = _transformExecutor.Execute(step, _workflowVariables);
        return Task.FromResult(result);
    }

    private Task<PrimitiveResult> ExecuteRetrieveFactsAsync(StepDefinition step)
    {
        _retrieveFactsExecutor ??= new RetrieveFactsExecutor(_templateEngine, Logger);
        var result = _retrieveFactsExecutor.Execute(step, _workflowVariables);
        return Task.FromResult(result);
    }

    private Task<PrimitiveResult> ExecuteHpaAsync(StepDefinition step)
    {
        // ============================================================
        //  HPA (token-free, coordinator-only)
        //
        //  WHY:
        //  - Extract HPA's "computable geometric layer" from prompt
        //  - Allow workflow to use deterministic metrics for routing/scheduling, instead of letting LLM tell stories
        // ============================================================
        _hpaExecutor ??= new HpaExecutor(_templateEngine, Logger);
        var result = _hpaExecutor.Execute(step, _workflowVariables);
        return Task.FromResult(result);
    }

    // ============================================================
    //  Other Steps
    // ============================================================

    private async Task<PrimitiveResult> ExecuteConditionalAsync(StepDefinition step)
    {
        var conditionExpr = step.Condition ?? "false";
        object? conditionResult;
        string? evalError = null;
        try
        {
            conditionResult = _templateEngine.Evaluate(conditionExpr, _workflowVariables);
        }
        catch (Exception ex)
        {
            // IMPORTANT:
            // - conditional is a critical control flow node (stop_or_continue / ensure_state)
            // - Throwing exception here will directly FailExecutionAsync, manifesting as "system completely stopped"
            // - Strategy: Default to if_false (continue execution), and record the error (visible in UI/logs)
            conditionResult = false;
            evalError = $"conditional-eval-error: {ex.Message}";
            Logger.LogWarning(ex, "[Conditional] Evaluate failed at step {StepId}: {Expr}", step.Id, conditionExpr);
        }

        var isTrue = conditionResult switch
        {
            bool b => b,
            string s => s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "yes",
            int i => i != 0,
            double d => d != 0,
            _ => conditionResult != null
        };

        var branch = isTrue ? step.IfTrue : step.IfFalse;
        if (branch == null || branch.Count == 0)
        {
            // Even if evaluation failed, do not fail the workflow here.
            if (evalError != null) _workflowVariables["_last_conditional_error"] = evalError;
            return PrimitiveResult.Ok();
        }

        // Execute branch
        Logger.LogInformation("[DEBUG][Conditional] Executing branch with {Count} steps: [{Steps}]",
            branch.Count, string.Join(", ", branch.Select(s => s.Id)));

        PrimitiveResult? lastResult = null;
        for (int i = 0; i < branch.Count; i++)
        {
            var childStep = branch[i];
            Logger.LogInformation(
                "[DEBUG][Conditional] >>> Executing branch step {Index}/{Total}: '{StepId}' (type={Type})",
                i + 1, branch.Count, childStep.Id, childStep.Type);

            lastResult = await ExecuteStepAsync(childStep);

            Logger.LogInformation(
                "[DEBUG][Conditional] <<< Step '{StepId}' result: Success={Success}, Error={Error}, Value is null={IsNull}",
                childStep.Id, lastResult.Success, lastResult.Error ?? "none", lastResult.Value == null);

            if (!lastResult.Success)
            {
                Logger.LogWarning("[DEBUG][Conditional] ⚠️ Step '{StepId}' failed, breaking out of branch",
                    childStep.Id);
                break;
            }

            if (!string.IsNullOrEmpty(childStep.Store) && lastResult.Value != null)
            {
                _workflowVariables[childStep.Store] = lastResult.Value;
                Logger.LogInformation("[DEBUG][Conditional] Stored '{Store}' = {Type}",
                    childStep.Store, lastResult.Value.GetType().FullName);
            }
        }

        Logger.LogInformation("[DEBUG][Conditional] Branch execution completed");
        // If we had an eval error but branch executed successfully, keep the workflow moving.
        if (evalError != null && lastResult is { Success: true })
        {
            _workflowVariables["_last_conditional_error"] = evalError;
            // Preserve success & value; attach debug info through variables (visible in logs if needed)
            return PrimitiveResult.Ok(lastResult.Value, lastResult.TokensUsed, lastResult.LlmCalls);
        }
        if (evalError != null) _workflowVariables["_last_conditional_error"] = evalError;
        return lastResult ?? PrimitiveResult.Ok(null);
    }

    // NOTE: vote moved to `CognitiveCoordinatorGAgent.Vote.cs`
    // NOTE: parsing/parameters moved to `CognitiveCoordinatorGAgent.Parameters.cs`

    private async Task<PrimitiveResult> ExecuteWorkflowCallAsync(StepDefinition step)
    {
        var workflowName = step.Workflow ?? "";
        var workflow = _workflowRegistry.Get(workflowName);

        if (workflow == null)
        {
            return PrimitiveResult.Fail($"Workflow '{workflowName}' not found");
        }

        // ============================================================
        //  Allow overriding max_depth for single workflow_call
        //
        //  WHY:
        //  - YAML supports both workflow input max_depth and passing max_depth in workflow_call.params
        //  - Coordinator's internal workflow_call execution doesn't go through StartWorkflowRequest, so need explicit handling here
        // ============================================================
        var savedMaxDepth = CustomState.MaxDepth;

        // Check recursion depth
        CustomState.CurrentDepth++;
        if (CustomState.CurrentDepth > CustomState.MaxDepth)
        {
            CustomState.CurrentDepth--;
            return PrimitiveResult.Fail($"Max recursion depth {CustomState.MaxDepth} exceeded");
        }

        try
        {
            // Build child context
            var childVariables = new Dictionary<string, object>(_workflowVariables);
            if (step.Params != null)
            {
                foreach (var (key, value) in step.Params)
                {
                    if (value != null)
                    {
                        var resolved = _templateEngine.ResolveValue(value, _workflowVariables);
                        if (resolved != null)
                            childVariables[key] = resolved;
                    }
                }
            }

            // Allow child call to override with params.max_depth (only effective for this call)
            if (TryGetPositiveInt(childVariables, "max_depth", out var callMaxDepth))
            {
                CustomState.MaxDepth = Math.Clamp(callMaxDepth, 1, 200);
            }

            // Temporarily replace variables
            var savedVariables = new Dictionary<string, object>(_workflowVariables);
            _workflowVariables.Clear();
            foreach (var (key, value) in childVariables)
            {
                _workflowVariables[key] = value;
            }

            // Execute child workflow
            await ExecuteWorkflowAsync(workflow);

            var output = _workflowVariables.GetValueOrDefault("_output");

            // DEBUG: Check child workflow output
            Logger.LogInformation("[DEBUG][WorkflowCall] _output is null: {IsNull}, type: {Type}",
                output == null, output?.GetType().FullName ?? "null");
            if (output is Dictionary<string, object?> dict)
            {
                foreach (var (k, v) in dict)
                {
                    Logger.LogInformation("[DEBUG][WorkflowCall] _output[{Key}] = {Value}",
                        k, v?.ToString()?.Substring(0, Math.Min(100, v?.ToString()?.Length ?? 0)) ?? "null");
                }
            }

            // Restore variables
            _workflowVariables.Clear();
            foreach (var (key, value) in savedVariables)
            {
                _workflowVariables[key] = value;
            }

            return PrimitiveResult.Ok(output);
        }
        finally
        {
            // Restore maxDepth before this call, avoid polluting outer workflow
            CustomState.MaxDepth = savedMaxDepth;
            CustomState.CurrentDepth--;
        }
    }

    // ============================================================
    //  Utility: Parse positive integer from variable table
    // ============================================================
    private static bool TryGetPositiveInt(
        Dictionary<string, object> variables,
        string key,
        out int value)
    {
        value = 0;
        if (!variables.TryGetValue(key, out var raw) || raw == null) return false;

        try
        {
            value = raw switch
            {
                int i => i,
                long l => (int)l,
                float f => (int)f,
                double d => (int)d,
                decimal m => (int)m,
                string s when int.TryParse(s, out var parsed) => parsed,
                _ => Convert.ToInt32(raw)
            };
        }
        catch
        {
            value = 0;
            return false;
        }

        return value > 0;
    }

    private Task<PrimitiveResult> ExecuteCheckpointAsync(StepDefinition step)
    {
        Logger.LogDebug("Checkpoint at step: {StepId}", step.Id);
        return Task.FromResult(PrimitiveResult.Ok(null));
    }

    // ============================================================
    //  assign: variable copy / projection
    //
    //  WHY:
    //  - workflow_call returns an object (dictionary) but DSL lacks a cheap "set var" primitive.
    //  - for theorem-loop recursion we need: state = recursive_output.state (without an extra LLM call).
    //
    //  DSL:
    //    - id: unwrap
    //      type: assign
    //      from: "recursive_output.state"
    //      store: state
    // ============================================================
    private Task<PrimitiveResult> ExecuteAssignAsync(StepDefinition step)
    {
        var from = step.Parameters.GetValueOrDefault("from")?.ToString();
        if (string.IsNullOrWhiteSpace(from))
            return Task.FromResult(PrimitiveResult.Fail("assign requires 'from'"));

        if (string.IsNullOrWhiteSpace(step.Store))
            return Task.FromResult(PrimitiveResult.Fail("assign requires 'store' as target variable name"));

        var value = ResolvePathValue(_workflowVariables, from!);
        if (value == null)
            return Task.FromResult(PrimitiveResult.Fail($"assign source '{from}' resolved to null"));

        _workflowVariables[step.Store!] = value;
        return Task.FromResult(PrimitiveResult.Ok(value));
    }

    private static object? ResolvePathValue(Dictionary<string, object> variables, string path)
    {
        // supports dotted paths: "a.b.c"
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;

        if (!variables.TryGetValue(parts[0], out var current) || current == null)
            return null;

        for (var i = 1; i < parts.Length; i++)
        {
            var key = parts[i];
            current = current switch
            {
                IDictionary<string, object> dict => dict.TryGetValue(key, out var v) ? v : null,
                System.Collections.IDictionary nd => nd.Contains(key) ? nd[key] : null,
                _ => null
            };
            if (current == null) return null;
        }

        return current;
    }

    private static List<object> ConvertToList(object items)
    {
        return items switch
        {
            IEnumerable<object> enumerable => enumerable.ToList(),
            System.Collections.IList list => list.Cast<object>().ToList(),
            System.Collections.IEnumerable enumerable => enumerable.Cast<object>().ToList(),
            _ => [items]
        };
    }

    private static object ApplyReducer(List<object> results, string reducer)
    {
        return reducer.ToLowerInvariant() switch
        {
            "collect" => results,
            "flatten" => results.SelectMany(r => r switch
            {
                IEnumerable<object> list => list,
                System.Collections.IList list => list.Cast<object>(),
                _ => new[] { r }
            }).ToList(),
            "first" => results.FirstOrDefault()!,
            "last" => results.LastOrDefault()!,
            "concat" => string.Join("\n", results.Select(r => r.ToString())),
            _ => results
        };
    }

    // Protobuf conversion: Use ProtoValueConverter utility class

}