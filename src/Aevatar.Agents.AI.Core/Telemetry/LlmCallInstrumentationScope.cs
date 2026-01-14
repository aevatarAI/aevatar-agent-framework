using System.Diagnostics;
using Aevatar.Agents.Core.Observability;
using Aevatar.Agents.Core.Telemetry;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

/// <summary>
/// LLM call instrumentation helper (telemetry + logs + stopwatch).
///
/// 中文 + ASCII:
/// - 目的：把 ChatAsync/ChatStreamAsync 的横切细节收敛成一个点，避免未来漏记/漏 stop
/// - 行为：严格保持“现有日志 + telemetry 调用点”的语义（不引入新事件/新日志）
/// </summary>
internal sealed class LlmCallInstrumentationScope : IDisposable
{
    private readonly ILogger _logger;
    private readonly string _agentId;
    private readonly string _provider;
    private readonly string _model;
    private readonly Activity? _activity;
    private readonly IDisposable? _logScope;
    private readonly Stopwatch _stopwatch;

    internal LlmCallInstrumentationScope(
        ILogger logger,
        string agentId,
        string provider,
        string model,
        bool isStreaming)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _agentId = agentId ?? string.Empty;
        _provider = provider ?? "unknown";
        _model = model ?? string.Empty;

        _stopwatch = Stopwatch.StartNew();
        _activity = LLMTelemetry.StartLLMCall(_agentId, _provider, _model, isStreaming);
        _logScope = LoggingScope.CreateLLMCallScope(_logger, _agentId, _provider, _model);

        AgentLogMessages.LLMCallStarting(_logger, _agentId, _provider, _model);
    }

    internal long ElapsedMilliseconds => _stopwatch.ElapsedMilliseconds;

    internal void StopWithoutRecording()
    {
        if (_stopwatch.IsRunning)
            _stopwatch.Stop();
    }

    internal void RecordCompleted(
        int promptTokens,
        int completionTokens,
        int promptChars,
        int responseChars)
    {
        StopWithoutRecording();

        LLMTelemetry.RecordLLMCallCompleted(
            _activity, _provider, _model, _stopwatch.ElapsedMilliseconds,
            promptTokens, completionTokens,
            promptChars: promptChars,
            responseChars: responseChars);

        AgentLogMessages.LLMCallCompleted(_logger, _agentId, promptTokens, completionTokens, _stopwatch.ElapsedMilliseconds);
    }

    internal void RecordFailed(Exception ex)
    {
        StopWithoutRecording();

        var errorType = ex.GetType().Name;

        LLMTelemetry.RecordLLMCallFailed(
            _activity, _provider, _model, errorType, ex.Message,
            _stopwatch.ElapsedMilliseconds);

        AgentLogMessages.LLMCallFailed(_logger, _agentId, errorType, ex.Message, ex);
    }

    public void Dispose()
    {
        _logScope?.Dispose();
        _activity?.Dispose();
    }
}


