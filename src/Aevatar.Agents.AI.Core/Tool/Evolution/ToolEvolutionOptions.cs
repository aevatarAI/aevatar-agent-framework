namespace Aevatar.Agents.AI.Tool.Evolution;

// ============================================================
//  Tool Evolution Options
//
//  中文 + ASCII:
//  - 显式开关 + best-effort 采集/发布。
//  - Cognitive 侧使用同一套选项实现工具演化闭环。
// ============================================================
/// <summary>
/// Tool evolution options (opt-in).
/// </summary>
public sealed class ToolEvolutionOptions
{
    /// <summary>
    /// Master switch for tool evolution and metrics.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Enable tool execution feedback hooks.
    /// </summary>
    public bool EnableFeedbackHooks { get; set; } = true;

    /// <summary>
    /// Enable in-memory metrics aggregation.
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// Publish ToolExecutionFeedback events (best-effort).
    /// </summary>
    public bool EnableFeedbackEvents { get; set; } = true;

    /// <summary>
    /// Append tool feedback into IMemoryStore (best-effort).
    /// </summary>
    public bool EnableMemoryStoreAppend { get; set; }

    /// <summary>
    /// Emit ToolMetricsSnapshot every N tool calls (0 disables).
    /// </summary>
    public int MetricsSnapshotEveryNCalls { get; set; } = 100;

    /// <summary>
    /// Default policy applied to evolved tools when no override is provided.
    /// </summary>
    public ToolEvolutionPolicy DefaultPolicy { get; set; } = new();

    /// <summary>
    /// Storage directory for evolved tool source files.
    /// </summary>
    public string ToolStorageDirectory { get; set; } = "~/.aevatar/tools/evolved";

    /// <summary>
    /// Cognitive: allow explicit tool_call steps.
    /// </summary>
    public bool EnableToolCalls { get; set; }

    /// <summary>
    /// Cognitive: allow tool_evolve steps.
    /// </summary>
    public bool EnableToolEvolutionSteps { get; set; }

    /// <summary>
    /// Cognitive: set evolution trigger on parse failures.
    /// </summary>
    public bool AutoTriggerOnParseFailure { get; set; }

    /// <summary>
    /// Cognitive: set evolution trigger on tool failures.
    /// </summary>
    public bool AutoTriggerOnToolFailure { get; set; }
}
