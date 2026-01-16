namespace Aevatar.Agents.Cognitive.Core;

// ============================================================
//  MESH PROJECT ABSTRACTION
//  认知网格项目定义
// ============================================================

/// <summary>
/// 认知网格项目。
/// 封装了一个可执行的认知任务配置。
/// </summary>
public interface IMeshProject
{
    /// <summary>
    /// 项目唯一标识。
    /// </summary>
    string Id { get; }

    /// <summary>
    /// 项目名称。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 项目描述。
    /// </summary>
    string Description { get; }

    /// <summary>
    /// 项目图标。
    /// </summary>
    string Icon { get; }

    /// <summary>
    /// 使用的策略类型。
    /// </summary>
    StrategyKind Strategy { get; }

    /// <summary>
    /// 任务描述。
    /// </summary>
    string Task { get; }

    /// <summary>
    /// 构建执行选项。
    /// </summary>
    ReasoningOptions BuildOptions();
}

/// <summary>
/// 项目运行状态。
/// </summary>
public enum MeshRunStatus
{
    /// <summary>空闲</summary>
    Idle,

    /// <summary>运行中</summary>
    Running,

    /// <summary>已暂停</summary>
    Paused,

    /// <summary>已完成</summary>
    Completed,

    /// <summary>失败</summary>
    Failed,

    /// <summary>已取消</summary>
    Cancelled
}

