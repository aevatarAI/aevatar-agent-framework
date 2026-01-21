using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Core.Embeddings;
using Aevatar.Agents.Cognitive.Agents;
using Aevatar.Agents.Cognitive.Engine;
using Aevatar.Agents.Cognitive.Messages;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.CognitiveMesh.Abstractions;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using System.Text;

namespace Aevatar.CognitiveMesh.Strategies;

// ============================================================
//  COGNITIVE DSL STRATEGY
//  DSL-driven cognitive strategy - Coordinator + Worker true parallelism
// ============================================================

/// <summary>
/// Cognitive DSL strategy adapter.
/// Uses YAML-defined workflows, executed via CognitiveCoordinatorGAgent.
/// 
/// Features:
/// - DSL-defined workflows (YAML)
/// - Coordinator + Worker true Actor parallelism
/// - Semantic clustering voting (optional)
/// - Supports recursive workflow calls
/// </summary>
public sealed class CognitiveStrategy : IReasoningStrategy
{
    private readonly IGAgentActorManager _actorManager;
    private readonly ILLMProviderFactory _llmFactory;
    private readonly IAIAgentEmbeddingFactory? _embeddingFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CognitiveStrategy> _logger;
    private readonly string _workflowsPath;
    
    // Lazy-initialized embedding generator for semantic clustering
    private IEmbeddingGenerator<string, Embedding<float>>? _embeddingGenerator;
    private bool _embeddingInitialized;
    
    // Workflow registry
    private readonly InMemoryWorkflowRegistry _workflowRegistry = new();
    private bool _workflowsLoaded;

    public CognitiveStrategy(
        IGAgentActorManager actorManager,
        ILLMProviderFactory llmFactory,
        IConfiguration configuration,
        ILogger<CognitiveStrategy> logger,
        IAIAgentEmbeddingFactory? embeddingFactory = null)
    {
        _actorManager = actorManager;
        _llmFactory = llmFactory;
        _embeddingFactory = embeddingFactory;
        _configuration = configuration;
        _logger = logger;
        
        // Workflow file path search (by priority)
        var searchPaths = new[]
        {
            // 1. workflows under execution directory
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "workflows"),
            // 2. workflows under current directory
            Path.Combine(Directory.GetCurrentDirectory(), "workflows"),
            // 3. Source code path (during development)
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "src", "Aevatar.Agents.Cognitive", "workflows"),
            // 4. Source code path relative to cognitive-mesh
            Path.Combine(Directory.GetCurrentDirectory(), "..", "src", "Aevatar.Agents.Cognitive", "workflows"),
            // 5. Absolute path fallback
            "/Users/zhaoyiqi/Code/aevatar-agent-framework/src/Aevatar.Agents.Cognitive/workflows"
        };
        
        _workflowsPath = searchPaths.FirstOrDefault(Directory.Exists) 
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "workflows");
        
        _logger.LogInformation("CognitiveStrategy initialized. Workflows path: {Path} (exists: {Exists})", 
            _workflowsPath, Directory.Exists(_workflowsPath));
    }

    public StrategyKind Kind => StrategyKind.Cognitive;
    public string DisplayName => "Cognitive DSL";
    public string Description => "DSL-defined workflows, true Actor parallelism";

    public ValidationResult ValidateOptions(ReasoningOptions options)
    {
        // Cognitive strategy requires workflow name to be specified
        if (string.IsNullOrEmpty(options.CognitiveWorkflow))
        {
            return ValidationResult.Failed("CognitiveWorkflow", "Cognitive strategy requires a workflow name");
        }
        
        return ValidationResult.Success();
    }

    private string ResolveProviderName(ReasoningOptions options)
    {
        var requested = (options.ProviderName ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(requested))
        {
            if (_llmFactory.HasProvider(requested))
                return requested;

            // Do NOT fail hard here; fall back to an available provider so workflows can still run.
            _logger.LogWarning(
                "Requested provider '{Provider}' not found. Falling back to available providers: {Providers}",
                requested,
                string.Join(", ", _llmFactory.GetAvailableProviderNames()));
        }

        // Prefer config default (instance name), if present and valid.
        var cfgDefault = (_configuration["LLMProviders:Default"] ?? _configuration["LLMProviders:default"] ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(cfgDefault) && _llmFactory.HasProvider(cfgDefault))
            return cfgDefault;

        // Finally, pick the first available provider from factory.
        var available = _llmFactory.GetAvailableProviderNames()
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (available.Count > 0)
            return available[0];

        // Last resort: historical constant (may still fail, but keeps behavior explicit).
        return string.IsNullOrWhiteSpace(cfgDefault) ? AevatarAgentsConstants.DefaultProviderName : cfgDefault;
    }

    public async Task<ReasoningResult> ExecuteAsync(
        string task,
        ReasoningOptions options,
        IProgress<ReasoningProgress>? progress = null,
        CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            // ─── Phase 0: Initialize Embedding Generator (semantic clustering) ───
            await EnsureEmbeddingInitializedAsync(ct);
            
            // ─── Phase 1: Load workflows ───
            progress?.Report(new ReasoningProgress
            {
                Phase = "LOADING",
                Message = "Loading workflows...",
                ProgressPercent = 0.05f
            });
            
            await EnsureWorkflowsLoadedAsync();
            
            var workflowName = options.CognitiveWorkflow ?? "maker-v2";
            var workflow = _workflowRegistry.Get(workflowName);
            
            if (workflow == null)
            {
                _logger.LogError("Workflow not found: {Name}", workflowName);
                return ReasoningResult.Failed(
                    $"Workflow '{workflowName}' not found. Available: {string.Join(", ", _workflowRegistry.List())}",
                    DateTime.UtcNow - startTime);
            }
            
            _logger.LogInformation("Executing workflow: {Name} v{Version}", workflow.Name, workflow.Version);
            
            // ─── Phase 2: Create Coordinator ───
            progress?.Report(new ReasoningProgress
            {
                Phase = "INITIALIZING",
                Message = "Creating Coordinator and Workers...",
                ProgressPercent = 0.1f
            });
            
            // ============================================================
            //  Stable AgentId (session-aware)
            //
            //  WHY:
            //  - Frontend wants "stateless refresh": reconnect must re-hydrate from Agent state/memory.
            //  - That requires deterministic ids so the service can locate the same Coordinator/Workers.
            //
            //  If no session_id/run_id is provided, we keep the old behavior (random Guid).
            // ============================================================
            var stableSessionKey = TryGetStableSessionKey(options);
            var rawCoordinatorId = !string.IsNullOrWhiteSpace(stableSessionKey)
                ? DeterministicGuid.FromString($"cognitive:{stableSessionKey}:coordinator").ToString("D")
                : Guid.NewGuid().ToString("D");

            // NOTE: Returned actor.Id is normalized full ActorId: "CognitiveCoordinatorGAgent:RawId"
            var coordinatorActor = await _actorManager.CreateAndRegisterAsync<CognitiveCoordinatorGAgent>(rawCoordinatorId, ct);
            var coordinator = coordinatorActor.GetAgent() as CognitiveCoordinatorGAgent;
            
            if (coordinator == null)
            {
                return ReasoningResult.Failed("Failed to create Coordinator agent", DateTime.UtcNow - startTime);
            }
            
            // Initialize AI Agent (set LLM Provider)
            //
            // IMPORTANT:
            // - Do NOT hardcode a single provider name (e.g., "deepseek").
            // - In many environments, configured provider instance names differ (e.g., "deepseek-deepseek-chat").
            // - If we pick a non-existent provider, the whole workflow never uses AI and returns failure.
            //
            // Strategy:
            // - Prefer request-provided ProviderName
            // - Then try config LLMProviders:Default
            // - Then fall back to the first available provider in factory
            var providerName = ResolveProviderName(options);
            await coordinator.InitializeAsync(providerName, cancellationToken: ct);
            
            // Configure Coordinator
            coordinator.SetActorManager(_actorManager);

            // ============================================================
            //  Chat history (State.History + compaction summary)
            //
            //  NOTE:
            //  - We enable this automatically when a stable session key exists,
            //    but callers can explicitly override via options.Context["enable_chat_history"].
            // ============================================================
            var enableChatHistory = ShouldEnableChatHistory(options, stableSessionKey);
            if (enableChatHistory)
            {
                coordinator.EnableChatHistoryInState = true;
                coordinator.EnableChatHistoryCompaction = true;
            }
            
            // Configure semantic clustering voting (if available)
            if (_embeddingGenerator != null)
            {
                coordinator.SetEmbeddingGenerator(_embeddingGenerator, options.CognitiveSemanticSimilarity ?? 0.85f);
            }
            
            // Track streaming state per step
            // - Used to identify "first token / last token", avoid per-token logging causing hang
            // - Note: StepEvent callbacks may trigger concurrently, HashSet is not thread-safe, will cause IndexOutOfRangeException (internal array corrupted by concurrent writes)
            var streamingStarted = new ConcurrentDictionary<string, byte>();

            // Set step event callback - forward to progress reporter
            coordinator.SetStepEventCallback(stepEvent =>
            {
                // Phase format: "{PHASE_PREFIX}:{stepId}"
                // Corresponds to MakerPhase enum: Assessing/Decomposing/Solving/Composing
                var phasePrefix = GetPhasePrefix(stepEvent.StepId, stepEvent.StepType);
                
                // Build StreamingTokenProgress for real-time display
                StreamingTokenProgress? streamingToken = null;
                var isRunning = stepEvent.Status == global::Aevatar.Agents.Cognitive.Messages.StepStatus.Running;
                var isCompleted = stepEvent.Status == global::Aevatar.Agents.Cognitive.Messages.StepStatus.Completed;
                
                // Worker count must use "real Worker Pool Size", cannot use ParallelTotal (it may be fan_out count or vote batchSize).
                // Otherwise same gen[N] will map to different workers in different events (UI will show some worker always empty / cross-talk).
                var n = options.CognitiveWorkerCount ?? 5;
                
                // Normalize worker ID: coordinator for main tasks, worker-{(index-1) % N} for gen[index]
                var normalizedWorkerId = NormalizeWorkerId(stepEvent.StepId, n);
                
                if (!string.IsNullOrEmpty(stepEvent.AssistantResponse) && stepEvent.StepType == "llm_call")
                {
                    var tokenCount = stepEvent.AssistantResponse.Length / 4;
                    var isFirst = streamingStarted.TryAdd(stepEvent.StepId, 0); // Returns true if newly added
                    
                    streamingToken = new StreamingTokenProgress
                    {
                        WorkerId = normalizedWorkerId,
                        ProposalId = stepEvent.StepId,
                        Token = "",
                        AccumulatedContent = stepEvent.AssistantResponse,
                        TokenIndex = tokenCount,
                        IsFirstToken = isFirst,
                        IsLastToken = isCompleted,
                        SystemPrompt = stepEvent.SystemPrompt,
                        UserPrompt = stepEvent.UserPrompt,
                        ProviderName = options.ProviderName ?? "deepseek"
                    };

                    // Only log once at "first token / last token": otherwise streaming will flood stdout
                    if (stepEvent.StepId.Contains("gen[") && (isFirst || isCompleted))
                    {
                        _logger.LogDebug(
                            "[STREAM-MAP] {StepId} ({Status}) -> {WorkerId} (n={N}, parallelTotal={ParallelTotal}, len={Len})",
                            stepEvent.StepId,
                            stepEvent.Status,
                            normalizedWorkerId,
                            n,
                            stepEvent.ParallelTotal,
                            stepEvent.AssistantResponse?.Length ?? 0);
                    }
                    
                    // Clear tracking when completed
                    if (isCompleted) streamingStarted.TryRemove(stepEvent.StepId, out _);
                }
                
                // Build VotingProgress when vote data is present
                Aevatar.CognitiveMesh.Abstractions.VotingProgress? votingProgress = null;
                if (stepEvent.StepType == "vote" && stepEvent.VoteK > 0)
                {
                    votingProgress = new Aevatar.CognitiveMesh.Abstractions.VotingProgress
                    {
                        Type = "Consensus",
                        Round = stepEvent.VoteRound,
                        TotalVotes = stepEvent.VoteCurrentVotes,
                        VotesNeeded = stepEvent.VoteK,
                        LeaderVotes = stepEvent.VoteCurrentVotes,
                        RunnerUpVotes = 0,
                        ClusterCount = 1,
                        UsedSemanticClustering = false
                    };
                }

                // Build ProposalProgress when LLM call completes
                ProposalProgress? proposalProgress = null;
                if (isCompleted && stepEvent.StepType == "llm_call" && !string.IsNullOrEmpty(stepEvent.AssistantResponse))
                {
                    proposalProgress = new ProposalProgress
                    {
                        ProposalId = stepEvent.StepId,
                        Content = stepEvent.AssistantResponse,
                        Success = true,
                        ProviderName = options.ProviderName ?? "deepseek"
                    };
                }
                
                progress?.Report(new ReasoningProgress
                {
                    Phase = $"{phasePrefix}:{stepEvent.StepId}",
                    Message = stepEvent.Message,
                    ProgressPercent = 0.2f + 0.7f * stepEvent.Progress,
                    TaskId = normalizedWorkerId,  // Use normalized ID for frontend aggregation
                    // ============================================================
                    //  MAKER recursion depth (critical field)
                    //
                    //  WHY:
                    //  - PaperReview's Atomic Points tree depends on Depth to build parent/child
                    //  - Missing Depth will cause:
                    //    1) Multi-level decompose all treated as root (only first level shown)
                    //    2) solve_atomic/compose consensus cannot be attributed to point (DONE but no conclusion)
                    //    3) Deep execute_subtasks[i] "cascading" to first level (ID conflict/override)
                    // ============================================================
                    Depth = stepEvent.Depth,
                    StepId = stepEvent.StepId,
                    StepType = stepEvent.StepType,
                    StepStatus = stepEvent.Status.ToString(),
                    VoteRound = stepEvent.VoteRound,
                    VoteMaxRounds = stepEvent.VoteMaxRounds,
                    VoteK = stepEvent.VoteK,
                    VoteCurrentVotes = stepEvent.VoteCurrentVotes,
                    WinnerProposalId = stepEvent.WinnerProposalId,
                    WinnerHash = stepEvent.WinnerHash,
                    WinnerVotes = stepEvent.WinnerVotes,
                    WinnerRunnerUpVotes = stepEvent.WinnerRunnerUpVotes,
                    WinnerClusterCount = stepEvent.WinnerClusterCount,
                    WinnerSemantic = stepEvent.WinnerSemantic,
                    WinnerIsConsensus = stepEvent.WinnerIsConsensus,
                    ParallelTotal = stepEvent.ParallelTotal,
                    ParallelCompleted = stepEvent.ParallelCompleted,
                    ParallelFailed = stepEvent.ParallelFailed,
                    // Global stats (Coordinator + Workers)
                    // NOTE:
                    // - DSL step events only provide cumulative tokens_used / llm_calls (no prompt/completion split)
                    // - We map tokens_used → TotalPromptTokens (Completion=0) to preserve exact totalTokens = prompt+completion
                    TotalLlmCalls = stepEvent.LlmCalls,
                    TotalPromptTokens = stepEvent.TokensUsed,
                    TotalCompletionTokens = 0,
                    // LLM conversation data
                    SystemPrompt = stepEvent.SystemPrompt,
                    UserPrompt = stepEvent.UserPrompt,
                    AssistantResponse = stepEvent.AssistantResponse,
                    // Streaming Token for real-time display
                    StreamingToken = streamingToken,
                    // Voting and Proposal progress for stage tracking
                    Voting = votingProgress,
                    Proposal = proposalProgress
                });
            });
            
            // Register workflows
            foreach (var wf in _workflowRegistry.List())
            {
                var w = _workflowRegistry.Get(wf);
                if (w != null) coordinator.RegisterWorkflow(w);
            }
            
            // Create Worker pool
            var workerPoolSize = options.CognitiveWorkerCount > 0 ? options.CognitiveWorkerCount.Value : 5;
            
            IReadOnlyList<Guid>? stableWorkerIds = null;
            if (!string.IsNullOrWhiteSpace(stableSessionKey))
            {
                var ids = new List<Guid>(capacity: workerPoolSize);
                for (var i = 0; i < workerPoolSize; i++)
                {
                    ids.Add(DeterministicGuid.FromString($"cognitive:{stableSessionKey}:worker:{i}"));
                }
                stableWorkerIds = ids;
            }
            
            await coordinator.CreateWorkerPoolAsync(workerPoolSize, stableWorkerIds);
            
            _logger.LogInformation("Created Coordinator {Id} with {Workers} workers", coordinatorActor.Id, workerPoolSize);
            
            // ─── Phase 3: Execute workflow ───
            progress?.Report(new ReasoningProgress
            {
                Phase = "EXECUTING",
                Message = $"Executing workflow: {workflowName}",
                ProgressPercent = 0.2f
            });
            
            // Set completion callback
            var completionSource = new TaskCompletionSource<WorkflowCompletedEventProto>();
            
            // Subscribe to completion event (by checking status)
            // Note: Since it's event-driven, we need polling or event subscription
            
            // Build initial variables
            // context contains original task content, child tasks can access it during recursion
            var initialVariables = new Dictionary<string, object>
            {
                ["task"] = task,
                ["context"] = task  // Child tasks access original content via context during recursion
            };

            // Add extra parameters
            // Default K=3, can be explicitly overridden externally
            if (options.CognitiveConsensusK is > 0)
            {
                initialVariables["k"] = options.CognitiveConsensusK.Value;
            }
            else
            {
                initialVariables["k"] = 3;
            }
            if (options.CognitiveMaxRounds > 0)
            {
                initialVariables["max_rounds"] = options.CognitiveMaxRounds.Value;
            }
            if (options.CognitiveMaxDepth > 0)
            {
                initialVariables["max_depth"] = options.CognitiveMaxDepth.Value;
            }

            // Extra runtime flags (from service Context)
            // - Keep it explicit: only propagate known flags to avoid leaking arbitrary user data into DSL variables.
            if (options.Context != null &&
                options.Context.TryGetValue("continue_on_failure", out var cof) &&
                bool.TryParse(cof, out var continueOnFailure))
            {
                initialVariables["continue_on_failure"] = continueOnFailure;
            }

            if (options.Context != null &&
                options.Context.TryGetValue("language", out var lang) &&
                !string.IsNullOrWhiteSpace(lang))
            {
                initialVariables["language"] = lang.Trim();
            }

            // HPA knobs (whitelist):
            // - propagate only expected keys to avoid leaking arbitrary user data into DSL variables
            if (options.Context != null)
            {
                foreach (var (key, value) in options.Context)
                {
                    if (string.IsNullOrWhiteSpace(key) || value == null) continue;

                    var k2 = key.Trim();
                    if (k2.StartsWith("hpa_", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(k2, "min_coherence", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(k2, "max_gap_norm", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(k2, "max_associator_mean", StringComparison.OrdinalIgnoreCase))
                    {
                        // Keep as string; DSL parsers / hpa executor will parse when needed.
                        initialVariables[k2] = value;
                    }
                }
            }
            
            // 直接调用 Coordinator 启动工作流（不通过事件流）
            _ = coordinator.StartWorkflowAsync(workflowName, initialVariables);
            
            // ─── 阶段 4：等待完成（轮询状态）───
            // 重要：必须尊重 options.MaxDuration，避免测试/生产无限等待。
            var timeout = options.MaxDuration > TimeSpan.Zero ? options.MaxDuration : TimeSpan.FromMinutes(30);
            var workflowResult = await WaitForCompletionAsync(coordinator, progress, timeout, ct);
            
            // ─── 阶段 5：清理 ───
            await _actorManager.DeactivateAndUnregisterAsync(coordinatorActor.Id, ct);
            
            var duration = DateTime.UtcNow - startTime;
            
            if (workflowResult.Success)
            {
                progress?.Report(new ReasoningProgress
                {
                    Phase = "COMPLETE",
                    Message = "Workflow completed successfully",
                    ProgressPercent = 1.0f,
                    TotalLlmCalls = workflowResult.TotalLlmCalls,
                    TotalPromptTokens = workflowResult.TotalTokens / 2, // 估算
                    TotalCompletionTokens = workflowResult.TotalTokens / 2
                });
                
                // Serialize output properly (not just ToString)
                var outputContent = SerializeOutput(workflowResult.Output);
                
                return ReasoningResult.Succeeded(
                    outputContent,
                    duration,
                    workflowResult.TotalLlmCalls,
                    workflowResult.TotalTokens / 2,
                    workflowResult.TotalTokens / 2);
            }

            // ─────────────────────────────────────────────────────────
            //  Timeout fallback (关键：不浪费已完成的结果)
            // ─────────────────────────────────────────────────────────
            if (string.Equals(workflowResult.Error, "Workflow execution timed out", StringComparison.OrdinalIgnoreCase))
            {
                // workflowResult.Output 已在 WaitForCompletionAsync 中被填充为“部分报告”
                var outputContent = SerializeOutput(workflowResult.Output);

                progress?.Report(new ReasoningProgress
                {
                    Phase = "COMPLETE",
                    Message = $"Timed out after {timeout.TotalMinutes:F0} minutes; returning partial report.",
                    ProgressPercent = 1.0f,
                    TotalLlmCalls = workflowResult.TotalLlmCalls,
                    TotalPromptTokens = workflowResult.TotalTokens,
                    TotalCompletionTokens = 0
                });

                // NOTE:
                // - DSL 只提供累计 tokens_used / llm_calls
                // - 这里把 tokens_used 视为 promptTokens（completion=0），确保 TotalTokens 一致
                return ReasoningResult.Succeeded(
                    outputContent,
                    duration,
                    workflowResult.TotalLlmCalls,
                    workflowResult.TotalTokens,
                    completionTokens: 0);
            }

            return ReasoningResult.Failed(workflowResult.Error ?? "Workflow failed", duration);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Cognitive workflow cancelled");
            return ReasoningResult.Failed("Cancelled by user", DateTime.UtcNow - startTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cognitive workflow failed");
            return ReasoningResult.Failed(ex.Message, DateTime.UtcNow - startTime);
        }
    }
    
    // ============================================================
    //  辅助方法
    // ============================================================
    
    /// <summary>
    /// Lazy-initialize embedding generator from LLM provider configuration.
    /// Required for semantic clustering in vote steps.
    /// </summary>
    private async Task EnsureEmbeddingInitializedAsync(CancellationToken ct = default)
    {
        if (_embeddingInitialized) return;
        
        if (_embeddingFactory == null)
        {
            _logger.LogDebug("No IAIAgentEmbeddingFactory available, semantic clustering disabled");
            _embeddingInitialized = true;
            return;
        }
        
        try
        {
            // Get default provider configuration from IConfiguration
            var providersSection = _configuration.GetSection("LLMProviders:Providers");
            var defaultProviderName = _configuration["LLMProviders:Default"] ?? "deepseek";
            var providerSection = providersSection.GetSection(defaultProviderName);
            
            if (!providerSection.Exists())
            {
                _logger.LogWarning("LLM provider configuration not found: {Name}, semantic clustering disabled", defaultProviderName);
                _embeddingInitialized = true;
                return;
            }
            
            var providerConfig = new LLMProviderConfig();
            providerSection.Bind(providerConfig);

            // Global embeddings fallback (LLMProviders:Embeddings) + partial merge (provider overrides global fields).
            LLMEmbeddingConfig? globalEmbeddings = null;
            var globalSection = _configuration.GetSection("LLMProviders:Embeddings");
            if (globalSection.Exists())
            {
                globalEmbeddings = new LLMEmbeddingConfig();
                globalSection.Bind(globalEmbeddings);
            }

            if (providerConfig.Embeddings == null)
            {
                providerConfig.Embeddings = globalEmbeddings;
            }
            else if (globalEmbeddings != null)
            {
                // Partial-merge missing fields from global.
                providerConfig.Embeddings.ProviderType ??= globalEmbeddings.ProviderType;
                providerConfig.Embeddings.Model ??= globalEmbeddings.Model;
                providerConfig.Embeddings.DeploymentName ??= globalEmbeddings.DeploymentName;
                providerConfig.Embeddings.Endpoint ??= globalEmbeddings.Endpoint;
                providerConfig.Embeddings.ApiKey ??= globalEmbeddings.ApiKey;
                providerConfig.Embeddings.Dimensions ??= globalEmbeddings.Dimensions;
                foreach (var kv in globalEmbeddings.ProviderSpecificSettings)
                {
                    if (!providerConfig.Embeddings.ProviderSpecificSettings.ContainsKey(kv.Key))
                        providerConfig.Embeddings.ProviderSpecificSettings[kv.Key] = kv.Value;
                }
            }

            if (providerConfig.Embeddings == null)
            {
                _logger.LogDebug("Embeddings not configured, semantic clustering disabled");
                _embeddingInitialized = true;
                return;
            }
            
            _embeddingGenerator = await _embeddingFactory.CreateAsync(providerConfig, ct);
            
            if (_embeddingGenerator != null)
            {
                _logger.LogInformation("✓ Semantic clustering enabled with embedding model: {Model}", 
                    providerConfig.Embeddings.Model ?? providerConfig.Model);
            }
            else
            {
                _logger.LogWarning("Failed to create embedding generator, semantic clustering disabled");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error initializing embedding generator, semantic clustering disabled");
        }
        
        _embeddingInitialized = true;
    }

    private Task EnsureWorkflowsLoadedAsync()
    {
        if (_workflowsLoaded) return Task.CompletedTask;
        
        lock (_workflowRegistry)
        {
            if (_workflowsLoaded) return Task.CompletedTask;
            
            if (Directory.Exists(_workflowsPath))
            {
                var parser = new WorkflowParser();
                var count = 0;
                
                foreach (var workflow in parser.ParseDirectory(_workflowsPath))
                {
                    _workflowRegistry.Register(workflow);
                    count++;
                    _logger.LogDebug("Loaded workflow: {Name}", workflow.Name);
                }
                
                _logger.LogInformation("Loaded {Count} workflows from {Path}", count, _workflowsPath);
            }
            else
            {
                _logger.LogWarning("Workflows directory not found: {Path}", _workflowsPath);
            }
            
            _workflowsLoaded = true;
        }
        
        return Task.CompletedTask;
    }
    
    private async Task<WorkflowResult> WaitForCompletionAsync(
        CognitiveCoordinatorGAgent coordinator,
        IProgress<ReasoningProgress>? progress,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var pollInterval = TimeSpan.FromMilliseconds(1000); // 降低轮询频率
        var elapsed = TimeSpan.Zero;
        var lastDescription = "";
        
        while (!ct.IsCancellationRequested && elapsed < timeout)
        {
            await Task.Delay(pollInterval, ct);
            elapsed += pollInterval;
            
            // 检查是否完成
            var result = coordinator.GetResult();
            if (result.Success || !string.IsNullOrEmpty(result.Error))
            {
                return result;
            }

            // 只在描述变化时报告进度（减少无用事件）
            var description = await coordinator.GetDescriptionAsync();
            if (description != lastDescription)
            {
                lastDescription = description;
                // 不再发送 RUNNING 轮询事件，避免干扰真正的步骤事件
            }
        }
        
        // ─────────────────────────────────────────────────────────
        //  超时：不直接判失败，而是输出“当前已完成的部分结果”
        // ─────────────────────────────────────────────────────────
        var snapshot = coordinator.GetResult();
        snapshot.Success = false;
        snapshot.Error = "Workflow execution timed out";
        snapshot.Output = BuildTimeoutFallbackOutput(coordinator.GetStepEvents(), elapsed, timeout);
        return snapshot;
    }

    // ─────────────────────────────────────────────────────────
    //  Timeout fallback: 组合已完成片段为部分报告（不浪费前面跑出的内容）
    // ─────────────────────────────────────────────────────────
    private static string BuildTimeoutFallbackOutput(IReadOnlyList<WorkflowStepEvent> events, TimeSpan elapsed, TimeSpan timeout)
    {
        static string? LastCompletedAssistant(IReadOnlyList<WorkflowStepEvent> evts, int depth, string stepId, string stepType)
        {
            for (var i = evts.Count - 1; i >= 0; i--)
            {
                var e = evts[i];
                if (e.Depth != depth) continue;
                if (e.Status != StepStatus.Completed) continue;
                if (!string.Equals(e.StepId, stepId, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(e.StepType, stepType, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrWhiteSpace(e.AssistantResponse)) return e.AssistantResponse;
            }
            return null;
        }

        var sb = new StringBuilder();
        sb.AppendLine("## ⚠️ Timeout: Partial Review Report\n");
        sb.AppendLine($"本次评审已运行 **{elapsed.TotalMinutes:F1} 分钟**，超过超时阈值 **{timeout.TotalMinutes:F0} 分钟**，系统停止等待并输出当前已完成的部分结果。");
        sb.AppendLine("注意：由于超时，评审流程未完整执行，以下内容可能不完整/未达成最终共识。\n");

        if (events.Count == 0)
        {
            sb.AppendLine("> (没有捕获到任何步骤事件，无法生成部分结果)");
            return sb.ToString().Trim();
        }

        var top = LastCompletedAssistant(events, 0, "compose", "vote") ?? LastCompletedAssistant(events, 0, "solve_atomic", "vote");
        if (!string.IsNullOrWhiteSpace(top))
        {
            sb.AppendLine("### 已生成的汇总输出（best-effort）");
            sb.AppendLine(top.Trim());
            return sb.ToString().Trim();
        }

        var solutions = new List<string>();
        foreach (var e in events)
        {
            if (e.Depth <= 0) continue;
            if (e.Status != StepStatus.Completed) continue;
            if (!string.Equals(e.StepType, "vote", StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(e.StepId, "solve_atomic", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrWhiteSpace(e.AssistantResponse)) continue;
            solutions.Add(e.AssistantResponse);
        }

        if (solutions.Count > 0)
        {
            sb.AppendLine("### 组合输出（fallback）");
            sb.AppendLine(string.Join("\n\n---\n\n", solutions.Select(s => s.Trim())));
            return sb.ToString().Trim();
        }

        var last = events[^1];
        sb.AppendLine("### 运行快照");
        sb.AppendLine($"- **Last step**: `{last.StepType}` `{last.StepId}` ({last.Status}) depth={last.Depth}");
        sb.AppendLine($"- **Last message**: {last.Message}");
        sb.AppendLine("> (尚未产生可用的评审内容；仅记录了流程事件)");
        return sb.ToString().Trim();
    }
    
    /// <summary>
    /// 获取可用工作流列表。
    /// </summary>
    public IReadOnlyList<string> GetAvailableWorkflows()
    {
        EnsureWorkflowsLoadedAsync().GetAwaiter().GetResult();
        return _workflowRegistry.List();
    }
    
    // ============================================================
    //  Phase 映射 - 与 MakerPhase 枚举对应
    // ============================================================
    
    /// <summary>
    /// stepId 关键词 → Phase 前缀映射
    /// </summary>
    private static readonly (string keyword, string prefix)[] PhaseMapping =
    [
        ("check_atomic", "ASSESS"),     // MakerPhase.Assessing
        ("decompose", "DECOMPOSE"),     // MakerPhase.Decomposing
        ("compose", "COMPOSE"),         // MakerPhase.Composing
        ("solve", "SOLVE"),             // MakerPhase.Solving
        ("execute", "EXECUTE"),         // MakerPhase.Executing
    ];

    // ============================================================
    //  Fan-out Step Prefixes (worker grouping)
    //
    //  WHY:
    //  - DSL fan_out 会把 step id 展开成 "{id}[i]"（0-based index）
    //  - UI 侧依赖 WorkerId 分组；若不识别这些展开形式，就会“永远只有 coordinator”
    //
    //  NOTE:
    //  - 这里用“数据驱动前缀集合”避免写一堆 if/else 分支
    //  - 新增 fan_out step 时，只需要把 id 加进集合（或升级为从 workflow 元数据自动生成）
    // ============================================================
    private static readonly HashSet<string> FanOutStepPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        // axiom_theorem_loop.yaml
        "prove_with_workers",

        // hypothesis_promotion_loop*.yaml
        // NOTE:
        // - fan_out 会展开成 "{id}[i]"，若不加入这里，UI 会把所有并行子任务都归到 coordinator，
        //   进而造成 streaming token 交错时“每个 token 都新增一条 history 记录”的错觉。
        "refute_scout",
        "prove_or_refute_with_workers",

        // maker-v2.yaml / maker.yaml
        "execute_subtasks",
        "solve_subtasks",

        // uot-combinational*.yaml
        "decompose_thoughts",
        "synthesize",
        "synthesize_candidates",
        "evaluate_candidates"
    };
    
    private static string GetPhasePrefix(string stepId, string stepType)
    {
        var stepIdLower = stepId.ToLowerInvariant();
        
        foreach (var (keyword, prefix) in PhaseMapping)
        {
            if (stepIdLower.Contains(keyword))
                return prefix;
        }
        
        return stepType.ToUpperInvariant();
    }

    /// <summary>
    /// Normalize step ID to logical worker ID (coordinator or worker-N).
    /// Uses N (worker count) to cycle workers: gen[index] -> worker-{(index-1) % workerCount}
    /// </summary>
    private static string NormalizeWorkerId(string stepId, int workerCount = 5)
    {
        if (string.IsNullOrEmpty(stepId)) return "coordinator";
        
        var lower = stepId.ToLowerInvariant();
        
        // Coordinator patterns: check_atomic, compose, vote steps
        if (lower.Contains("check_atomic") || 
            lower.Contains("coordinator") ||
            (lower.Contains("compose") && !lower.Contains("gen[")) ||
            lower.EndsWith(".vote"))
        {
            return "coordinator";
        }
        
        // Worker patterns: gen[index] -> worker-{(index-1) % workerCount}
        var match = System.Text.RegularExpressions.Regex.Match(stepId, @"gen\[(\d+)\]");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var genIndex))
        {
            var workerIndex = workerCount > 0 ? (genIndex - 1) % workerCount : 0;
            return $"worker-{workerIndex}";
        }

        // Fan-out patterns: "{stepIdPrefix}[i]" -> worker-{i % workerCount}
        // e.g. prove_with_workers[2], execute_subtasks[0], decompose_thoughts[4] ...
        var bracketStart = stepId.IndexOf('[');
        if (bracketStart > 0)
        {
            var bracketEnd = stepId.IndexOf(']', bracketStart + 1);
            if (bracketEnd > bracketStart + 1)
            {
                var prefix = stepId[..bracketStart];
                var indexText = stepId[(bracketStart + 1)..bracketEnd];
                if (FanOutStepPrefixes.Contains(prefix) && int.TryParse(indexText, out var i))
                {
                    var workerIndex = workerCount > 0 ? i % workerCount : 0;
                    return $"worker-{workerIndex}";
                }
            }
        }
        
        // Default to coordinator for unknown patterns
        return "coordinator";
    }

    private static string? TryGetStableSessionKey(ReasoningOptions options)
    {
        if (options.Context == null || options.Context.Count == 0)
            return null;

        // Prefer "session_id" (service-owned); fallback to "run_id" for generic callers.
        if (options.Context.TryGetValue("session_id", out var sessionId) &&
            !string.IsNullOrWhiteSpace(sessionId))
        {
            return sessionId.Trim();
        }

        if (options.Context.TryGetValue("run_id", out var runId) &&
            !string.IsNullOrWhiteSpace(runId))
        {
            return runId.Trim();
        }

        return null;
    }

    private static bool ShouldEnableChatHistory(ReasoningOptions options, string? stableSessionKey)
    {
        // Explicit override (preferred).
        if (options.Context != null &&
            options.Context.TryGetValue("enable_chat_history", out var raw) &&
            !string.IsNullOrWhiteSpace(raw) &&
            bool.TryParse(raw, out var enabled))
        {
            return enabled;
        }

        // Implicit default:
        // - if caller provides a stable session key, we assume they want reconnectable history.
        return !string.IsNullOrWhiteSpace(stableSessionKey);
    }
    
    /// <summary>
    /// Serialize workflow output to readable string.
    /// Handles Dictionary, List, and primitive types.
    /// </summary>
    private static string SerializeOutput(object? output)
    {
        if (output == null) return "";
        
        // If it's already a string, return it
        if (output is string str) return str;
        
        // If it's a Dictionary, try to extract meaningful content
        if (output is IDictionary<string, object> dict)
        {
            // Try common field names first
            var priorityFields = new[] { "solution", "content", "result", "answer", "output", "text", "review" };
            foreach (var field in priorityFields)
            {
                if (dict.TryGetValue(field, out var val) && val != null)
                {
                    var serialized = SerializeOutput(val);
                    if (!string.IsNullOrEmpty(serialized) && serialized.Length > 10)
                        return serialized;
                }
            }
            
            // If no priority field found, look for any string value
            foreach (var (key, val) in dict)
            {
                if (val is string s && s.Length > 50)
                    return s;
            }
            
            // Last resort: JSON serialize
            try
            {
                return System.Text.Json.JsonSerializer.Serialize(dict, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });
            }
            catch
            {
                return dict.ToString() ?? "";
            }
        }
        
        // If it's a List, serialize each item
        if (output is System.Collections.IList list)
        {
            var items = new List<string>();
            foreach (var item in list)
            {
                items.Add(SerializeOutput(item));
            }
            return string.Join("\n\n", items);
        }
        
        // For other objects, try JSON serialization
        try
        {
            return System.Text.Json.JsonSerializer.Serialize(output, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch
        {
            return output.ToString() ?? "";
        }
    }
}
