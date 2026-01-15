using System.Text.Json;
using Microsoft.Extensions.Options;
using VibeResearching.Vibe.Pivot;
using VibeResearching.Vibe.Pivot.Messages;
using VibeResearching.Vibe.Pivot.Models;

namespace VibeResearching.Api.Vibe.Pivot;

// ============================================================
//  Research Direction Pivot API (US-5 Rollback)
//
//  Endpoints:
//  - POST /api/sessions/{sessionId}/pivot/{pivotId}/rollback
//  - POST /api/sessions/{sessionId}/pivot/rollback
//  - GET  /api/sessions/{sessionId}/pivot/snapshots
// ============================================================

internal static class PivotApi
{
    public static void MapPivotApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        MapRollback(app);
        MapRollbackMostRecent(app);
        MapSnapshots(app);
    }

    /// <summary>
    /// POST /api/sessions/{sessionId}/pivot/{pivotId}/rollback
    /// Rolls back to a specific pivot snapshot.
    /// </summary>
    private static void MapRollback(WebApplication app)
    {
        app.MapPost("/api/sessions/{sessionId}/pivot/{pivotId}/rollback",
            async (
                string sessionId,
                string pivotId,
                IPivotSnapshotManager snapshotManager,
                IOptions<PivotOptions> options,
                HttpContext context) =>
            {
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    return Results.BadRequest(new { error = "sessionId is required" });
                }

                if (string.IsNullOrWhiteSpace(pivotId))
                {
                    return Results.BadRequest(new { error = "pivotId is required" });
                }

                // Parse optional body for preserveNewCompleted option
                var preserveNewCompleted = await ParsePreserveNewCompletedAsync(context);

                var request = new RollbackRequest
                {
                    SessionId = sessionId,
                    PivotId = pivotId,
                    PreserveNewCompleted = preserveNewCompleted
                };

                var response = await snapshotManager.ExecuteRollbackAsync(request);

                if (!response.Success)
                {
                    return Results.BadRequest(new
                    {
                        success = false,
                        sessionId,
                        pivotId,
                        error = response.ErrorMessage ?? PivotMessages.RollbackExpired_EN
                    });
                }

                return Results.Ok(new
                {
                    success = true,
                    sessionId,
                    pivotId,
                    restoredNodeCount = response.RestoredNodeCount,
                    preservedNewNodeCount = response.PreservedNewNodeCount,
                    message = $"Rolled back to snapshot {pivotId}, restored {response.RestoredNodeCount} nodes"
                });
            })
            .WithName("RollbackPivot")
            .WithTags("Pivot");
    }

    /// <summary>
    /// POST /api/sessions/{sessionId}/pivot/rollback
    /// Rolls back to the most recent pivot snapshot (when pivotId is not specified).
    /// </summary>
    private static void MapRollbackMostRecent(WebApplication app)
    {
        app.MapPost("/api/sessions/{sessionId}/pivot/rollback",
            async (
                string sessionId,
                IPivotSnapshotManager snapshotManager,
                IOptions<PivotOptions> options,
                HttpContext context) =>
            {
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    return Results.BadRequest(new { error = "sessionId is required" });
                }

                // Find most recent snapshot
                var mostRecent = snapshotManager.GetMostRecentSnapshot(sessionId);
                if (mostRecent == null)
                {
                    return Results.NotFound(new
                    {
                        success = false,
                        sessionId,
                        error = "No rollback snapshot available"
                    });
                }

                // Parse optional body for preserveNewCompleted option
                var preserveNewCompleted = await ParsePreserveNewCompletedAsync(context);

                var request = new RollbackRequest
                {
                    SessionId = sessionId,
                    PivotId = mostRecent.PivotId,
                    PreserveNewCompleted = preserveNewCompleted
                };

                var response = await snapshotManager.ExecuteRollbackAsync(request);

                if (!response.Success)
                {
                    return Results.BadRequest(new
                    {
                        success = false,
                        sessionId,
                        pivotId = mostRecent.PivotId,
                        error = response.ErrorMessage ?? PivotMessages.RollbackExpired_EN
                    });
                }

                return Results.Ok(new
                {
                    success = true,
                    sessionId,
                    pivotId = mostRecent.PivotId,
                    restoredNodeCount = response.RestoredNodeCount,
                    preservedNewNodeCount = response.PreservedNewNodeCount,
                    message = $"Rolled back to most recent snapshot {mostRecent.PivotId}, restored {response.RestoredNodeCount} nodes"
                });
            })
            .WithName("RollbackToMostRecentPivot")
            .WithTags("Pivot");
    }

    /// <summary>
    /// GET /api/sessions/{sessionId}/pivot/snapshots
    /// Lists available rollback snapshots for a session.
    /// </summary>
    private static void MapSnapshots(WebApplication app)
    {
        app.MapGet("/api/sessions/{sessionId}/pivot/snapshots",
            (string sessionId, IPivotSnapshotManager snapshotManager, IOptions<PivotOptions> options) =>
            {
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    return Results.BadRequest(new { error = "sessionId is required" });
                }

                var mostRecent = snapshotManager.GetMostRecentSnapshot(sessionId);

                if (mostRecent == null)
                {
                    return Results.Ok(new
                    {
                        sessionId,
                        snapshots = Array.Empty<object>(),
                        rollbackWindowMinutes = options.Value.RollbackWindowMinutes
                    });
                }

                // Return basic info about available snapshots
                return Results.Ok(new
                {
                    sessionId,
                    snapshots = new[]
                    {
                        new
                        {
                            pivotId = mostRecent.PivotId,
                            snapshotId = mostRecent.SnapshotId,
                            directionSummary = mostRecent.DirectionSummary,
                            nodeCount = mostRecent.Snapshot.NodeCount,
                            createdAt = mostRecent.CreatedAt,
                            expiresAt = mostRecent.ExpiresAt,
                            isValid = mostRecent.IsValid
                        }
                    },
                    rollbackWindowMinutes = options.Value.RollbackWindowMinutes
                });
            })
            .WithName("GetPivotSnapshots")
            .WithTags("Pivot");
    }

    private static async Task<bool> ParsePreserveNewCompletedAsync(HttpContext context)
    {
        if (context.Request.ContentLength is null or 0)
        {
            return false;
        }

        try
        {
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(body))
            {
                return false;
            }

            var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("preserveNewCompleted", out var prop))
            {
                return prop.GetBoolean();
            }
        }
        catch (JsonException)
        {
            // Ignore invalid JSON - use defaults
        }

        return false;
    }
}
