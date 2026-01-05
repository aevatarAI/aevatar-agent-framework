namespace Aevatar.Agents.Cognitive.Primitives;

// ============================================================
//  Primitive Execution Context
// ============================================================

/// <summary>
/// Primitive execution context, containing variables, cancellation token, progress reporting, etc.
/// </summary>
public class PrimitiveContext
{
    /// <summary>Workflow variable storage</summary>
    public Dictionary<string, object> Variables { get; } = new();
    
    /// <summary>Cancellation token</summary>
    public CancellationToken CancellationToken { get; init; }
    
    /// <summary>Progress reporter</summary>
    public IProgress<WorkflowProgress>? Progress { get; init; }
    
    /// <summary>Current recursion depth</summary>
    public int CurrentDepth { get; init; }
    
    /// <summary>Maximum recursion depth</summary>
    public int MaxDepth { get; init; } = 10;
    
    /// <summary>Run ID</summary>
    public string RunId { get; init; } = string.Empty;
    
    /// <summary>Current step ID</summary>
    public string CurrentStepId { get; set; } = string.Empty;
    
    /// <summary>Clone context (for subtasks)</summary>
    public PrimitiveContext Clone()
    {
        var clone = new PrimitiveContext
        {
            CancellationToken = CancellationToken,
            Progress = Progress,
            CurrentDepth = CurrentDepth,
            MaxDepth = MaxDepth,
            RunId = RunId,
            CurrentStepId = CurrentStepId
        };
        
        foreach (var (key, value) in Variables)
        {
            clone.Variables[key] = value;
        }
        
        return clone;
    }
}

// ============================================================
//  Primitive Execution Result
// ============================================================

/// <summary>
/// Primitive execution result
/// </summary>
public record PrimitiveResult
{
    /// <summary>Whether successful</summary>
    public bool Success { get; init; }
    
    /// <summary>Return value</summary>
    public object? Value { get; init; }
    
    /// <summary>Error message</summary>
    public string? Error { get; init; }
    
    /// <summary>Number of tokens used</summary>
    public int TokensUsed { get; init; }
    
    /// <summary>Prompt token count</summary>
    public int PromptTokens { get; init; }
    
    /// <summary>Completion token count</summary>
    public int CompletionTokens { get; init; }
    
    /// <summary>LLM call count</summary>
    public int LlmCalls { get; init; }
    
    /// <summary>Execution duration</summary>
    public TimeSpan Duration { get; init; }
    
    // ─────────────────────────────────────────────────────────
    //  LLM Conversation History (for frontend visualization)
    // ─────────────────────────────────────────────────────────
    
    /// <summary>System prompt</summary>
    public string? SystemPrompt { get; init; }
    
    /// <summary>User prompt</summary>
    public string? UserPrompt { get; init; }
    
    /// <summary>Assistant response</summary>
    public string? AssistantResponse { get; init; }
    
    /// <summary>Create success result</summary>
    public static PrimitiveResult Ok(object? value = null, int tokensUsed = 0, int llmCalls = 0) => new()
    {
        Success = true,
        Value = value,
        TokensUsed = tokensUsed,
        LlmCalls = llmCalls
    };
    
    /// <summary>Create failure result</summary>
    public static PrimitiveResult Fail(string error) => new()
    {
        Success = false,
        Error = error
    };
}

// ============================================================
//  Workflow Progress
// ============================================================

/// <summary>
/// Workflow progress report
/// </summary>
public record WorkflowProgress
{
    public required string Phase { get; init; }
    public string? StepId { get; init; }
    public float ProgressPercent { get; init; }
    public string? Message { get; init; }
    
    // Voting progress
    public VotingProgressInfo? Voting { get; init; }
    
    // Fan-out progress
    public FanOutProgressInfo? FanOut { get; init; }
}

public record VotingProgressInfo
{
    public int Round { get; init; }
    public int MaxRounds { get; init; }
    public int VotesForLeader { get; init; }
    public int K { get; init; }
    public string? CurrentLeader { get; init; }
}

public record FanOutProgressInfo
{
    public int Total { get; init; }
    public int Completed { get; init; }
    public int Failed { get; init; }
}

// ============================================================
//  Primitive Interface
// ============================================================

/// <summary>
/// Primitive interface - Unified abstraction for all DSL step types
/// </summary>
public interface IPrimitive
{
    /// <summary>Primitive type name</summary>
    string Type { get; }
    
    /// <summary>Execute primitive</summary>
    Task<PrimitiveResult> ExecuteAsync(
        PrimitiveContext context,
        Dictionary<string, object?> parameters);
}

// ============================================================
//  Parameter Helper Extensions
// ============================================================

public static class ParameterExtensions
{
    /// <summary>Get required parameter</summary>
    public static T GetRequired<T>(this Dictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value == null)
            throw new ArgumentException($"Required parameter '{key}' is missing");
        
        if (value is T typed)
            return typed;
        
        // Try conversion
        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            throw new ArgumentException($"Parameter '{key}' cannot be converted to {typeof(T).Name}");
        }
    }
    
    /// <summary>Get optional parameter</summary>
    public static T? GetOptional<T>(this Dictionary<string, object?> parameters, string key, T? defaultValue = default)
    {
        if (!parameters.TryGetValue(key, out var value) || value == null)
            return defaultValue;
        
        if (value is T typed)
            return typed;
        
        // Try conversion
        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return defaultValue;
        }
    }
}

public static class VariableExtensions
{
    /// <summary>Get required value from context variables</summary>
    public static T GetRequired<T>(this Dictionary<string, object> variables, string key)
    {
        if (!variables.TryGetValue(key, out var value))
            throw new KeyNotFoundException($"Variable '{key}' not found in context");
        
        if (value is T typed)
            return typed;
        
        throw new InvalidCastException($"Variable '{key}' is {value.GetType().Name}, expected {typeof(T).Name}");
    }
}

