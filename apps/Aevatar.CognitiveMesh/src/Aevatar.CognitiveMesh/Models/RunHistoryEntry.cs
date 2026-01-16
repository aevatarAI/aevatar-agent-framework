using Aevatar.Agents.Cognitive.Core;

namespace Aevatar.CognitiveMesh.Models;

// ============================================================
//  RUN HISTORY ENTRY
//  运行历史条目
// ============================================================

/// <summary>
/// 运行历史条目。
/// </summary>
public sealed class RunHistoryEntry
{
    /// <summary>运行 ID</summary>
    public required string RunId { get; init; }

    /// <summary>开始时间</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>完成时间</summary>
    public DateTimeOffset CompletedAt { get; init; }

    /// <summary>运行状态</summary>
    public MeshRunStatus Status { get; init; }

    /// <summary>执行时长</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>LLM 调用次数</summary>
    public int TotalLlmCalls { get; init; }

    /// <summary>总 Token 数</summary>
    public long TotalTokens { get; init; }

    /// <summary>错误信息</summary>
    public string? Error { get; init; }

    /// <summary>输出目录</summary>
    public string? OutputDir { get; init; }

    /// <summary>文件数量</summary>
    public int FileCount { get; init; }
}

