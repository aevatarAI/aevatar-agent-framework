using System.Linq;
using Aevatar.Agents.AGUI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using VibeResearching.Api.Vibe.Mesh;
using VibeResearching.Api.Workspace;

namespace VibeResearching.Api.Sessions;

internal static partial class ResearchSessionsApi
{
    private static void MapMesh(WebApplication app)
    {
        // Mesh DSL API (local-only; session-scoped File-SSoT)
        app.MapGet("/api/sessions/{sessionId}/mesh", async (
            string sessionId,
            ResearchSessionManager sessions,
            MeshDefinitionStore store,
            CancellationToken ct,
            HttpContext http) =>
        {
            if (!IsLocal(http))
                return Results.Forbid();

            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { ok = false, error = "session not found" });

            var (raw, format) = await store.TryLoadRawAsync(session.Id, ct);
            return Results.Json(new
            {
                ok = true,
                sessionId = session.Id,
                exists = !string.IsNullOrWhiteSpace(raw),
                format = format ?? "",
                raw = raw
            });
        });

        app.MapPut("/api/sessions/{sessionId}/mesh", async (
            string sessionId,
            UpdateMeshInDto input,
            ResearchSessionManager sessions,
            MeshDefinitionStore store,
            MeshCompilerService compiler,
            CancellationToken ct,
            HttpContext http) =>
        {
            if (!IsLocal(http))
                return Results.Forbid();

            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { ok = false, error = "session not found" });

            var raw = (input.Raw ?? string.Empty).Replace("\r", "").Trim();
            if (raw.Length == 0)
                return Results.BadRequest(new { ok = false, error = "raw is required" });

            // Bound payload to prevent abuse.
            const int maxChars = 500_000;
            if (raw.Length > maxChars)
                raw = raw[..maxChars];

            // Validate first (do not execute).
            var result = compiler.Compile(raw);
            if (!result.Ok || result.Definition == null)
            {
                var errors = (result.Errors ?? [])
                    .Take(50)
                    .Select(e => new { code = e.Code, message = e.Message, path = e.Path })
                    .ToList();

                return Results.BadRequest(new
                {
                    ok = false,
                    sessionId = session.Id,
                    errors
                });
            }

            await store.SaveAsync(session.Id, raw, input.Format, ct);

            // Best-effort: surface compile ok to UI via custom event.
            try
            {
                session.Events.Publish(new CustomEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Name = "aevatar.vibe.mesh_saved",
                    Value = new { sessionId = session.Id }
                });
            }
            catch
            {
                // best-effort only
            }

            return Results.Json(new
            {
                ok = true,
                sessionId = session.Id
            }, Json);
        });
    }

    private sealed record UpdateMeshInDto(string Raw, string? Format);

    private static void MapFiles(WebApplication app)
    {
        // File manager API (local-only; safe within workspace/sessions/{id})
        app.MapGet("/api/sessions/{sessionId}/files/tree", (
            string sessionId,
            string? dir,
            int? depth,
            ResearchSessionManager sessions,
            SessionFilesService files,
            HttpContext http) =>
        {
            if (!IsLocal(http))
                return Results.Forbid();

            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            try
            {
                var tree = files.ListTree(session.Id, dir, maxDepth: depth ?? 6);
                return Results.Json(new { ok = true, sessionId = session.Id, tree });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { ok = false, error = ex.Message });
            }
        });

        app.MapGet("/api/sessions/{sessionId}/files", async (
            string sessionId,
            string path,
            ResearchSessionManager sessions,
            SessionFilesService files,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!IsLocal(http))
                return Results.Forbid();

            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            try
            {
                var res = await files.ReadTextAsync(session.Id, path, ct);
                return Results.Json(new { ok = true, sessionId = session.Id, file = res });
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound(new { ok = false, error = "file not found" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { ok = false, error = ex.Message });
            }
        });

        app.MapPut("/api/sessions/{sessionId}/files", async (
            string sessionId,
            SaveFileInDto input,
            ResearchSessionManager sessions,
            SessionFilesService files,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!IsLocal(http))
                return Results.Forbid();

            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            var path = (input.Path ?? string.Empty).Trim();
            if (path.Length == 0)
                return Results.BadRequest(new { ok = false, error = "path is required" });

            try
            {
                var wr = await files.WriteTextAsync(session.Id, path, input.Content ?? string.Empty, ct);
                return Results.Json(new { ok = true, sessionId = session.Id, file = wr });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { ok = false, error = ex.Message });
            }
        });
    }

    private static bool IsLocal(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress;
        return ip == null || System.Net.IPAddress.IsLoopback(ip);
    }
}
