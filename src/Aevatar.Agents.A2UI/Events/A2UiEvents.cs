using Aevatar.Agents.AGUI;

namespace Aevatar.Agents.A2UI.Events;

// ============================================================
//  A2UI EVENTS (Frontend/SDK aligned)
//  Matches the "frontend SDK definition"
// ============================================================

public sealed record SurfaceUpdateEvent : AgUiEvent
{
    public override string Type => "surfaceUpdate";
    public required string SurfaceId { get; init; }
    public required string Template { get; init; }
    public object? InitialData { get; init; }
}

public sealed record DataModelUpdateEvent : AgUiEvent
{
    public override string Type => "dataModelUpdate";
    public required string SurfaceId { get; init; }
    public required object Data { get; init; }
}

public sealed record BeginRenderingEvent : AgUiEvent
{
    public override string Type => "beginRendering";
    public required string SurfaceId { get; init; }
}

public sealed record DeleteSurfaceEvent : AgUiEvent
{
    public override string Type => "deleteSurface";
    public required string SurfaceId { get; init; }
}

public sealed record A2UiUserActionEvent : AgUiEvent
{
    public override string Type => "A2uiUserAction";
    public required string SurfaceId { get; init; }
    public required string ActionId { get; init; }
    public object? Payload { get; init; }
}

