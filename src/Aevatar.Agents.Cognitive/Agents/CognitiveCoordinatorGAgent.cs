using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
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
//  DSL 工作流协调器 - 真正的分布式并行
// ============================================================

/// <summary>
/// Cognitive Coordinator Agent - 工作流协调器
/// 
/// 并行模型：
/// - 简单步骤（单次 LLM）：Coordinator 直接执行
/// - 并行步骤（fan_out）：通过 Protobuf 事件分发给 Worker Actors
/// 
/// 这遵循 MAKER 的设计模式：
/// - MakerCoordinatorGAgent → CognitiveCoordinatorGAgent
/// - MakerWorkerGAgent → CognitiveWorkerGAgent
/// </summary>
public partial class CognitiveCoordinatorGAgent : AIGAgentBase<CognitiveCoordinatorState>
{
    // ============================================================
    //  组件
    // ============================================================

    private readonly TemplateEngine _templateEngine = new();
    private readonly OutputParserFactory _parserFactory = new();
    private readonly InMemoryWorkflowRegistry _workflowRegistry = new();

    // ============================================================
    //  运行时状态
    // ============================================================

    private readonly Dictionary<string, object> _workflowVariables = new();

    // Fan-out 结果收集
    private readonly ConcurrentDictionary<string, StepCompletedEventProto> _collectedResults = new();
    private readonly ConcurrentDictionary<string, string> _fanOutChildTypes = new();
    private readonly ConcurrentDictionary<string, string> _fanOutUserPrompts = new();
    private readonly ConcurrentDictionary<string, string> _fanOutSystemPrompts = new();
    private readonly ConcurrentDictionary<string, string> _fanOutOutputTypes = new();
    private int _expectedResults;
    private TaskCompletionSource<bool>? _fanOutCompletionSource;
    private StepDefinition? _currentFanOutStep;

    // Worker 管理（由外部注入）
    private IGAgentActorManager? _actorManager;
    private readonly List<Guid> _workerIds = [];

    // 语义聚类投票 (可选)
    private IEmbeddingGenerator<string, Embedding<float>>? _embeddingGenerator;
    private float _semanticSimilarityThreshold = 0.85f;

    // Red-Flagging (可选, 可插拔)
    private IRedFlagStrategy? _redFlagStrategy;
    private IRedFlagHandler _redFlagHandler = new DefaultRedFlagHandler();

    // Token-free primitives
    private TransformExecutor? _transformExecutor;
    private RetrieveFactsExecutor? _retrieveFactsExecutor;
    private HpaExecutor? _hpaExecutor;

    // 步骤事件追踪（用于前端可视化）
    private readonly List<WorkflowStepEvent> _stepEvents = [];
    private readonly ConcurrentDictionary<string, DateTime> _stepStartTimes = new();
    private Action<WorkflowStepEvent>? _onStepEvent;
    private readonly object _stepEventsLock = new(); // vote 并行生成时可能并发写入
    private readonly object _statsLock = new(); // 多并行任务下累加统计，避免丢失/错乱

    // ============================================================
    //  构造函数
    // ============================================================

    // ============================================================
    //  History compaction policy (no extra LLM calls)
    //
    //  WHY:
    //  - AIGAgentBase's default compaction can call LLM to summarize history.
    //  - Cognitive workflows already have strict budgets; hidden LLM calls are unacceptable.
    //  - For UI hydration, we rely on State.History (short-term) + AIMemory (long-term).
    // ============================================================
    protected override Task<string?> UpdateHistorySummaryAsync(
        string? existingSummary,
        IReadOnlyList<AevatarChatMessage> newlyArchivedMessages,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<string?>(null);
    }

    public CognitiveCoordinatorGAgent()
    {
    }

    // ============================================================
    //  生命周期
    // ============================================================

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        // ============================================================
        //  递归深度上限（默认值）
        //
        //  NOTE:
        //  - 最终上限应由工作流输入变量 `max_depth` 决定（见 HandleStartWorkflowRequest）。
        //  - 这里仅提供一个“启动默认值”，避免未配置时无限递归。
        // ============================================================
        CustomState.MaxDepth = 50;
        CustomState.Status = ExecutionStatus.EsPending;

        // 默认开启 Red-Flag 策略，限制最大内容长度 102400
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
    //  公共 API
    // ============================================================

    /// <summary>
    /// 设置 Actor Manager（用于创建 Workers）
    /// </summary>
    public void SetActorManager(IGAgentActorManager actorManager)
    {
        _actorManager = actorManager;
    }

    /// <summary>
    /// 设置 Embedding Generator（用于语义聚类投票）
    /// 如果不设置，投票将使用精确哈希匹配
    /// </summary>
    public void SetEmbeddingGenerator(
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator,
        float semanticSimilarityThreshold = 0.85f)
    {
        _embeddingGenerator = embeddingGenerator;
        _semanticSimilarityThreshold = Math.Clamp(semanticSimilarityThreshold, 0.1f, 1.0f);
    }

    /// <summary>
    /// 设置 Red-Flag 策略（用于过滤无效 LLM 响应）
    /// 可插拔设计：
    /// - null: 禁用 Red-Flag 检测（默认）
    /// - DefaultEnglishRedFlagStrategy: 英文内容验证
    /// - ChineseRedFlagStrategy: 中文内容验证
    /// - CodeAwareRedFlagStrategy: 代码内容验证
    /// - 自定义实现 IRedFlagStrategy
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
    /// 创建 Worker 池
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
            // 创建 Worker Actor
            var workerId = ids[i];
            var workerActor = await _actorManager.CreateAndRegisterAsync<CognitiveWorkerGAgent>(workerId);

            if (workerActor.GetAgent() is CognitiveWorkerGAgent worker)
            {
                // Reuse AIGAgentBase history switch (default off)
                worker.EnableChatHistoryInState = EnableChatHistoryInState;
                worker.EnableChatHistoryCompaction = EnableChatHistoryCompaction;
                worker.ChatHistoryMaxMessages = ChatHistoryMaxMessages;
                worker.ChatHistorySummaryMaxChars = ChatHistorySummaryMaxChars;
                worker.ArchiveCompactedHistoryToAIMemory = ArchiveCompactedHistoryToAIMemory;

                // 初始化 Worker 的 LLM Provider（否则 Worker.LLMProvider 会抛异常）
                if (!string.IsNullOrWhiteSpace(providerName))
                {
                    await worker.InitializeAsync(providerName!, cancellationToken: CancellationToken.None);
                }
            }

            // 设置父子关系（Worker 订阅 Coordinator 的流）
            await _actorManager.LinkParentChildAsync(Id, workerId);

            _workerIds.Add(workerId);
        }

        CustomState.ActiveWorkers = _workerIds.Count;

        Logger.LogInformation("Created worker pool with {Count} workers", poolSize);
    }

    /// <summary>
    /// 注册工作流
    /// </summary>
    public void RegisterWorkflow(WorkflowDefinition workflow)
    {
        _workflowRegistry.Register(workflow);
    }

    /// <summary>
    /// 获取执行结果
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
    //  事件处理
    // ============================================================

    private async Task<PrimitiveResult> ExecuteStepAsync(StepDefinition step)
    {
        // 预渲染 prompt 用于开始事件（确保 WORKERS 面板能显示完整提示词）
        string? preRenderedPrompt = null;
        string? preRenderedSystem = null;

        if (step.Type == "llm_call")
        {
            var rawPrompt = step.Parameters.GetValueOrDefault("prompt")?.ToString() ?? "";
            var rawSystem = step.Parameters.GetValueOrDefault("system")?.ToString();

            preRenderedPrompt = _templateEngine.Render(rawPrompt, _workflowVariables);
            if (rawSystem != null)
                preRenderedSystem = _templateEngine.Render(rawSystem, _workflowVariables);

            // 调试：如果 task 变量为空，记录警告
            if (rawPrompt.Contains("{{task}}") &&
                string.IsNullOrWhiteSpace(_workflowVariables.GetValueOrDefault("task")?.ToString()))
            {
                Logger.LogWarning("[{Step}] WARNING: task variable is empty! Available vars: {Vars}",
                    step.Id, string.Join(", ", _workflowVariables.Keys));
            }
        }

        // 发送开始事件（包含预渲染的 prompt）
        EmitStepEvent(step, StepStatus.Running,
            userPrompt: preRenderedPrompt,
            systemPrompt: preRenderedSystem);

        try
        {
            var result = step.Type switch
            {
                // 简单步骤：Coordinator 直接执行（传递预渲染的 prompt 避免重复渲染）
                "llm_call" => await ExecuteLlmCallDirectAsync(step, preRenderedPrompt, preRenderedSystem),
                "conditional" => await ExecuteConditionalAsync(step),

                // 并行步骤：分发给 Workers (真正的 Actor 并行)
                "fan_out" => await ExecuteFanOutAsync(step),
                "parallel" => await ExecuteParallelAsync(step),

                // 其他
                "vote" => await ExecuteVoteAsync(step),
                "workflow_call" => await ExecuteWorkflowCallAsync(step),
                "checkpoint" => await ExecuteCheckpointAsync(step),
                "assign" => await ExecuteAssignAsync(step),
                "transform" => await ExecuteTransformAsync(step),
                "retrieve_facts" => await ExecuteRetrieveFactsAsync(step),
                "hpa" => await ExecuteHpaAsync(step),

                _ => PrimitiveResult.Fail($"Unknown step type: {step.Type}")
            };

            // 发送完成/失败事件（包含对话记录）
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
        //  - 把 HPA 的“可计算几何层”从 prompt 中剥离出来
        //  - 让 workflow 可以用 deterministic 指标做分流/调度，而不是让 LLM 自己讲故事
        // ============================================================
        _hpaExecutor ??= new HpaExecutor(_templateEngine, Logger);
        var result = _hpaExecutor.Execute(step, _workflowVariables);
        return Task.FromResult(result);
    }

    // ============================================================
    //  其他步骤
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
            // - conditional 是控制流关键节点（stop_or_continue / ensure_state）
            // - 这里抛异常会直接 FailExecutionAsync，表现为“系统彻底停了”
            // - 策略：默认走 if_false（继续执行），并把错误记录下来（可从 UI/日志看到）
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

        // 执行分支
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
        //  对单次 workflow_call 允许覆盖 max_depth
        //
        //  WHY:
        //  - YAML 既支持 workflow 输入 max_depth，也可能在 workflow_call.params 里传 max_depth
        //  - Coordinator 内部执行 workflow_call 不走 StartWorkflowRequest，因此需要在这里显式处理
        // ============================================================
        var savedMaxDepth = CustomState.MaxDepth;

        // 检查递归深度
        CustomState.CurrentDepth++;
        if (CustomState.CurrentDepth > CustomState.MaxDepth)
        {
            CustomState.CurrentDepth--;
            return PrimitiveResult.Fail($"Max recursion depth {CustomState.MaxDepth} exceeded");
        }

        try
        {
            // 构建子上下文
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

            // 允许子调用用 params.max_depth 覆盖（仅对本次调用生效）
            if (TryGetPositiveInt(childVariables, "max_depth", out var callMaxDepth))
            {
                CustomState.MaxDepth = Math.Clamp(callMaxDepth, 1, 200);
            }

            // 临时替换变量
            var savedVariables = new Dictionary<string, object>(_workflowVariables);
            _workflowVariables.Clear();
            foreach (var (key, value) in childVariables)
            {
                _workflowVariables[key] = value;
            }

            // 执行子工作流
            await ExecuteWorkflowAsync(workflow);

            var output = _workflowVariables.GetValueOrDefault("_output");

            // DEBUG: 检查子工作流输出
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

            // 恢复变量
            _workflowVariables.Clear();
            foreach (var (key, value) in savedVariables)
            {
                _workflowVariables[key] = value;
            }

            return PrimitiveResult.Ok(output);
        }
        finally
        {
            // 恢复本次调用前的 maxDepth，避免污染外层流程
            CustomState.MaxDepth = savedMaxDepth;
            CustomState.CurrentDepth--;
        }
    }

    // ============================================================
    //  小工具：从变量表里解析正整数
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
        // Checkpoint is a token-free observability primitive.
        // It can optionally emit a JSON snapshot of selected workflow variables (or dotted paths),
        // so the UI / transcript can show incremental state without waiting for workflow completion.
        //
        // DSL usage:
        // - id: checkpoint_state
        //   type: checkpoint
        //   variables: ["state", "state.theorems"]
        Logger.LogDebug("Checkpoint at step: {StepId}", step.Id);

        var varsObj = step.Parameters.GetValueOrDefault("variables");
        var names = varsObj switch
        {
            IEnumerable<string> ss => ss.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList(),
            System.Collections.IEnumerable e => e.Cast<object?>()
                .Select(x => x?.ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .ToList(),
            _ => new List<string>()
        };

        var snapshot = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            var val = ResolvePathValue(_workflowVariables, name);
            snapshot[name] = val;
        }

        string body;
        try
        {
            body = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
            {
                WriteIndented = true,
                // Same spirit as TemplateEngine: keep non-ASCII readable in UI output.
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
        catch
        {
            body = snapshot.ToString() ?? "";
        }

        var userPrompt = names.Count > 0
            ? $"Checkpoint variables: {string.Join(", ", names)}"
            : "Checkpoint";

        return Task.FromResult(new PrimitiveResult
        {
            Success = true,
            Value = snapshot,
            UserPrompt = userPrompt,
            AssistantResponse = body
        });
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

    // Protobuf 转换：使用 ProtoValueConverter 工具类

}