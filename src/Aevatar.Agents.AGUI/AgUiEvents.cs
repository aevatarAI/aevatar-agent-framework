namespace Aevatar.Agents.AGUI;

// ============================================================
//  AG-UI EVENTS (minimal subset)
//
//  Goals:
//  - Align Agent system's SSE output with AG-UI Protocol (standardized event stream interface)
//  - Keep implementation minimal: only implement event types needed by this project + CUSTOM extensions
//
//  NOTE:
//  - JSON serialization uses camelCase (configured in Program.cs), so PascalCase naming here is fine.
//  - Timestamp uses Unix epoch milliseconds (consistent with AG-UI SDK examples).
// ============================================================

public abstract record AgUiEvent
{
    public abstract string Type { get; }
    public long? Timestamp { get; init; }
    public object? RawEvent { get; init; }
}

// ─────────────────────────────────────────────────────────────
//  Run lifecycle
// ─────────────────────────────────────────────────────────────

public sealed record RunStartedEvent : AgUiEvent
{
    public override string Type => "RUN_STARTED";
    public required string ThreadId { get; init; }
    public required string RunId { get; init; }
}

public sealed record RunFinishedEvent : AgUiEvent
{
    public override string Type => "RUN_FINISHED";
    public required string ThreadId { get; init; }
    public required string RunId { get; init; }
    public object? Result { get; init; }
}

public sealed record RunErrorEvent : AgUiEvent
{
    public override string Type => "RUN_ERROR";
    public required string Message { get; init; }
    public string? Code { get; init; }
}

// ─────────────────────────────────────────────────────────────
//  Step lifecycle
// ─────────────────────────────────────────────────────────────

public sealed record StepStartedEvent : AgUiEvent
{
    public override string Type => "STEP_STARTED";
    public required string StepName { get; init; }
}

public sealed record StepFinishedEvent : AgUiEvent
{
    public override string Type => "STEP_FINISHED";
    public required string StepName { get; init; }
}

// ─────────────────────────────────────────────────────────────
//  Text message streaming
// ─────────────────────────────────────────────────────────────

public sealed record TextMessageStartEvent : AgUiEvent
{
    public override string Type => "TEXT_MESSAGE_START";
    public required string MessageId { get; init; }
    public required string Role { get; init; } // "assistant" | "user" | "system" | ...
}

public sealed record TextMessageContentEvent : AgUiEvent
{
    public override string Type => "TEXT_MESSAGE_CONTENT";
    public required string MessageId { get; init; }
    public required string Delta { get; init; } // Non-empty string
}

public sealed record TextMessageEndEvent : AgUiEvent
{
    public override string Type => "TEXT_MESSAGE_END";
    public required string MessageId { get; init; }
}

// ─────────────────────────────────────────────────────────────
//  State sync
// ─────────────────────────────────────────────────────────────

public sealed record StateSnapshotEvent : AgUiEvent
{
    public override string Type => "STATE_SNAPSHOT";
    public required object Snapshot { get; init; }
}

public sealed record StateDeltaEvent : AgUiEvent
{
    public override string Type => "STATE_DELTA";
    public required object[] Delta { get; init; } // JSON Patch (RFC 6902)
}

// ─────────────────────────────────────────────────────────────
//  Message sync
// ─────────────────────────────────────────────────────────────

public sealed record AgUiMessage
{
    public required string Id { get; init; }
    public required string Role { get; init; }   // "user" | "assistant" | "system" | "tool" | ...
    public required string Content { get; init; }
    public string? Name { get; init; }
    public string? ToolCallId { get; init; }
}

public sealed record MessagesSnapshotEvent : AgUiEvent
{
    public override string Type => "MESSAGES_SNAPSHOT";
    public required List<AgUiMessage> Messages { get; init; }
}

// ─────────────────────────────────────────────────────────────
//  Extension point
// ─────────────────────────────────────────────────────────────

public sealed record CustomEvent : AgUiEvent
{
    public override string Type => "CUSTOM";
    public required string Name { get; init; }
    public object? Value { get; init; }
}

// ─────────────────────────────────────────────────────────────
//  Tool usage
// ─────────────────────────────────────────────────────────────

public sealed record ToolCallStartEvent : AgUiEvent
{
    public override string Type => "TOOL_CALL_START";
    public required string MessageId { get; init; }
    public required string ToolCallId { get; init; }
    public required string ToolName { get; init; }
}

public sealed record ToolCallArgsEvent : AgUiEvent
{
    public override string Type => "TOOL_CALL_ARGS";
    public required string MessageId { get; init; }
    public required string ToolCallId { get; init; }
    public required string ArgsDelta { get; init; }
}

public sealed record ToolCallEndEvent : AgUiEvent
{
    public override string Type => "TOOL_CALL_END";
    public required string MessageId { get; init; }
    public required string ToolCallId { get; init; }
}

public sealed record ToolCallResultEvent : AgUiEvent
{
    public override string Type => "TOOL_CALL_RESULT";
    public required string MessageId { get; init; }
    public required string ToolCallId { get; init; }
    public required string Result { get; init; }
}

// ─────────────────────────────────────────────────────────────
//  Research direction pivot events
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Emitted when the system detects a potential research direction change.
/// </summary>
public sealed record PivotDetectedEvent : AgUiEvent
{
    public override string Type => "PIVOT_DETECTED";
    public required string SessionId { get; init; }
    public required string PivotId { get; init; }
    public required string DetectedIntent { get; init; }
    public required double Confidence { get; init; }
    public string? NewTopic { get; init; }
    public bool NeedsClarification { get; init; }
    public IReadOnlyList<string> PreserveAspects { get; init; } = [];
}

/// <summary>
/// Emitted when a pivot operation begins executing.
/// </summary>
public sealed record PivotStartedEvent : AgUiEvent
{
    public override string Type => "PIVOT_STARTED";
    public required string SessionId { get; init; }
    public required string PivotId { get; init; }
    public string? OldDirection { get; init; }
    public string? NewDirection { get; init; }
}

/// <summary>
/// Emitted during pivot execution to report progress.
/// </summary>
public sealed record PivotProgressEvent : AgUiEvent
{
    public override string Type => "PIVOT_PROGRESS";
    public required string SessionId { get; init; }
    public required string PivotId { get; init; }
    public required string Stage { get; init; }
    public required string Message { get; init; }
    public double? Progress { get; init; }
}

/// <summary>
/// Emitted when a pivot operation completes successfully.
/// </summary>
public sealed record PivotCompletedEvent : AgUiEvent
{
    public override string Type => "PIVOT_COMPLETED";
    public required string SessionId { get; init; }
    public required string PivotId { get; init; }
    public required int CancelledCount { get; init; }
    public required int PreservedCount { get; init; }
    public required int NewCount { get; init; }
    public long DurationMs { get; init; }
    public string? Summary { get; init; }
}

/// <summary>
/// Emitted when a pivot operation fails.
/// </summary>
public sealed record PivotErrorEvent : AgUiEvent
{
    public override string Type => "PIVOT_ERROR";
    public required string SessionId { get; init; }
    public required string PivotId { get; init; }
    public required string ErrorMessage { get; init; }
    public string? ErrorCode { get; init; }
}

/// <summary>
/// Emitted to request user clarification for low-confidence direction changes.
/// </summary>
public sealed record PivotClarificationRequestEvent : AgUiEvent
{
    public override string Type => "PIVOT_CLARIFICATION_REQUEST";
    public required string SessionId { get; init; }
    public required string PivotId { get; init; }
    public required string Question { get; init; }
    public string? SuggestedTopic { get; init; }
    public double Confidence { get; init; }
}

