using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VibeResearching.Vibe.ReviewAgent;
using Aevatar.VibeResearching.Api.ReviewAgent.Events;
using Aevatar.VibeResearching.Api.ReviewAgent.Storage;

namespace Aevatar.VibeResearching.Api.ReviewAgent.Api;

/// <summary>
/// ASP.NET Core minimal API endpoints for the Review Agent feature.
/// Provides status, settings, and iteration history endpoints.
/// </summary>
public static class ReviewAgentApi
{
    /// <summary>
    /// Maps all Review Agent API routes.
    /// </summary>
    public static RouteGroupBuilder MapReviewAgentApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/review-agent")
            .WithTags("Review Agent")
            .WithOpenApi();

        // GET /api/review-agent/status - Get current status
        group.MapGet("/status", GetStatus)
            .WithName("GetReviewAgentStatus")
            .WithSummary("Get the current status of the Review Agent")
            .WithDescription("Returns the current state including status, counters, timestamps, and error information if any.")
            .Produces<ReviewAgentStatusResponse>(StatusCodes.Status200OK);

        // GET /api/review-agent/settings - Get current settings
        group.MapGet("/settings", GetSettings)
            .WithName("GetReviewAgentSettings")
            .WithSummary("Get the current Review Agent settings")
            .WithDescription("Returns the current configuration values for the Review Agent.")
            .Produces<ReviewAgentOptionsResponse>(StatusCodes.Status200OK);

        // PUT /api/review-agent/settings - Update settings
        group.MapPut("/settings", UpdateSettings)
            .WithName("UpdateReviewAgentSettings")
            .WithSummary("Update Review Agent settings")
            .WithDescription("Updates the Review Agent configuration. Changes take effect on the next iteration.")
            .Accepts<ReviewAgentSettingsUpdate>("application/json")
            .Produces<ReviewAgentOptionsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        // GET /api/review-agent/iterations - List iterations
        group.MapGet("/iterations", GetIterations)
            .WithName("GetReviewAgentIterations")
            .WithSummary("Get review iteration history")
            .WithDescription("Returns a paginated list of past review iterations with summary information.")
            .Produces<IterationListResponse>(StatusCodes.Status200OK);

        // GET /api/review-agent/iterations/{iterationId} - Get specific iteration
        group.MapGet("/iterations/{iterationId}", GetIteration)
            .WithName("GetReviewAgentIteration")
            .WithSummary("Get details of a specific review iteration")
            .WithDescription("Returns full details of a review iteration including all log entries.")
            .Produces<ReviewIteration>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        // GET /api/review-agent/current-entries - Get entries for current iteration in progress
        group.MapGet("/current-entries", GetCurrentIterationEntries)
            .WithName("GetCurrentIterationEntries")
            .WithSummary("Get review log entries for the current iteration in progress")
            .WithDescription("Returns all review log entries collected so far in the current iteration. Empty if no iteration is in progress.")
            .Produces<CurrentIterationEntriesResponse>(StatusCodes.Status200OK);

        // GET /api/review-agent/events - SSE stream of real-time events
        group.MapGet("/events", StreamEvents)
            .WithName("StreamReviewAgentEvents")
            .WithSummary("Stream real-time Review Agent events")
            .WithDescription("Server-Sent Events (SSE) endpoint for real-time updates during review/cleanup rounds.")
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream");

        // GET /api/review-agent/graph - Get knowledge graph with review status
        group.MapGet("/graph", GetReviewGraph)
            .WithName("GetReviewGraph")
            .WithSummary("Get knowledge graph with review status")
            .WithDescription("Returns all knowledge nodes with their review status for graph visualization. Nodes are colored based on: reviewed (passed), pending (waiting), deactivated (failed), removed, or currently reviewing.")
            .Produces<ReviewGraphResponse>(StatusCodes.Status200OK);

        return group;
    }

    private static async Task StreamEvents(
        HttpContext context,
        IReviewAgentEventPublisher eventPublisher,
        CancellationToken ct)
    {
        context.Response.Headers.Append("Content-Type", "text/event-stream");
        context.Response.Headers.Append("Cache-Control", "no-cache");
        context.Response.Headers.Append("Connection", "keep-alive");

        var tcs = new TaskCompletionSource();
        using var registration = ct.Register(() => tcs.TrySetResult());

        // Cache JsonSerializerOptions to avoid re-creating on each event
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        using var subscription = eventPublisher.Subscribe(async evt =>
        {
            if (ct.IsCancellationRequested) return;

            try
            {
                // Use evt.GetType() to serialize the actual runtime type, not the base class.
                // Without this, System.Text.Json only serializes base class properties (Type, Timestamp),
                // and derived class properties (AgentId, Token, etc.) are lost.
                var json = JsonSerializer.Serialize(evt, evt.GetType(), jsonOptions);

                await context.Response.WriteAsync($"event: {evt.Type}\n", ct);
                await context.Response.WriteAsync($"data: {json}\n\n", ct);
                await context.Response.Body.FlushAsync(ct);
            }
            catch
            {
                // Client disconnected
                tcs.TrySetResult();
            }
        });

        // Keep connection open until cancelled
        await tcs.Task;
    }

    private static IResult GetCurrentIterationEntries(IReviewAgentService service)
    {
        var state = service.GetState();
        var response = new CurrentIterationEntriesResponse(
            IterationId: state.CurrentIterationId,
            Entries: state.CurrentIterationEntries.Select(e => new ReviewLogEntryResponse(
                EntryId: e.EntryId,
                NodeId: e.NodeId,
                NodeLabel: e.NodeLabel,
                ExplainContent: e.ExplainContent,
                Result: e.ReviewResult.ToString(),
                DeactivatedReason: e.DeactivatedReason,
                Timestamp: e.Timestamp.ToString("O"),
                VerificationContent: e.VerificationContent
            )).ToList()
        );
        return Results.Ok(response);
    }

    private static IResult GetReviewGraph(IReviewAgentService service)
    {
        var state = service.GetState();
        var graphData = service.GetGraphData();

        // Determine which node is currently being reviewed
        var currentlyReviewingNodeId = state.Status == ReviewAgentStatus.WorkingReviewRound
            ? state.CurrentIterationEntries.LastOrDefault()?.NodeId
            : null;

        // Get node IDs that have been reviewed in the current iteration
        var reviewedInCurrentIteration = new HashSet<string>(
            state.CurrentIterationEntries
                .Where(e => e.ReviewResult == ReviewResult.Passed)
                .Select(e => e.NodeId),
            StringComparer.Ordinal);

        // Get node IDs that failed in the current iteration
        var failedInCurrentIteration = new HashSet<string>(
            state.CurrentIterationEntries
                .Where(e => e.ReviewResult == ReviewResult.Failed)
                .Select(e => e.NodeId),
            StringComparer.Ordinal);

        var nodes = graphData.Nodes.Select(n =>
        {
            // Determine review status for this node
            string reviewStatus;
            if (n.NodeId == currentlyReviewingNodeId)
            {
                reviewStatus = "reviewing";
            }
            else if (!n.IsActivated && n.DeactivatedTimestamp != null)
            {
                reviewStatus = "deactivated";
            }
            else if (failedInCurrentIteration.Contains(n.NodeId))
            {
                reviewStatus = "deactivated";
            }
            else if (reviewedInCurrentIteration.Contains(n.NodeId))
            {
                reviewStatus = "validated";
            }
            else if (n.LastReviewedAt != null)
            {
                reviewStatus = "validated";
            }
            else
            {
                reviewStatus = "pending";
            }

            return new ReviewGraphNodeResponse(
                NodeId: n.NodeId,
                Label: n.Label,
                SessionId: n.SessionId,
                ReviewStatus: reviewStatus,
                LastReviewedAt: n.LastReviewedAt?.ToString("O"),
                DeactivatedAt: n.DeactivatedTimestamp?.ToString("O"),
                DeactivatedReason: n.DeactivatedReason,
                DependsOn: n.DependsOn
            );
        }).ToList();

        var edges = graphData.Edges.Select(e => new ReviewGraphEdgeResponse(
            Source: e.FromId,
            Target: e.ToId,
            Type: e.Type
        )).ToList();

        var response = new ReviewGraphResponse(
            Nodes: nodes,
            Edges: edges,
            TotalNodes: nodes.Count,
            ValidatedCount: nodes.Count(n => n.ReviewStatus == "validated"),
            PendingCount: nodes.Count(n => n.ReviewStatus == "pending"),
            DeactivatedCount: nodes.Count(n => n.ReviewStatus == "deactivated"),
            ReviewingCount: nodes.Count(n => n.ReviewStatus == "reviewing")
        );

        return Results.Ok(response);
    }

    private static IResult GetStatus(IReviewAgentService service)
    {
        var state = service.GetState();
        var response = new ReviewAgentStatusResponse(
            Status: state.Status.ToString(),
            CurrentIterationId: state.CurrentIterationId,
            LastCompletedAt: state.LastCompletedAt?.ToString("O"),
            NextScheduledAt: state.NextScheduledAt?.ToString("O"),
            NodesReviewed: state.NodesReviewed,
            NodesPending: state.NodesPending,
            NodesDeactivated: state.NodesDeactivated,
            NodesRemoved: state.NodesRemoved,
            ErrorMessage: state.ErrorMessage
        );

        return Results.Ok(response);
    }

    private static IResult GetSettings(IReviewAgentService service)
    {
        var options = service.GetSettings();
        var response = new ReviewAgentOptionsResponse(
            IterationIntervalMinutes: options.IterationIntervalMinutes,
            OutOfDateThresholdMinutes: options.OutOfDateThresholdMinutes,
            ToDeleteThresholdMinutes: options.ToDeleteThresholdMinutes,
            LLMProviderName: options.LLMProviderName,
            PerNodeTimeoutSeconds: options.PerNodeTimeoutSeconds
        );
        return Results.Ok(response);
    }

    private static async Task<IResult> UpdateSettings(
        ReviewAgentSettingsUpdate update,
        IReviewAgentService service,
        IReviewAgentStorage storage,
        CancellationToken ct)
    {
        // Validate input per data-model.md requirements
        var errors = new List<string>();

        if (update.IterationIntervalMinutes.HasValue && update.IterationIntervalMinutes.Value < 1)
        {
            errors.Add("IterationIntervalMinutes must be at least 1 minute");
        }

        if (update.OutOfDateThresholdMinutes.HasValue && update.OutOfDateThresholdMinutes.Value < 1)
        {
            errors.Add("OutOfDateThresholdMinutes must be at least 1 minute");
        }

        if (update.ToDeleteThresholdMinutes.HasValue && update.ToDeleteThresholdMinutes.Value < 60)
        {
            errors.Add("ToDeleteThresholdMinutes must be at least 60 minutes (1 hour)");
        }

        if (update.PerNodeTimeoutSeconds.HasValue && update.PerNodeTimeoutSeconds.Value < 10)
        {
            errors.Add("PerNodeTimeoutSeconds must be at least 10 seconds");
        }

        if (errors.Count > 0)
        {
            return Results.BadRequest(new { errors });
        }

        var current = service.GetSettings();

        var updated = new ReviewAgentOptions
        {
            IterationIntervalMinutes = update.IterationIntervalMinutes ?? current.IterationIntervalMinutes,
            OutOfDateThresholdMinutes = update.OutOfDateThresholdMinutes ?? current.OutOfDateThresholdMinutes,
            ToDeleteThresholdMinutes = update.ToDeleteThresholdMinutes ?? current.ToDeleteThresholdMinutes,
            LLMProviderName = update.LLMProviderName ?? current.LLMProviderName,
            PerNodeTimeoutSeconds = update.PerNodeTimeoutSeconds ?? current.PerNodeTimeoutSeconds
        };

        // Persist settings to storage
        await storage.SaveConfigAsync(updated, ct);

        // Update runtime settings
        var result = await service.UpdateSettingsAsync(updated);

        var response = new ReviewAgentOptionsResponse(
            IterationIntervalMinutes: result.IterationIntervalMinutes,
            OutOfDateThresholdMinutes: result.OutOfDateThresholdMinutes,
            ToDeleteThresholdMinutes: result.ToDeleteThresholdMinutes,
            LLMProviderName: result.LLMProviderName,
            PerNodeTimeoutSeconds: result.PerNodeTimeoutSeconds
        );

        return Results.Ok(response);
    }

    private static async Task<IResult> GetIterations(
        IReviewAgentStorage storage,
        int limit = 10,
        int offset = 0,
        CancellationToken ct = default)
    {
        var response = await storage.LoadIterationsAsync(limit, offset, ct);
        return Results.Ok(response);
    }

    private static async Task<IResult> GetIteration(
        string iterationId,
        IReviewAgentStorage storage,
        CancellationToken ct)
    {
        var iteration = await storage.LoadIterationAsync(iterationId, ct);
        if (iteration == null)
        {
            return Results.NotFound(new { error = $"Iteration '{iterationId}' not found" });
        }

        return Results.Ok(iteration);
    }
}

/// <summary>
/// Response model for the status endpoint.
/// </summary>
public sealed record ReviewAgentStatusResponse(
    string Status,
    string? CurrentIterationId,
    string? LastCompletedAt,
    string? NextScheduledAt,
    int NodesReviewed,
    int NodesPending,
    int NodesDeactivated,
    int NodesRemoved,
    string? ErrorMessage
);

/// <summary>
/// Response model for settings endpoints.
/// </summary>
public sealed record ReviewAgentOptionsResponse(
    int IterationIntervalMinutes,
    int OutOfDateThresholdMinutes,
    int ToDeleteThresholdMinutes,
    string LLMProviderName,
    int PerNodeTimeoutSeconds
);

/// <summary>
/// Request model for updating settings (all fields optional).
/// </summary>
public sealed record ReviewAgentSettingsUpdate(
    int? IterationIntervalMinutes = null,
    int? OutOfDateThresholdMinutes = null,
    int? ToDeleteThresholdMinutes = null,
    string? LLMProviderName = null,
    int? PerNodeTimeoutSeconds = null
);

/// <summary>
/// Response model for current iteration entries.
/// </summary>
public sealed record CurrentIterationEntriesResponse(
    string? IterationId,
    List<ReviewLogEntryResponse> Entries
);

/// <summary>
/// Response model for a single review log entry.
/// </summary>
public sealed record ReviewLogEntryResponse(
    string EntryId,
    string NodeId,
    string NodeLabel,
    string? ExplainContent,
    string Result,
    string? DeactivatedReason,
    string Timestamp,
    string? VerificationContent
);

/// <summary>
/// Response model for review graph (progress visualization).
/// </summary>
public sealed record ReviewGraphResponse(
    List<ReviewGraphNodeResponse> Nodes,
    List<ReviewGraphEdgeResponse> Edges,
    int TotalNodes,
    int ValidatedCount,
    int PendingCount,
    int DeactivatedCount,
    int ReviewingCount
);

/// <summary>
/// A node in the review graph with its review status.
/// </summary>
public sealed record ReviewGraphNodeResponse(
    string NodeId,
    string Label,
    string? SessionId,
    string ReviewStatus,
    string? LastReviewedAt,
    string? DeactivatedAt,
    string? DeactivatedReason,
    IReadOnlyList<string> DependsOn
);

/// <summary>
/// An edge in the review graph.
/// </summary>
public sealed record ReviewGraphEdgeResponse(
    string Source,
    string Target,
    string Type
);
