namespace Aevatar.PaperReview.Models;

// ============================================================
//  阶段跟踪器
//  职责：跟踪评审阶段的统计信息和详情
// ============================================================

/// <summary>
/// 阶段跟踪器 - 记录阶段开始时间、统计和详情。
/// </summary>
public sealed class StageTracker
{
    public string CurrentStage { get; set; } = "";
    public DateTimeOffset StageStartTime { get; set; }
    
    // 统计
    public int LlmCalls { get; set; }
    public long Tokens { get; set; }
    public int VotingRounds { get; set; }
    public int WorkerCount { get; set; }
    public bool ConsensusReached { get; set; }
    
    // 详情
    public List<CandidateDetail> Candidates { get; } = [];
    public List<WorkerOutput> WorkerOutputs { get; } = [];
    public string? WinnerContent { get; set; }
    
    /// <summary>
    /// 重置统计和详情，保留当前阶段信息。
    /// </summary>
    public void Reset()
    {
        LlmCalls = 0;
        Tokens = 0;
        VotingRounds = 0;
        WorkerCount = 0;
        ConsensusReached = false;
        Candidates.Clear();
        WorkerOutputs.Clear();
        WinnerContent = null;
    }
    
    /// <summary>
    /// 开始新阶段。
    /// </summary>
    public void StartStage(string stageName)
    {
        Reset();
        CurrentStage = stageName;
        StageStartTime = DateTimeOffset.UtcNow;
    }
    
    /// <summary>
    /// 构建当前阶段的统计摘要。
    /// </summary>
    public StageStats BuildStats() => new()
    {
        LlmCalls = LlmCalls,
        Tokens = Tokens,
        WorkerCount = WorkerCount,
        VotingRounds = VotingRounds,
        ConsensusReached = ConsensusReached
    };
    
    /// <summary>
    /// 构建当前阶段的详情。
    /// </summary>
    public StageDetails BuildDetails() => new()
    {
        Candidates = Candidates.Count > 0 ? [..Candidates] : null,
        WorkerOutputs = WorkerOutputs.Count > 0 ? [..WorkerOutputs] : null,
        WinnerContent = WinnerContent
    };
}

// CandidateDetail 和 WorkerOutput 定义在 ReviewSession.cs 中
