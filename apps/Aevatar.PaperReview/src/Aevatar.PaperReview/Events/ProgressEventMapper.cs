using System.Collections.Concurrent;
using Aevatar.Agents.Cognitive.Core;
using Aevatar.PaperReview.Models;

namespace Aevatar.PaperReview.Events;

// ============================================================
//  进度事件映射器
//  职责：将 Cognitive DSL 进度映射为 UI 事件
// ============================================================

/// <summary>
/// 进度事件映射器 - 将 ReasoningProgress 转换为 UI 事件。
/// </summary>
public sealed class ProgressEventMapper
{
    private readonly ConcurrentDictionary<string, string> _stepPrompts = new();
    private readonly ConcurrentDictionary<string, string> _stepContent = new();
    
    /// <summary>
    /// 缓存步骤的 User Prompt。
    /// </summary>
    public void CachePrompt(string stepId, string? prompt)
    {
        if (!string.IsNullOrEmpty(prompt))
            _stepPrompts[stepId] = prompt;
    }
    
    /// <summary>
    /// 缓存步骤的响应内容。
    /// </summary>
    public void CacheContent(string stepId, string? content)
    {
        if (!string.IsNullOrEmpty(content))
            _stepContent[stepId] = content;
    }
    
    /// <summary>
    /// 获取缓存的 Prompt。
    /// </summary>
    public string? GetCachedPrompt(string stepId) =>
        _stepPrompts.TryGetValue(stepId, out var p) ? p : null;
    
    /// <summary>
    /// 获取缓存的内容。
    /// </summary>
    public string? GetCachedContent(string stepId) =>
        _stepContent.TryGetValue(stepId, out var c) ? c : null;
    
    /// <summary>
    /// 映射 ReasoningProgress 到 ReviewPhase。
    /// </summary>
    public ReviewPhase MapPhase(ReasoningProgress p)
    {
        var stepId = p.StepId ?? p.TaskId ?? "";
        var phaseText = p.Phase ?? "";
        var stepType = p.StepType ?? "";
        
        var stepIdLower = stepId.ToLowerInvariant();
        var phaseUpper = phaseText.ToUpperInvariant();
        var stepTypeLower = stepType.ToLowerInvariant();
        
        // 按 stepId 精确映射（优先级最高）
        if (stepIdLower.Contains("check_atomic") || stepIdLower.Contains("decompose"))
            return ReviewPhase.Decomposing;
        if (stepIdLower.Contains("compose"))
            return ReviewPhase.Composing;
        if (stepIdLower.Contains("solve") || stepIdLower.Contains("execute"))
            return ReviewPhase.Solving;
        
        // 按 phase/stepType 映射
        if (phaseUpper.Contains("START"))
            return ReviewPhase.Starting;
        if (stepTypeLower == "vote" || phaseUpper.Contains("VOTE"))
            return ReviewPhase.Voting;
        if (stepTypeLower == "fan_out" || phaseUpper.Contains("DECOMPOSE"))
            return ReviewPhase.Decomposing;
        if (phaseUpper.Contains("REVIEW") || phaseUpper.Contains("COMPOS"))
            return ReviewPhase.Composing;
        if (phaseUpper.Contains("RESULT") || phaseUpper.Contains("COMPLETE"))
            return ReviewPhase.Completed;
        if (phaseUpper.Contains("FAIL") || phaseUpper.Contains("ERROR"))
            return ReviewPhase.Failed;
        
        // 默认
        return ReviewPhase.Solving;
    }
    
    /// <summary>
    /// 判断是否为 LLM 调用步骤的开始。
    /// </summary>
    public bool IsLlmCallRunning(ReasoningProgress p) =>
        p.StepType?.Equals("llm_call", StringComparison.OrdinalIgnoreCase) == true &&
        (p.StepStatus?.ToLowerInvariant().Contains("running") == true);
    
    /// <summary>
    /// 判断是否为 LLM 调用步骤的完成。
    /// </summary>
    public bool IsLlmCallCompleted(ReasoningProgress p) =>
        p.StepType?.Equals("llm_call", StringComparison.OrdinalIgnoreCase) == true &&
        (p.StepStatus?.ToLowerInvariant().Contains("completed") == true);
    
    /// <summary>
    /// 判断是否为投票步骤。
    /// </summary>
    public bool IsVoteStep(ReasoningProgress p) =>
        p.StepType?.Equals("vote", StringComparison.OrdinalIgnoreCase) == true;
    
    /// <summary>
    /// 判断是否达成共识。
    /// </summary>
    public bool IsConsensusReached(ReasoningProgress p)
    {
        var votesNeeded = p.VoteK ?? 0;
        var leaderVotes = p.VoteCurrentVotes ?? 0;
        var isCompleted = p.StepStatus?.ToLowerInvariant().Contains("completed") == true;
        return isCompleted || (votesNeeded > 0 && leaderVotes >= votesNeeded);
    }
    
    /// <summary>
    /// 构建 Worker 启动事件。
    /// </summary>
    public WorkerStartedEvent BuildWorkerStarted(string sessionId, string stepId, string? role = null) => new()
    {
        SessionId = sessionId,
        WorkerId = stepId,
        TaskId = stepId,
        Role = role ?? "LLM",
        ProviderName = "deepseek"
    };
    
    /// <summary>
    /// 构建 LLM 调用开始事件。
    /// </summary>
    public LlmCallStartEvent BuildLlmCallStart(string sessionId, ReasoningProgress p)
    {
        var stepId = p.StepId ?? p.TaskId ?? sessionId;
        return new LlmCallStartEvent
        {
            SessionId = sessionId,
            CallId = stepId,
            WorkerId = stepId,
            ProviderName = "deepseek",
            SystemPrompt = p.SystemPrompt,
            UserPrompt = p.UserPrompt ?? GetCachedPrompt(stepId) ?? p.Message ?? "(llm_call)",
            Phase = "LLM"
        };
    }
    
    /// <summary>
    /// 构建 LLM 流式事件。
    /// </summary>
    public LlmStreamingEvent BuildLlmStreaming(string sessionId, string stepId, string token) => new()
    {
        SessionId = sessionId,
        CallId = stepId,
        WorkerId = stepId,
        Token = token
    };
    
    /// <summary>
    /// 构建 LLM 调用完成事件。
    /// </summary>
    public LlmCallCompleteEvent BuildLlmCallComplete(string sessionId, string stepId, string content) => new()
    {
        SessionId = sessionId,
        CallId = stepId,
        WorkerId = stepId,
        Success = true,
        Content = content,
        PromptTokens = 0,
        CompletionTokens = 0,
        LatencyMs = 0,
        ProviderName = "deepseek",
        Phase = "LLM"
    };
    
    /// <summary>
    /// 构建 Worker 完成事件。
    /// </summary>
    public WorkerCompletedEvent BuildWorkerCompleted(string sessionId, string stepId, string content) => new()
    {
        SessionId = sessionId,
        WorkerId = stepId,
        TaskId = stepId,
        Success = true,
        Content = content,
        ContentPreview = content.Length > 100 ? content[..100] + "..." : content,
        LatencyMs = 0,
        TotalTokens = 0
    };
    
    /// <summary>
    /// 构建投票轮次事件。
    /// </summary>
    public VotingRoundEvent BuildVotingRound(string sessionId, ReasoningProgress p)
    {
        var stepId = p.StepId ?? p.TaskId ?? sessionId;
        var leaderVotes = p.VoteCurrentVotes ?? 0;
        var votesNeeded = p.VoteK ?? 0;
        
        return new VotingRoundEvent
        {
            SessionId = sessionId,
            TaskId = stepId,
            Round = p.VoteRound ?? 0,
            VotesNeeded = votesNeeded,
            Candidates =
            [
                new VoteCandidateInfo
                {
                    CandidateId = "leader",
                    ContentPreview = p.Message ?? "(leader)",
                    Votes = leaderVotes,
                    IsLeader = true
                }
            ],
            ConsensusReached = votesNeeded > 0 && leaderVotes >= votesNeeded
        };
    }
    
    /// <summary>
    /// 构建共识事件。
    /// </summary>
    public ConsensusEvent BuildConsensus(string sessionId, ReasoningProgress p) => new()
    {
        SessionId = sessionId,
        Round = p.VoteRound ?? 0,
        LeaderVotes = p.VoteCurrentVotes ?? 0,
        TotalVotes = p.VoteCurrentVotes ?? 0
    };
}
