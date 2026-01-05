using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Aevatar.Agents.Core.Telemetry;

// ============================================================
//  LLM Telemetry - Comprehensive AI Call Monitoring
//  Token Tracking, Latency Analysis, Cost Estimation
// ============================================================

/// <summary>
/// LLM call telemetry.
/// Provides comprehensive monitoring for token usage, latency, and cost.
/// </summary>
public static class LLMTelemetry
{
    // ============================================================
    //  Source Configuration
    // ============================================================

    public const string SourceName = "Aevatar.LLM";
    public const string MeterName = "Aevatar.LLM";
    public const string Version = "1.0.0";

    private static readonly ActivitySource ActivitySource = new(SourceName, Version);
    private static readonly Meter Meter = new(MeterName, Version);

    // ============================================================
    //  Counters - Cumulative Counts
    // ============================================================

    private static readonly Counter<long> LLMCalls = Meter.CreateCounter<long>(
        "aevatar.llm.calls.total",
        "calls",
        "Total LLM API calls");

    private static readonly Counter<long> LLMCallsSuccess = Meter.CreateCounter<long>(
        "aevatar.llm.calls.success.total",
        "calls",
        "Successful LLM API calls");

    private static readonly Counter<long> LLMCallsError = Meter.CreateCounter<long>(
        "aevatar.llm.calls.errors.total",
        "calls",
        "Failed LLM API calls");

    private static readonly Counter<long> PromptTokens = Meter.CreateCounter<long>(
        "aevatar.llm.tokens.prompt.total",
        "tokens",
        "Total prompt tokens consumed");

    private static readonly Counter<long> CompletionTokens = Meter.CreateCounter<long>(
        "aevatar.llm.tokens.completion.total",
        "tokens",
        "Total completion tokens generated");

    private static readonly Counter<long> TotalTokens = Meter.CreateCounter<long>(
        "aevatar.llm.tokens.total",
        "tokens",
        "Total tokens (prompt + completion)");

    private static readonly Counter<long> StreamingChunks = Meter.CreateCounter<long>(
        "aevatar.llm.streaming.chunks.total",
        "chunks",
        "Total streaming chunks received");

    private static readonly Counter<long> EmbeddingCalls = Meter.CreateCounter<long>(
        "aevatar.llm.embeddings.calls.total",
        "calls",
        "Total embedding API calls");

    private static readonly Counter<long> EmbeddingTokens = Meter.CreateCounter<long>(
        "aevatar.llm.embeddings.tokens.total",
        "tokens",
        "Total embedding tokens consumed");

    private static readonly Counter<long> ToolCalls = Meter.CreateCounter<long>(
        "aevatar.llm.tools.calls.total",
        "calls",
        "Total tool/function calls");

    private static readonly Counter<long> RetryAttempts = Meter.CreateCounter<long>(
        "aevatar.llm.retries.total",
        "retries",
        "Total retry attempts");

    // ============================================================
    //  Histograms - Latency Distribution
    // ============================================================

    private static readonly Histogram<double> CallDuration = Meter.CreateHistogram<double>(
        "aevatar.llm.call.duration",
        "ms",
        "LLM call duration (end-to-end)");

    private static readonly Histogram<double> TimeToFirstToken = Meter.CreateHistogram<double>(
        "aevatar.llm.ttft",
        "ms",
        "Time to first token (streaming)");

    private static readonly Histogram<double> TokensPerSecond = Meter.CreateHistogram<double>(
        "aevatar.llm.tokens.per_second",
        "tokens/s",
        "Token generation rate");

    private static readonly Histogram<int> PromptLength = Meter.CreateHistogram<int>(
        "aevatar.llm.prompt.length",
        "chars",
        "Prompt character length");

    private static readonly Histogram<int> ResponseLength = Meter.CreateHistogram<int>(
        "aevatar.llm.response.length",
        "chars",
        "Response character length");

    private static readonly Histogram<double> EmbeddingDuration = Meter.CreateHistogram<double>(
        "aevatar.llm.embedding.duration",
        "ms",
        "Embedding call duration");

    // ============================================================
    //  Gauges - Current State
    // ============================================================

    private static long _activeLLMCalls;
    private static long _activeStreamingSessions;

    static LLMTelemetry()
    {
        Meter.CreateObservableGauge(
            "aevatar.llm.calls.active",
            () => Interlocked.Read(ref _activeLLMCalls),
            "calls",
            "Currently active LLM calls");

        Meter.CreateObservableGauge(
            "aevatar.llm.streaming.active",
            () => Interlocked.Read(ref _activeStreamingSessions),
            "sessions",
            "Active streaming sessions");
    }

    // ============================================================
    //  LLM Call Traces + Metrics
    // ============================================================

    /// <summary>
    /// Start LLM call tracing.
    /// </summary>
    public static Activity? StartLLMCall(
        string agentId,
        string provider,
        string model,
        bool isStreaming = false)
    {
        Interlocked.Increment(ref _activeLLMCalls);
        if (isStreaming)
        {
            Interlocked.Increment(ref _activeStreamingSessions);
        }

        var activity = ActivitySource.StartActivity("llm.call", ActivityKind.Client);
        activity?.SetTag("agent.id", agentId.ToString());
        activity?.SetTag("llm.provider", provider);
        activity?.SetTag("llm.model", model);
        activity?.SetTag("llm.streaming", isStreaming);

        // Metrics
        LLMCalls.Add(1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("model", model),
            new KeyValuePair<string, object?>("streaming", isStreaming));

        return activity;
    }

    /// <summary>
    /// Record LLM call completion.
    /// </summary>
    public static void RecordLLMCallCompleted(
        Activity? activity,
        string provider,
        string model,
        double durationMs,
        int promptTokens,
        int completionTokens,
        int promptChars = 0,
        int responseChars = 0,
        bool isStreaming = false,
        double? ttftMs = null)
    {
        Interlocked.Decrement(ref _activeLLMCalls);
        if (isStreaming)
        {
            Interlocked.Decrement(ref _activeStreamingSessions);
        }

        var tags = new[]
        {
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("model", model)
        };

        // Token Metrics
        PromptTokens.Add(promptTokens, tags);
        CompletionTokens.Add(completionTokens, tags);
        TotalTokens.Add(promptTokens + completionTokens, tags);

        // Duration Metrics
        CallDuration.Record(durationMs, tags);

        // Length Metrics
        if (promptChars > 0)
        {
            PromptLength.Record(promptChars, tags);
        }
        if (responseChars > 0)
        {
            ResponseLength.Record(responseChars, tags);
        }

        // Token Rate (if we have duration and completion tokens)
        if (durationMs > 0 && completionTokens > 0)
        {
            var tokensPerSec = completionTokens / (durationMs / 1000.0);
            TokensPerSecond.Record(tokensPerSec, tags);
        }

        // TTFT for streaming
        if (ttftMs.HasValue)
        {
            TimeToFirstToken.Record(ttftMs.Value, tags);
        }

        // Success counter
        LLMCallsSuccess.Add(1, tags);

        // Update Activity
        if (activity != null)
        {
            activity.SetTag("llm.duration.ms", durationMs);
            activity.SetTag("llm.tokens.prompt", promptTokens);
            activity.SetTag("llm.tokens.completion", completionTokens);
            activity.SetTag("llm.tokens.total", promptTokens + completionTokens);

            if (ttftMs.HasValue)
            {
                activity.SetTag("llm.ttft.ms", ttftMs.Value);
            }

            activity.SetStatus(ActivityStatusCode.Ok);
        }
    }

    /// <summary>
    /// Record LLM call failure.
    /// </summary>
    public static void RecordLLMCallFailed(
        Activity? activity,
        string provider,
        string model,
        string errorType,
        string errorMessage,
        double durationMs,
        bool isStreaming = false)
    {
        Interlocked.Decrement(ref _activeLLMCalls);
        if (isStreaming)
        {
            Interlocked.Decrement(ref _activeStreamingSessions);
        }

        var tags = new[]
        {
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("model", model),
            new KeyValuePair<string, object?>("error.type", errorType)
        };

        LLMCallsError.Add(1, tags);
        CallDuration.Record(durationMs,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("model", model));

        // Update Activity
        if (activity != null)
        {
            activity.SetStatus(ActivityStatusCode.Error, errorMessage);
            activity.SetTag("llm.error.type", errorType);
            activity.SetTag("llm.error.message", errorMessage);
            activity.SetTag("llm.duration.ms", durationMs);
        }
    }

    // ============================================================
    //  Streaming Tracking
    // ============================================================

    /// <summary>
    /// Record streaming chunk.
    /// </summary>
    public static void RecordStreamingChunk(
        Activity? activity,
        string provider,
        string model,
        int chunkIndex,
        int chunkTokens = 0)
    {
        StreamingChunks.Add(1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("model", model));

        activity?.AddEvent(new ActivityEvent("streaming.chunk",
            tags: new ActivityTagsCollection
            {
                { "chunk.index", chunkIndex },
                { "chunk.tokens", chunkTokens }
            }));
    }

    // ============================================================
    //  Embedding Tracking
    // ============================================================

    /// <summary>
    /// Start embedding call tracing.
    /// </summary>
    public static Activity? StartEmbeddingCall(
        Guid agentId,
        string provider,
        string model,
        int inputCount)
    {
        var activity = ActivitySource.StartActivity("llm.embedding", ActivityKind.Client);
        activity?.SetTag("agent.id", agentId.ToString());
        activity?.SetTag("llm.provider", provider);
        activity?.SetTag("llm.model", model);
        activity?.SetTag("embedding.input_count", inputCount);

        EmbeddingCalls.Add(1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("model", model));

        return activity;
    }

    /// <summary>
    /// Record embedding call completion.
    /// </summary>
    public static void RecordEmbeddingCompleted(
        Activity? activity,
        string provider,
        string model,
        double durationMs,
        int totalTokens,
        int dimensions)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("model", model)
        };

        EmbeddingTokens.Add(totalTokens, tags);
        EmbeddingDuration.Record(durationMs, tags);

        if (activity != null)
        {
            activity.SetTag("embedding.duration.ms", durationMs);
            activity.SetTag("embedding.tokens", totalTokens);
            activity.SetTag("embedding.dimensions", dimensions);
            activity.SetStatus(ActivityStatusCode.Ok);
        }
    }

    // ============================================================
    //  Tool/Function Call Tracking
    // ============================================================

    /// <summary>
    /// Start tool call tracing.
    /// </summary>
    public static Activity? StartToolCall(
        Guid agentId,
        string toolName,
        string toolCategory)
    {
        var activity = ActivitySource.StartActivity("llm.tool.call", ActivityKind.Internal);
        activity?.SetTag("agent.id", agentId.ToString());
        activity?.SetTag("tool.name", toolName);
        activity?.SetTag("tool.category", toolCategory);

        ToolCalls.Add(1,
            new KeyValuePair<string, object?>("tool.name", toolName),
            new KeyValuePair<string, object?>("tool.category", toolCategory));

        return activity;
    }

    /// <summary>
    /// Record tool call completion.
    /// </summary>
    public static void RecordToolCallCompleted(
        Activity? activity,
        string toolName,
        double durationMs,
        bool success,
        string? error = null)
    {
        if (activity != null)
        {
            activity.SetTag("tool.duration.ms", durationMs);
            activity.SetTag("tool.success", success);

            if (success)
            {
                activity.SetStatus(ActivityStatusCode.Ok);
            }
            else
            {
                activity.SetStatus(ActivityStatusCode.Error, error);
                activity.SetTag("tool.error", error);
            }
        }
    }

    // ============================================================
    //  Retry Tracking
    // ============================================================

    /// <summary>
    /// Record retry attempt.
    /// </summary>
    public static void RecordRetry(
        Activity? activity,
        string provider,
        string model,
        int attemptNumber,
        string reason)
    {
        RetryAttempts.Add(1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("model", model),
            new KeyValuePair<string, object?>("reason", reason));

        activity?.AddEvent(new ActivityEvent("llm.retry",
            tags: new ActivityTagsCollection
            {
                { "attempt", attemptNumber },
                { "reason", reason }
            }));
    }

    // ============================================================
    //  Cost Estimation (Optional)
    // ============================================================

    /// <summary>
    /// Estimate call cost based on token count.
    /// </summary>
    public static void RecordEstimatedCost(
        Activity? activity,
        string provider,
        string model,
        int promptTokens,
        int completionTokens,
        decimal promptCostPer1K,
        decimal completionCostPer1K)
    {
        var promptCost = promptTokens / 1000.0m * promptCostPer1K;
        var completionCost = completionTokens / 1000.0m * completionCostPer1K;
        var totalCost = promptCost + completionCost;

        activity?.SetTag("llm.cost.prompt", promptCost);
        activity?.SetTag("llm.cost.completion", completionCost);
        activity?.SetTag("llm.cost.total", totalCost);
        activity?.SetTag("llm.cost.currency", "USD");
    }
}
