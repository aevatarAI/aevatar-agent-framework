using System.Collections.Concurrent;
using Aevatar.Agents.Cognitive.Core;
using Aevatar.Agents.Cognitive.Streaming;
using Aevatar.AxiomReasoning.EventStreaming.Events;

namespace Aevatar.AxiomReasoning.Models;

// ============================================================
//  AXIOM SESSION
//  公理推理会话模型（仿照 PaperReview 的 Session + SSE）
// ============================================================



/// <summary>
/// 公理推理会话。
/// </summary>
public sealed class AxiomSession
{
    private const string EventHubName = "AxiomSession.EventHub";
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public AxiomSessionStatus Status { get; set; } = AxiomSessionStatus.Pending;

    // 用户输入（原始）
    public string AxiomsText { get; set; } = "";
    public string Goal { get; set; } = "";
    public string SeedHypothesis { get; set; } = "";

    // 运行模式
    // - Workflow: 选择 Cognitive DSL workflow（支持多个）
    // - Language: 控制 LLM 生成内容的自然语言（不影响 JSON key）
    public string Workflow { get; set; } = "hypothesis_promotion_loop";
    public string Language { get; set; } = "English";

    // 共识参数
    public int K { get; set; } = 3;
    public int MaxRounds { get; set; } = 10;
    public int MaxDepth { get; set; } = 10;

    // 运行预算（用于长时间探索）
    // NOTE:
    // - 这些是“服务端执行预算”，用于限制/放宽 MaxDuration/MaxTokens/MaxLlmCalls
    // - 不跨运行时边界，仅用于本服务配置
    public int MaxDurationMinutes { get; set; } = 30;
    public int MaxLlmCallsBudget { get; set; } = 300;
    public long MaxTokensBudget { get; set; } = 800_000;

    // 工作流行为开关：是否在某次证明失败后继续提出新定理
    public bool ContinueOnFailure { get; set; } = false;

    // 运行统计
    public int ProgressPercent { get; set; }
    public string CurrentPhase { get; set; } = "";
    public int TotalLlmCalls { get; set; }
    public long TotalTokens { get; set; }
    public TimeSpan Duration { get; set; }

    // 引擎结果
    public ReasoningResult? Result { get; set; }
    public string? Error { get; set; }

    // 时间线
    public List<TimelineEntry> Timeline { get; } = [];

    // SSE 事件通道
    // NOTE:
    // - 旧实现用 Channel<T>（queue 语义），多个 SSE 连接会“抢消息”。
    // - 现在用 BroadcastEventHub<T>（pub-sub 语义），每个连接都能收到完整事件流。
    public BroadcastEventHub<AxiomEvent> EventHub { get; } = new(replayBufferSize: 256, hubName: EventHubName);

    public CancellationTokenSource CancellationTokenSource { get; } = new();

    // 产出文件（内存 + 可选落盘）
    public ConcurrentDictionary<string, ConcurrentDictionary<string, string>> Files { get; } = new();
    public string? OutputDir { get; set; }
}