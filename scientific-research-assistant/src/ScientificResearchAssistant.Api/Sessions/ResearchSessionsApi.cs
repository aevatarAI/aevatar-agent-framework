using System.Text;
using System.Text.Json;
using Aevatar.Agents.AGUI;
using ScientificResearchAssistant.Api.Materials;

namespace ScientificResearchAssistant.Api.Sessions;

// ============================================================
//  Scientific Research Assistant Sessions API (AG-UI)
//
//  Endpoints:
//  - GET  /health
//  - GET  /api/info
//  - POST /api/sessions
//  - GET  /api/sessions
//  - POST /api/sessions/{id}/input
//  - GET  /api/sessions/{id}/agui/events   (SSE, snapshot-first)
// ============================================================

internal static class ResearchSessionsApi
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static void MapResearchSessionsApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        MapCreate(app);
        MapList(app);
        MapTools(app);
        MapInput(app);
        MapMcpReconnect(app);
        MapFacts(app);
        MapAgUiEvents(app);
    }

    private static void MapCreate(WebApplication app)
    {
        app.MapPost("/api/sessions", (CreateSessionInDto? input, ResearchSessionManager sessions) =>
        {
            var s = sessions.Create(input?.ProviderName);
            return Results.Json(new { ok = true, sessionId = s.Id });
        });
    }

    private static void MapList(WebApplication app)
    {
        app.MapGet("/api/sessions", (ResearchSessionManager sessions) =>
        {
            var list = sessions.ListSessions();
            return Results.Json(new { count = list.Count, sessions = list });
        });
    }

    private static void MapTools(WebApplication app)
    {
        app.MapGet("/api/sessions/{sessionId}/tools", async (
            string sessionId,
            ResearchSessionManager sessions,
            ResearchRuntime runtime,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            try
            {
                var (tools, _mcpNames) = await runtime.GetToolsSnapshotAsync(session.Id, session.ProviderName, ct);
                return Results.Json(new { ok = true, sessionId = session.Id, tools });
            }
            catch (Exception ex)
            {
                return Results.Problem(title: "tools snapshot failed", detail: ex.Message, statusCode: 500);
            }
        });
    }

    private static void MapInput(WebApplication app)
    {
        app.MapPost("/api/sessions/{sessionId}/input", async (
            string sessionId,
            SessionInputInDto input,
            ResearchSessionManager sessions,
            ResearchRunExecutor executor,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            if (string.IsNullOrWhiteSpace(input.Message))
                return Results.BadRequest(new { error = "message is required" });

            // Fire-and-forget run; clients receive progress via AG-UI SSE.
            var runSeq = session.NextRunSeq();
            var runId = $"{session.Id}:{runSeq}";

            _ = Task.Run(async () =>
            {
                await executor.ExecuteAsync(session, runId, input, CancellationToken.None);
            }, CancellationToken.None);

            return Results.Accepted($"/api/sessions/{session.Id}", new { ok = true, sessionId = session.Id, runId });
        });
    }

    private static void MapMcpReconnect(WebApplication app)
    {
        app.MapPost("/api/sessions/{sessionId}/mcp/reconnect", (
            string sessionId,
            ResearchSessionManager sessions,
            ResearchRuntime runtime) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            // Fire-and-forget; result is streamed back via SSE (CUSTOM events + tools_snapshot refresh).
            _ = Task.Run(async () =>
            {
                long Ts() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                session.Events.Publish(new CustomEvent
                {
                    Timestamp = Ts(),
                    Name = "aevatar.scientific.mcp_reconnect_started",
                    Value = new { sessionId = session.Id }
                });

                try
                {
                    var (agent, _) = await runtime.GetAgentAsync(session.Id, session.ProviderName, CancellationToken.None);
                    var (ok, error) = await agent.ReconnectMcpAsync(CancellationToken.None);

                    // Always refresh the tools snapshot so UI updates immediately (even if reconnect failed).
                    var (tools, _mcpNames) = await runtime.RefreshToolsSnapshotAsync(session.Id, session.ProviderName, CancellationToken.None);
                    session.Events.Publish(new CustomEvent
                    {
                        Timestamp = Ts(),
                        Name = "aevatar.scientific.tools_snapshot",
                        Value = new { sessionId = session.Id, tools }
                    });

                    if (ok)
                    {
                        session.Events.Publish(new CustomEvent
                        {
                            Timestamp = Ts(),
                            Name = "aevatar.scientific.mcp_reconnect_finished",
                            Value = new { sessionId = session.Id, ok = true }
                        });
                    }
                    else
                    {
                        session.Events.Publish(new CustomEvent
                        {
                            Timestamp = Ts(),
                            Name = "aevatar.scientific.mcp_reconnect_error",
                            Value = new { sessionId = session.Id, ok = false, error = error ?? "mcp reconnect failed" }
                        });
                    }
                }
                catch (Exception ex)
                {
                    session.Events.Publish(new CustomEvent
                    {
                        Timestamp = Ts(),
                        Name = "aevatar.scientific.mcp_reconnect_error",
                        Value = new { sessionId = session.Id, ok = false, error = ex.Message }
                    });
                }
            }, CancellationToken.None);

            return Results.Accepted($"/api/sessions/{session.Id}", new { ok = true });
        });
    }

    private static void MapFacts(WebApplication app)
    {
        // Write-back: allow user to persist a verified conclusion as a new fact under facts/
        app.MapPost("/api/sessions/{sessionId}/facts", async (
            string sessionId,
            SaveFactInDto input,
            ResearchSessionManager sessions,
            MaterialsService materials,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            var content = (input.Content ?? string.Empty).Trim();
            if (content.Length == 0)
                return Results.BadRequest(new { error = "content is required" });

            try
            {
                var title = (input.Title ?? string.Empty).Trim();
                var saved = await materials.SaveFactAsync(title, content, input.RelativePath, ct);

                // Refresh workspace materials snapshot (best-effort; bounded by options)
                var snapshot = await materials.LoadAsync(session.Id, query: "", ct);
                ApplyMaterialsToWorkspace(session, snapshot);

                session.Events.Publish(new StateSnapshotEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Snapshot = session.Workspace
                });

                session.Events.Publish(new CustomEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Name = "aevatar.scientific.fact_saved",
                    Value = new { sessionId = session.Id, id = saved.Id, title = saved.Title, relativePath = saved.RelativePath }
                });

                return Results.Json(new
                {
                    ok = true,
                    sessionId = session.Id,
                    fact = new { id = saved.Id, title = saved.Title, relativePath = saved.RelativePath }
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(title: "save material failed", detail: ex.Message, statusCode: 500);
            }
        });
    }

    private static void MapAgUiEvents(WebApplication app)
    {
        app.MapGet("/api/sessions/{sessionId}/agui/events", async (
            HttpContext http,
            string sessionId,
            ResearchSessionManager sessions,
            ResearchRuntime runtime,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
            {
                http.Response.StatusCode = StatusCodes.Status404NotFound;
                await http.Response.WriteAsJsonAsync(new { error = "session not found" }, cancellationToken: ct);
                return;
            }

            http.Response.StatusCode = StatusCodes.Status200OK;
            http.Response.Headers.ContentType = "text/event-stream; charset=utf-8";
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers.Pragma = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";

            await http.Response.StartAsync(ct);

            await using var writer = new StreamWriter(
                http.Response.Body,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 16 * 1024,
                leaveOpen: true);

            async Task WriteSseAsync(AgUiEvent evt, CancellationToken token)
            {
                // IMPORTANT:
                // System.Text.Json does not polymorphically serialize derived members when using the generic overload.
                // Here `evt` is typed as AgUiEvent, so Serialize(evt, options) would only output base fields
                // (type/timestamp/rawEvent) and drop derived fields like messageId/delta/name/value.
                // Use runtime type to keep AG-UI protocol payloads intact.
                var line = JsonSerializer.Serialize((object)evt, evt.GetType(), Json);
                await writer.WriteAsync("data: ");
                await writer.WriteLineAsync(line);
                await writer.WriteLineAsync();
                await writer.FlushAsync();
            }

            long Ts(DateTimeOffset? ts) => (ts ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds();

            // ------------------------------------------------------------
            //  0) Fast bootstrap (NEVER block SSE on agent/tool init)
            //
            //  WHY:
            //  - Agent initialization may involve MCP/tool bootstrap and can be slow or flaky.
            //  - If we await it here, frontend sees an "empty" chat for a long time.
            //  - So we send a minimal snapshot immediately, then hydrate in background.
            // ------------------------------------------------------------
            await WriteSseAsync(new MessagesSnapshotEvent
            {
                Timestamp = Ts(DateTimeOffset.UtcNow),
                Messages = session.GetMessagesSnapshot(maxMessages: 60)
            }, ct);

            await WriteSseAsync(new CustomEvent
            {
                Timestamp = Ts(DateTimeOffset.UtcNow),
                Name = "aevatar.scientific.session",
                Value = new
                {
                    sessionId = session.Id,
                    createdAt = session.CreatedAt.ToString("O"),
                    providerName = session.ProviderName ?? ""
                }
            }, ct);

            // Optional visual state (workspace / materials / graph)
            await WriteSseAsync(new StateSnapshotEvent
                    {
                        Timestamp = Ts(DateTimeOffset.UtcNow),
                Snapshot = session.Workspace
            }, ct);

            // 1) Background hydration (tools catalog) -> publish into the same hub.
            _ = Task.Run(async () =>
            {
                try
                {
                    var (tools, _mcpNames) = await runtime.GetToolsSnapshotAsync(session.Id, session.ProviderName, ct);
                    session.Events.Publish(new CustomEvent
                    {
                        Timestamp = Ts(DateTimeOffset.UtcNow),
                        Name = "aevatar.scientific.tools_snapshot",
                        Value = new { sessionId = session.Id, tools }
                    });
                }
                catch
                {
                    // best-effort
                }
            }, ct);

            // 2) Live stream (no replay; snapshot already hydrated UI)
            await foreach (var evt in session.Events.SubscribeAsync(replay: false, ct: ct))
            {
                ct.ThrowIfCancellationRequested();
                await WriteSseAsync(evt, ct);
            }
        });
    }

    private static void ApplyMaterialsToWorkspace(ResearchSession session, MaterialsSnapshot snapshot)
    {
        var ws = session.Workspace;
        ws.Materials.RootDir = "";
        ws.Materials.LoadedAt = snapshot.LoadedAt.ToString("O");
        ws.Materials.Items = new List<MaterialMeta>(capacity: snapshot.Facts.Count + snapshot.Sources.Count);

        foreach (var x in snapshot.Facts)
        {
            ws.Materials.Items.Add(new MaterialMeta
            {
                Id = x.Id,
                Title = x.Title,
                RelativePath = x.RelativePath,
                Kind = x.Kind
            });
        }

        foreach (var x in snapshot.Sources)
        {
            ws.Materials.Items.Add(new MaterialMeta
            {
                Id = x.Id,
                Title = x.Title,
                RelativePath = x.RelativePath,
                Kind = x.Kind
            });
        }

        ws.Materials.ContextPreview = Trunc(snapshot.RenderedContext, 2000);
    }

    private static string Trunc(string? s, int maxChars)
    {
        var t = (s ?? string.Empty).Replace("\r", "").Trim();
        if (t.Length <= maxChars) return t;
        return t[..maxChars];
    }
}


