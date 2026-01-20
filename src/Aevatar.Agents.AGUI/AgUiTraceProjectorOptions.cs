using Aevatar.Agents.Abstractions.Tracing;

namespace Aevatar.Agents.AGUI;

/// <summary>
/// Options for projecting ExecutionTraceEvent to AG-UI events.
/// </summary>
public sealed class AgUiTraceProjectorOptions
{
    public Func<ExecutionTraceEvent, string>? ResolveThreadId { get; set; }
    public Func<ExecutionTraceEvent, string>? ResolveRunId { get; set; }
    public Func<ExecutionTraceEvent, string?>? ResolveMessageId { get; set; }
    public Func<ExecutionTraceEvent, string?>? ResolveToolCallId { get; set; }
    public Func<ExecutionTraceEvent, string?>? ResolveToolName { get; set; }
    public Func<ExecutionTraceEvent, string>? ResolveRole { get; set; }
}
