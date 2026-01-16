using System.Collections.Concurrent;
using System.Threading.Channels;
using Aevatar.Agents.Cognitive.Core;

namespace Aevatar.CognitiveMesh.Models;

// ============================================================
//  MESH RUN
//  运行状态模型
// ============================================================

/// <summary>
/// 网格运行实例。
/// </summary>
public sealed class MeshRun
{
    /// <summary>运行 ID</summary>
    public string RunId { get; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>项目 ID</summary>
    public required string ProjectId { get; init; }

    /// <summary>项目名称</summary>
    public required string ProjectName { get; init; }

    /// <summary>使用的策略</summary>
    public required StrategyKind Strategy { get; init; }

    /// <summary>运行状态</summary>
    public MeshRunStatus Status { get; set; } = MeshRunStatus.Running;

    /// <summary>当前阶段</summary>
    public string? CurrentPhase { get; set; }

    /// <summary>进度百分比</summary>
    public float ProgressPercent { get; set; }

    /// <summary>执行时长</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>错误信息</summary>
    public string? Error { get; set; }

    /// <summary>执行结果</summary>
    public ReasoningResult? Result { get; set; }

    // ─────────────────────────────────────────────────────────
    //  统计信息
    // ─────────────────────────────────────────────────────────

    /// <summary>递归深度（MAKER）</summary>
    public int Depth { get; set; }

    /// <summary>总 LLM 调用次数</summary>
    public int TotalLlmCalls { get; set; }

    /// <summary>总 Token 数</summary>
    public long TotalTokens { get; set; }

    // ─────────────────────────────────────────────────────────
    //  输出
    // ─────────────────────────────────────────────────────────

    /// <summary>输出目录</summary>
    public string? OutputDir { get; set; }

    /// <summary>生成的文件 (category -> filename -> content)</summary>
    public ConcurrentDictionary<string, ConcurrentDictionary<string, string>> Files { get; } = new();

    /// <summary>时间线</summary>
    public List<TimelineEntry> Timeline { get; } = [];

    /// <summary>SSE 事件通道</summary>
    public Channel<MeshEvent> EventChannel { get; } = Channel.CreateUnbounded<MeshEvent>();

    /// <summary>取消令牌源（用于停止运行）</summary>
    public CancellationTokenSource CancellationTokenSource { get; } = new();

    /// <summary>当前递归深度</summary>
    public int CurrentDepth { get; set; }
}

/// <summary>
/// 时间线条目。
/// </summary>
public sealed record TimelineEntry(string Phase, string Message, DateTimeOffset Timestamp);

