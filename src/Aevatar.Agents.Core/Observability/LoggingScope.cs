using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Core.Observability;

// ============================================================
//  Logging Scope - Structured Logging Context
//  Provides rich log attributes for Aspire Dashboard
// ============================================================

/// <summary>
/// Structured logging scope helper class.
/// Provides cross-request context tracking.
/// </summary>
public static class LoggingScope
{
    // ============================================================
    //  Agent Operation Scopes
    // ============================================================

    /// <summary>
    /// Create agent operation scope.
    /// </summary>
    public static IDisposable CreateAgentScope(
        ILogger logger,
        string agentId,
        string operation,
        Dictionary<string, object>? additionalData = null)
    {
        var scopeData = new Dictionary<string, object>
        {
            ["AgentId"] = agentId.ToString(),
            ["Operation"] = operation,
            ["Timestamp"] = DateTime.UtcNow.ToString("O")
        };

        if (additionalData != null)
        {
            foreach (var kvp in additionalData)
            {
                scopeData[kvp.Key] = kvp.Value;
            }
        }

        return logger.BeginScope(scopeData) ?? NoOpDisposable.Instance;
    }

    /// <summary>
    /// Create agent activation scope.
    /// </summary>
    public static IDisposable CreateActivationScope(
        ILogger logger,
        Guid agentId,
        string agentType)
    {
        return logger.BeginScope(new Dictionary<string, object>
        {
            ["AgentId"] = agentId.ToString(),
            ["AgentType"] = agentType,
            ["Operation"] = "Activation",
            ["Phase"] = "Lifecycle",
            ["StartTime"] = DateTime.UtcNow.ToString("O")
        }) ?? NoOpDisposable.Instance;
    }

    /// <summary>
    /// Create agent deactivation scope.
    /// </summary>
    public static IDisposable CreateDeactivationScope(
        ILogger logger,
        Guid agentId,
        string agentType)
    {
        return logger.BeginScope(new Dictionary<string, object>
        {
            ["AgentId"] = agentId.ToString(),
            ["AgentType"] = agentType,
            ["Operation"] = "Deactivation",
            ["Phase"] = "Lifecycle",
            ["StartTime"] = DateTime.UtcNow.ToString("O")
        }) ?? NoOpDisposable.Instance;
    }

    // ============================================================
    //  Event Handling Scopes
    // ============================================================

    /// <summary>
    /// Create event handling scope.
    /// </summary>
    public static IDisposable CreateEventHandlingScope(
        ILogger logger,
        string agentId,
        string eventId,
        string eventType,
        string? correlationId = null)
    {
        var scopeData = new Dictionary<string, object>
        {
            ["AgentId"] = agentId.ToString(),
            ["EventId"] = eventId,
            ["EventType"] = eventType,
            ["Operation"] = "EventHandling",
            ["Phase"] = "Processing"
        };

        if (correlationId != null)
        {
            scopeData["CorrelationId"] = correlationId;
        }

        return logger.BeginScope(scopeData) ?? NoOpDisposable.Instance;
    }

    /// <summary>
    /// Create handler execution scope.
    /// </summary>
    public static IDisposable CreateHandlerScope(
        ILogger logger,
        Guid agentId,
        string handlerName,
        string eventType)
    {
        return logger.BeginScope(new Dictionary<string, object>
        {
            ["AgentId"] = agentId.ToString(),
            ["HandlerName"] = handlerName,
            ["EventType"] = eventType,
            ["Operation"] = "HandlerExecution",
            ["Phase"] = "Handler"
        }) ?? NoOpDisposable.Instance;
    }

    /// <summary>
    /// Create event publish scope.
    /// </summary>
    public static IDisposable CreateEventPublishScope(
        ILogger logger,
        Guid agentId,
        string eventType,
        string direction)
    {
        return logger.BeginScope(new Dictionary<string, object>
        {
            ["AgentId"] = agentId.ToString(),
            ["EventType"] = eventType,
            ["Direction"] = direction,
            ["Operation"] = "EventPublish",
            ["Phase"] = "Publishing"
        }) ?? NoOpDisposable.Instance;
    }

    /// <summary>
    /// Create event routing scope.
    /// </summary>
    public static IDisposable CreateEventRoutingScope(
        ILogger logger,
        Guid agentId,
        string eventId,
        string direction,
        int targetCount)
    {
        // Backward-compatible overload (legacy guid-only id).
        return CreateEventRoutingScope(logger, agentId.ToString(), eventId, direction, targetCount);
    }
    
    /// <summary>
    /// Create event routing scope (unified ActorId: "Type:RawId").
    /// </summary>
    public static IDisposable CreateEventRoutingScope(
        ILogger logger,
        string agentId,
        string eventId,
        string direction,
        int targetCount)
    {
        return logger.BeginScope(new Dictionary<string, object>
        {
            ["AgentId"] = agentId,
            ["EventId"] = eventId,
            ["Direction"] = direction,
            ["TargetCount"] = targetCount,
            ["Operation"] = "EventRouting",
            ["Phase"] = "Routing"
        }) ?? NoOpDisposable.Instance;
    }

    // ============================================================
    //  LLM Call Scopes
    // ============================================================

    /// <summary>
    /// Create LLM call scope.
    /// </summary>
    public static IDisposable CreateLLMCallScope(
        ILogger logger,
        string agentId,
        string provider,
        string model,
        bool isStreaming = false)
    {
        return logger.BeginScope(new Dictionary<string, object>
        {
            ["AgentId"] = agentId.ToString(),
            ["LLMProvider"] = provider,
            ["LLMModel"] = model,
            ["IsStreaming"] = isStreaming,
            ["Operation"] = "LLMCall",
            ["Phase"] = "AI"
        }) ?? NoOpDisposable.Instance;
    }

    /// <summary>
    /// Create embedding call scope.
    /// </summary>
    public static IDisposable CreateEmbeddingScope(
        ILogger logger,
        Guid agentId,
        string provider,
        string model,
        int inputCount)
    {
        return logger.BeginScope(new Dictionary<string, object>
        {
            ["AgentId"] = agentId.ToString(),
            ["LLMProvider"] = provider,
            ["LLMModel"] = model,
            ["InputCount"] = inputCount,
            ["Operation"] = "Embedding",
            ["Phase"] = "AI"
        }) ?? NoOpDisposable.Instance;
    }

    /// <summary>
    /// Create tool call scope.
    /// </summary>
    public static IDisposable CreateToolCallScope(
        ILogger logger,
        Guid agentId,
        string toolName,
        string toolCategory)
    {
        return logger.BeginScope(new Dictionary<string, object>
        {
            ["AgentId"] = agentId.ToString(),
            ["ToolName"] = toolName,
            ["ToolCategory"] = toolCategory,
            ["Operation"] = "ToolCall",
            ["Phase"] = "Tool"
        }) ?? NoOpDisposable.Instance;
    }

    // ============================================================
    //  Workflow Execution Scopes
    // ============================================================

    /// <summary>
    /// Create workflow execution scope.
    /// </summary>
    public static IDisposable CreateWorkflowScope(
        ILogger logger,
        string executionId,
        string workflowName,
        Guid coordinatorId)
    {
        return logger.BeginScope(new Dictionary<string, object>
        {
            ["ExecutionId"] = executionId,
            ["WorkflowName"] = workflowName,
            ["CoordinatorId"] = coordinatorId.ToString(),
            ["Operation"] = "WorkflowExecution",
            ["Phase"] = "Workflow"
        }) ?? NoOpDisposable.Instance;
    }

    /// <summary>
    /// Create step execution scope.
    /// </summary>
    public static IDisposable CreateStepScope(
        ILogger logger,
        string executionId,
        string stepId,
        string stepType,
        int depth = 0)
    {
        return logger.BeginScope(new Dictionary<string, object>
        {
            ["ExecutionId"] = executionId,
            ["StepId"] = stepId,
            ["StepType"] = stepType,
            ["Depth"] = depth,
            ["Operation"] = "StepExecution",
            ["Phase"] = "Step"
        }) ?? NoOpDisposable.Instance;
    }

    /// <summary>
    /// Create fan-out execution scope.
    /// </summary>
    public static IDisposable CreateFanOutScope(
        ILogger logger,
        string executionId,
        string stepId,
        int taskCount,
        int workerCount)
    {
        return logger.BeginScope(new Dictionary<string, object>
        {
            ["ExecutionId"] = executionId,
            ["StepId"] = stepId,
            ["TaskCount"] = taskCount,
            ["WorkerCount"] = workerCount,
            ["Operation"] = "FanOut",
            ["Phase"] = "Parallel"
        }) ?? NoOpDisposable.Instance;
    }

    /// <summary>
    /// Create vote scope.
    /// </summary>
    public static IDisposable CreateVoteScope(
        ILogger logger,
        string executionId,
        string stepId,
        int requiredVotes,
        int maxRounds)
    {
        return logger.BeginScope(new Dictionary<string, object>
        {
            ["ExecutionId"] = executionId,
            ["StepId"] = stepId,
            ["RequiredVotes"] = requiredVotes,
            ["MaxRounds"] = maxRounds,
            ["Operation"] = "Vote",
            ["Phase"] = "Consensus"
        }) ?? NoOpDisposable.Instance;
    }

    /// <summary>
    /// Create worker task scope.
    /// </summary>
    public static IDisposable CreateWorkerScope(
        ILogger logger,
        Guid workerId,
        string requestId,
        string stepId,
        string stepType)
    {
        return logger.BeginScope(new Dictionary<string, object>
        {
            ["WorkerId"] = workerId.ToString(),
            ["RequestId"] = requestId,
            ["StepId"] = stepId,
            ["StepType"] = stepType,
            ["Operation"] = "WorkerTask",
            ["Phase"] = "Worker"
        }) ?? NoOpDisposable.Instance;
    }

    // ============================================================
    //  Hierarchy Relationship Scopes
    // ============================================================

    /// <summary>
    /// Create hierarchy operation scope.
    /// </summary>
    public static IDisposable CreateHierarchyScope(
        ILogger logger,
        string agentId,
        string operation,
        string? targetId = null)
    {
        var scopeData = new Dictionary<string, object>
        {
            ["AgentId"] = agentId,
            ["HierarchyOperation"] = operation,
            ["Operation"] = "Hierarchy",
            ["Phase"] = "Structure"
        };

        if (targetId != null)
        {
            scopeData["TargetId"] = targetId;
        }

        return logger.BeginScope(scopeData) ?? NoOpDisposable.Instance;
    }

    // ============================================================
    //  State Operation Scopes
    // ============================================================

    /// <summary>
    /// Create state operation scope.
    /// </summary>
    public static IDisposable CreateStateScope(
        ILogger logger,
        Guid agentId,
        string agentType,
        string operation)
    {
        return logger.BeginScope(new Dictionary<string, object>
        {
            ["AgentId"] = agentId.ToString(),
            ["AgentType"] = agentType,
            ["StateOperation"] = operation,
            ["Operation"] = "State",
            ["Phase"] = "Persistence"
        }) ?? NoOpDisposable.Instance;
    }

    // ============================================================
    //  Exception Scopes
    // ============================================================

    /// <summary>
    /// Create exception handling scope.
    /// </summary>
    public static IDisposable CreateExceptionScope(
        ILogger logger,
        Guid agentId,
        string operation,
        string exceptionType)
    {
        return logger.BeginScope(new Dictionary<string, object>
        {
            ["AgentId"] = agentId.ToString(),
            ["Operation"] = operation,
            ["ExceptionType"] = exceptionType,
            ["Phase"] = "Error"
        }) ?? NoOpDisposable.Instance;
    }

    // ============================================================
    //  Utilities
    // ============================================================

    /// <summary>
    /// No-op disposable (used when BeginScope returns null).
    /// </summary>
    private sealed class NoOpDisposable : IDisposable
    {
        public static readonly NoOpDisposable Instance = new();
        private NoOpDisposable() { }
        public void Dispose() { }
    }
}

// ============================================================
//  High-Performance Logging Extensions
//  Using LoggerMessage.Define for high-performance logging
// ============================================================

/// <summary>
/// High-performance log message definitions.
/// Avoids boxing overhead from string interpolation.
/// </summary>
public static partial class AgentLogMessages
{
    // ============================================================
    //  Agent Lifecycle
    // ============================================================

    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Agent {AgentType} [{AgentId}] activating")]
    public static partial void AgentActivating(
        ILogger logger,
        string agentType,
        Guid agentId);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Agent {AgentType} [{AgentId}] activated in {DurationMs}ms")]
    public static partial void AgentActivated(
        ILogger logger,
        string agentType,
        Guid agentId,
        double durationMs);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Agent {AgentType} [{AgentId}] deactivating")]
    public static partial void AgentDeactivating(
        ILogger logger,
        string agentType,
        Guid agentId);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Information,
        Message = "Agent {AgentType} [{AgentId}] deactivated")]
    public static partial void AgentDeactivated(
        ILogger logger,
        string agentType,
        Guid agentId);

    // ============================================================
    //  Event Handling
    // ============================================================

    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Debug,
        Message = "Agent [{AgentId}] received event {EventType} [{EventId}]")]
    public static partial void EventReceived(
        ILogger logger,
        Guid agentId,
        string eventType,
        string eventId);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Debug,
        Message = "Agent [{AgentId}] handled event {EventType} in {DurationMs}ms with {HandlerCount} handlers")]
    public static partial void EventHandled(
        ILogger logger,
        Guid agentId,
        string eventType,
        double durationMs,
        int handlerCount);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Warning,
        Message = "Agent [{AgentId}] dropped event {EventType} - no handlers")]
    public static partial void EventDropped(
        ILogger logger,
        Guid agentId,
        string eventType);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Debug,
        Message = "Agent [{AgentId}] invoking handler {HandlerName} for {EventType}")]
    public static partial void HandlerInvoking(
        ILogger logger,
        Guid agentId,
        string handlerName,
        string eventType);

    [LoggerMessage(
        EventId = 2004,
        Level = LogLevel.Debug,
        Message = "Agent [{AgentId}] handler {HandlerName} completed in {DurationMs}ms")]
    public static partial void HandlerCompleted(
        ILogger logger,
        Guid agentId,
        string handlerName,
        double durationMs);

    [LoggerMessage(
        EventId = 2005,
        Level = LogLevel.Error,
        Message = "Agent [{AgentId}] handler {HandlerName} failed: {Error}")]
    public static partial void HandlerFailed(
        ILogger logger,
        Guid agentId,
        string handlerName,
        string error,
        Exception? exception = null);

    // ============================================================
    //  Event Publishing/Routing
    // ============================================================

    [LoggerMessage(
        EventId = 2100,
        Level = LogLevel.Debug,
        Message = "Agent [{AgentId}] publishing {EventType} direction={Direction}")]
    public static partial void EventPublishing(
        ILogger logger,
        Guid agentId,
        string eventType,
        string direction);

    [LoggerMessage(
        EventId = 2101,
        Level = LogLevel.Debug,
        Message = "Agent [{AgentId}] published {EventType} in {DurationMs}ms")]
    public static partial void EventPublished(
        ILogger logger,
        Guid agentId,
        string eventType,
        double durationMs);

    [LoggerMessage(
        EventId = 2102,
        Level = LogLevel.Debug,
        Message = "Agent [{AgentId}] routing event {EventId} to {TargetCount} targets")]
    public static partial void EventRouting(
        ILogger logger,
        string agentId,
        string eventId,
        int targetCount);

    // ============================================================
    //  LLM Calls
    // ============================================================

    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Debug,
        Message = "Agent [{AgentId}] calling LLM {Provider}/{Model}")]
    public static partial void LLMCallStarting(
        ILogger logger,
        string agentId,
        string provider,
        string model);

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Information,
        Message = "Agent [{AgentId}] LLM call completed: {PromptTokens}+{CompletionTokens} tokens in {DurationMs}ms")]
    public static partial void LLMCallCompleted(
        ILogger logger,
        string agentId,
        int promptTokens,
        int completionTokens,
        double durationMs);

    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Error,
        Message = "Agent [{AgentId}] LLM call failed: {ErrorType} - {ErrorMessage}")]
    public static partial void LLMCallFailed(
        ILogger logger,
        string agentId,
        string errorType,
        string errorMessage,
        Exception? exception = null);

    [LoggerMessage(
        EventId = 3003,
        Level = LogLevel.Debug,
        Message = "Agent [{AgentId}] streaming chunk {ChunkIndex}")]
    public static partial void LLMStreamingChunk(
        ILogger logger,
        Guid agentId,
        int chunkIndex);

    // ============================================================
    //  Workflow Execution
    // ============================================================

    [LoggerMessage(
        EventId = 4000,
        Level = LogLevel.Information,
        Message = "Workflow [{ExecutionId}] starting: {WorkflowName}")]
    public static partial void WorkflowStarting(
        ILogger logger,
        string executionId,
        string workflowName);

    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Information,
        Message = "Workflow [{ExecutionId}] completed in {DurationMs}ms - {TotalTokens} tokens, {LLMCalls} LLM calls")]
    public static partial void WorkflowCompleted(
        ILogger logger,
        string executionId,
        double durationMs,
        int totalTokens,
        int llmCalls);

    [LoggerMessage(
        EventId = 4002,
        Level = LogLevel.Error,
        Message = "Workflow [{ExecutionId}] failed at step {StepId}: {Error}")]
    public static partial void WorkflowFailed(
        ILogger logger,
        string executionId,
        string stepId,
        string error);

    [LoggerMessage(
        EventId = 4010,
        Level = LogLevel.Debug,
        Message = "Workflow [{ExecutionId}] step {StepId} ({StepType}) starting")]
    public static partial void StepStarting(
        ILogger logger,
        string executionId,
        string stepId,
        string stepType);

    [LoggerMessage(
        EventId = 4011,
        Level = LogLevel.Debug,
        Message = "Workflow [{ExecutionId}] step {StepId} completed in {DurationMs}ms")]
    public static partial void StepCompleted(
        ILogger logger,
        string executionId,
        string stepId,
        double durationMs);

    [LoggerMessage(
        EventId = 4020,
        Level = LogLevel.Information,
        Message = "Workflow [{ExecutionId}] fan-out {StepId}: {TaskCount} tasks to {WorkerCount} workers")]
    public static partial void FanOutStarting(
        ILogger logger,
        string executionId,
        string stepId,
        int taskCount,
        int workerCount);

    [LoggerMessage(
        EventId = 4021,
        Level = LogLevel.Information,
        Message = "Workflow [{ExecutionId}] fan-out {StepId} completed: {SuccessCount}/{TaskCount} successful")]
    public static partial void FanOutCompleted(
        ILogger logger,
        string executionId,
        string stepId,
        int successCount,
        int taskCount);

    [LoggerMessage(
        EventId = 4030,
        Level = LogLevel.Debug,
        Message = "Workflow [{ExecutionId}] vote {StepId} round {Round}: {Votes}/{Required} votes")]
    public static partial void VoteRound(
        ILogger logger,
        string executionId,
        string stepId,
        int round,
        int votes,
        int required);

    [LoggerMessage(
        EventId = 4031,
        Level = LogLevel.Information,
        Message = "Workflow [{ExecutionId}] vote {StepId} consensus reached in {Rounds} rounds")]
    public static partial void VoteConsensus(
        ILogger logger,
        string executionId,
        string stepId,
        int rounds);

    [LoggerMessage(
        EventId = 4032,
        Level = LogLevel.Warning,
        Message = "Workflow [{ExecutionId}] vote {StepId} 🚩 red flag: {Reason}")]
    public static partial void VoteRedFlag(
        ILogger logger,
        string executionId,
        string stepId,
        string reason);

    // ============================================================
    //  Hierarchy Relationships
    // ============================================================

    [LoggerMessage(
        EventId = 5000,
        Level = LogLevel.Debug,
        Message = "Agent [{AgentId}] setting parent to [{ParentId}]")]
    public static partial void SettingParent(
        ILogger logger,
        string agentId,
        string parentId);

    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Debug,
        Message = "Agent [{AgentId}] adding child [{ChildId}]")]
    public static partial void AddingChild(
        ILogger logger,
        string agentId,
        string childId);

    [LoggerMessage(
        EventId = 5002,
        Level = LogLevel.Debug,
        Message = "Agent [{AgentId}] hierarchy loaded: parent={ParentId}, children={ChildCount}")]
    public static partial void HierarchyLoaded(
        ILogger logger,
        string agentId,
        string? parentId,
        int childCount);
}
