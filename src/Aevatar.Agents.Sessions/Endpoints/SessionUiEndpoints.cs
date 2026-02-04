using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.Sessions.Bootstrap;
using Aevatar.Agents.Sessions.Runtime;
using Aevatar.Agents.Tooling.Catalog;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.Sessions.Endpoints;

public static class SessionUiEndpoints
{
    public static void MapSessionUiEndpoints(this WebApplication app)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        app.MapGet("/api/chat/workflows", (CognitiveSessionService sessions, IOptions<SessionRuntimeOptions> options) =>
        {
            var list = sessions.ListWorkflows();
            var workflows = list.Workflows
                .Select(w => new { name = w.Name, version = w.Version, description = w.Description })
                .ToList();

            var fallback = (options.Value.WorkflowName ?? string.Empty).Trim();
            return Results.Ok(new { workflows, defaultWorkflow = fallback });
        });

        app.MapGet("/api/chat/sessions/new", async (
            string? workflow,
            SessionRuntime runtime,
            CancellationToken ct) =>
        {
            var state = await runtime.CreateSessionAsync(workflow, ct);
            var primary = await runtime.ResolvePrimaryAgentAsync(state.SessionId, ct);
            return Results.Ok(new
            {
                sessionId = state.SessionId,
                workflowName = state.WorkflowName,
                primaryRole = primary?.Role ?? string.Empty,
                primaryAgentId = primary?.AgentId ?? string.Empty
            });
        });

        app.MapGet("/api/chat/sessions", async (
            HttpRequest request,
            SessionRuntime runtime,
            CancellationToken ct) =>
        {
            var limit = ReadInt(request, "limit", 200, 1, 2000);
            var list = await runtime.ListSessionsAsync(limit, ct);
            var sessions = list.Select(s => new
                {
                    sessionId = s.SessionId,
                    workflowName = s.WorkflowName,
                    primaryRole = s.PrimaryRole,
                    createdAt = s.CreatedAt.ToUniversalTime().ToString("O"),
                    updatedAt = s.UpdatedAt.ToUniversalTime().ToString("O")
                })
                .ToList();
            return Results.Ok(new { sessions });
        });

        app.MapPost("/api/chat/sessions/{sessionId}/input", async (
            string sessionId,
            HttpRequest req,
            SessionRuntime runtime,
            CancellationToken ct) =>
        {
            var input = await req.ReadFromJsonAsync<ChatRequestInput>(cancellationToken: ct);
            if (input == null || string.IsNullOrWhiteSpace(input.Message))
                return Results.BadRequest(new { error = "message is required" });

            var request = BuildChatRequest(input);
            var runId = await runtime.SendChatAsync(sessionId, request, ct);
            return Results.Ok(new { runId });
        });

        app.MapGet("/api/chat/sessions/{sessionId}/state/history", async (
            string sessionId,
            SessionRuntime runtime,
            IGAgentActorManager actorManager,
            IOptions<SessionRuntimeOptions> options,
            CancellationToken ct) =>
        {
            var agent = await runtime.GetPrimaryAgentAsync(sessionId, ct);
            if (agent == null)
                return Results.NotFound();

            var actor = await actorManager.GetActorAsync(agent.Id);
            if (actor == null)
                return Results.NotFound();

            var state = await actor.InvokeAsync<AevatarAIAgentState>("GetState");
            var history = TrimHistory(state.History, options.Value.MaxSnapshotMessages);

            var list = history.Select(msg => new
                {
                    id = msg.Id,
                    role = msg.Role.ToString().ToLowerInvariant(),
                    content = msg.Content ?? string.Empty,
                    timestamp = msg.Timestamp == null
                        ? null
                        : msg.Timestamp.ToDateTime().ToUniversalTime().ToString("O")
                })
                .ToList();

            return Results.Ok(new { history = list });
        });

        // ============================================================
        //  SSE 端点 - 极简版
        //  直接流式转发 agent 事件到前端
        // ============================================================
        app.MapGet("/api/chat/sessions/{sessionId}/agui/events", async (
            HttpContext http,
            string sessionId,
            SessionRuntime runtime,
            CancellationToken ct) =>
        {
            var stream = await runtime.GetSessionStreamAsync(sessionId, ct);
            if (stream == null)
                return Results.NotFound();

            http.Response.StatusCode = StatusCodes.Status200OK;
            http.Response.Headers.ContentType = "text/event-stream; charset=utf-8";
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers.Pragma = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";

            await http.Response.StartAsync(ct);
            await using var writer = new AgUiSseWriter(http.Response, jsonOptions);

            // 发送连接确认事件（让前端知道 SSE 已就绪）
            await writer.WriteAsync(new CustomEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = "SSE_CONNECTED",
                Value = new { sessionId }
            }, ct);

            try
            {
                await foreach (var evt in stream.SubscribeAsync(ct))
                {
                    ct.ThrowIfCancellationRequested();
                    await writer.WriteAsync(evt, ct);
                }
            }
            finally
            {
                await runtime.CleanupIdleContextsAsync(ct: CancellationToken.None);
            }

            return Results.Empty;
        });

        app.MapGet("/api/agent/handlers", async (
            string sessionId,
            SessionRuntime runtime,
            CancellationToken ct) =>
        {
            var agent = await runtime.GetPrimaryAgentAsync(sessionId, ct);
            if (agent == null)
                return Results.NotFound();

            var handlers = DescribeHandlers(agent);
            var modules = agent is RoleAIGAgent roleAgent
                ? roleAgent.GetEventModules()
                    .Select(m => (object)new { name = m.Name, priority = m.Priority })
                    .ToList()
                : new List<object>();

            return Results.Ok(new
            {
                agentId = agent.Id,
                agentType = agent.GetType().Name,
                mode = "workflow",
                role = agent is RoleAIGAgent role ? role.Role : string.Empty,
                handlers,
                modules
            });
        });

        app.MapGet("/api/agent/state", async (
            string sessionId,
            SessionRuntime runtime,
            IGAgentActorManager actorManager,
            CancellationToken ct) =>
        {
            var agent = await runtime.GetPrimaryAgentAsync(sessionId, ct);
            if (agent == null)
                return Results.NotFound();

            var actor = await actorManager.GetActorAsync(agent.Id);
            if (actor == null)
                return Results.NotFound();

            var state = await actor.InvokeAsync<AevatarAIAgentState>("GetState");
            state.Context.TryGetValue("history_summary", out var summary);
            return Results.Ok(new
            {
                agentId = agent.Id,
                agentType = agent.GetType().Name,
                mode = "workflow",
                role = agent is RoleAIGAgent roleAgent ? roleAgent.Role : string.Empty,
                historyCount = state.History?.Count ?? 0,
                historySummary = summary ?? string.Empty,
                contextKeys = state.Context.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList()
            });
        });

        app.MapGet("/api/agent/memory", async (
            string sessionId,
            SessionRuntime runtime,
            CognitiveSessionService sessions,
            CancellationToken ct) =>
        {
            var primary = await runtime.ResolvePrimaryAgentAsync(sessionId, ct);
            if (primary == null)
                return Results.NotFound();

            var agentMemoryId = $"privateagent::{primary.AgentId}";
            var sessionMemoryId = $"session::{sessionId}";

            try
            {
                var agentEntries = await sessions.GetAgentMemoryEntriesAsync(sessionId, primary.AgentId, 50, ct);
                var sessionEntries = await sessions.GetSessionMemoryEntriesAsync(sessionId, 50, ct);

                return Results.Ok(new
                {
                    agentMemoryId,
                    sessionMemoryId,
                    agentEntries = agentEntries?.Entries.Select(MapEntry).ToList() ?? [],
                    sessionEntries = sessionEntries?.Entries.Select(MapEntry).ToList() ?? []
                });
            }
            catch (Exception ex)
            {
                return Results.Ok(new
                {
                    agentMemoryId,
                    sessionMemoryId,
                    error = ex.Message,
                    agentEntries = Array.Empty<object>(),
                    sessionEntries = Array.Empty<object>()
                });
            }
        });

        app.MapPost("/api/agent/ping", async (
            HttpRequest req,
            SessionRuntime runtime,
            CancellationToken ct) =>
        {
            var input = await req.ReadFromJsonAsync<PingInput>(cancellationToken: ct);
            if (string.IsNullOrWhiteSpace(input?.SessionId))
                return Results.BadRequest(new { error = "sessionId is required" });

            var stream = await runtime.GetSessionStreamAsync(input.SessionId, ct);
            if (stream == null)
                return Results.NotFound();

            var requestId = $"{input.SessionId}:ping:{Guid.NewGuid():N}";
            stream.Publish(new CustomEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = "SESSION_PONG",
                Value = new { requestId, content = input.Content ?? "ping" }
            });

            return Results.Ok(new { requestId });
        });

        app.MapPost("/api/agent/settings", async (
            HttpRequest req,
            SessionRuntime runtime,
            CancellationToken ct) =>
        {
            var input = await req.ReadFromJsonAsync<AgentSettingsInput>(cancellationToken: ct);
            if (string.IsNullOrWhiteSpace(input?.SessionId))
                return Results.BadRequest(new { error = "sessionId is required" });

            var agent = await runtime.GetPrimaryAgentAsync(input.SessionId, ct);
            if (agent == null)
                return Results.NotFound();

            if (input.EnableHistory.HasValue)
                agent.EnableChatHistoryInState = input.EnableHistory.Value;
            if (input.EnableCompaction.HasValue)
                agent.EnableChatHistoryCompaction = input.EnableCompaction.Value;
            if (input.EnableMemoryStore.HasValue)
                agent.EnableMemoryStoreAppend = input.EnableMemoryStore.Value;
            if (input.EnableSessionMemory.HasValue)
                agent.EnableSessionMemoryStoreAppend = input.EnableSessionMemory.Value;
            if (input.EnableVectorIndex.HasValue)
                agent.EnableMemoryVectorIndexAppend = input.EnableVectorIndex.Value;
            if (input.EnableMcp.HasValue)
                agent.EnableMcpServers = input.EnableMcp.Value;
            if (input.EnableSkills.HasValue)
                agent.EnableAgentSkills = input.EnableSkills.Value;
            if (input.AllowDangerousTools.HasValue)
                agent.AllowDangerousTools = input.AllowDangerousTools.Value;
            if (input.AllowInternalTools.HasValue)
                agent.AllowInternalTools = input.AllowInternalTools.Value;

            return Results.Ok(new
            {
                enableHistory = agent.EnableChatHistoryInState,
                enableCompaction = agent.EnableChatHistoryCompaction,
                enableMemoryStore = agent.EnableMemoryStoreAppend,
                enableSessionMemory = agent.EnableSessionMemoryStoreAppend,
                enableVectorIndex = agent.EnableMemoryVectorIndexAppend,
                enableMcp = agent.EnableMcpServers,
                enableSkills = agent.EnableAgentSkills,
                allowDangerousTools = agent.AllowDangerousTools,
                allowInternalTools = agent.AllowInternalTools
            });
        });

        app.MapGet("/api/tools/catalog", async (
            string sessionId,
            SessionRuntime runtime,
            AgentToolCatalog catalog,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return Results.BadRequest(new { error = "sessionId is required" });

            var agent = await runtime.GetPrimaryAgentAsync(sessionId, ct);
            if (agent == null)
                return Results.NotFound();

            var tools = await catalog.GetToolsAsync(agent, ct);
            return Results.Ok(new { sessionId, tools });
        });

        app.MapGet("/api/tools/dotnet", async (
            AgentToolCatalog catalog,
            CancellationToken ct) =>
        {
            var tools = await catalog.ListDotNetFilesAsync(ct);
            return Results.Ok(new { tools });
        });

        app.MapPost("/api/tools/dotnet/register", async (
            HttpRequest req,
            SessionRuntime runtime,
            AgentToolCatalog catalog,
            CancellationToken ct) =>
        {
            var input = await req.ReadFromJsonAsync<RegisterDotNetToolInput>(cancellationToken: ct);
            if (string.IsNullOrWhiteSpace(input?.SessionId) || string.IsNullOrWhiteSpace(input.FilePath))
                return Results.BadRequest(new { error = "sessionId and filePath are required" });

            var agent = await runtime.GetPrimaryAgentAsync(input.SessionId, ct);
            if (agent == null)
                return Results.NotFound();

            var tool = await catalog.RegisterDotNetFileAsync(agent, input.FilePath, ct);
            if (tool == null)
                return Results.BadRequest(new { error = "dotnet tool file not allowed or invalid" });

            return Results.Ok(new { tool });
        });

        app.MapPost("/api/agent/yaml", async (
            HttpRequest req,
            IWorkflowCatalog workflowCatalog,
            SessionRuntime runtime,
            CancellationToken ct) =>
        {
            var input = await req.ReadFromJsonAsync<AgentYamlInput>(cancellationToken: ct);
            var yaml = (input?.Yaml ?? string.Empty).Trim();
            if (yaml.Length == 0)
                return Results.BadRequest(new { error = "yaml is required" });

            var loader = new AgentYamlConfigLoader();
            AgentYamlConfig cfg;
            try
            {
                cfg = loader.LoadFromString(yaml);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            var role = GlobalAgentYamlRegistry.NormalizeRoleKey(cfg.Id);
            if (string.IsNullOrWhiteSpace(role))
                return Results.BadRequest(new { error = "invalid role id" });

            var dir = AgentYamlConfigLoader.GetDefaultConfigDirectory();
            Directory.CreateDirectory(dir);
            var path = AgentYamlConfigLoader.GetConfigFilePath(role);
            await File.WriteAllTextAsync(path, yaml, ct);

            if (input?.CreateSession == true)
            {
                var workflowName = workflowCatalog.EnsureRoleWorkflowYaml(role);
                var state = await runtime.CreateSessionAsync(workflowName, ct);
                return Results.Ok(new { role, path, sessionId = state.SessionId });
            }

            return Results.Ok(new { role, path });
        });

        app.MapPost("/api/workflow/yaml", async (
            HttpRequest req,
            WorkflowMeshService mesh,
            CancellationToken ct) =>
        {
            var input = await req.ReadFromJsonAsync<WorkflowYamlInput>(cancellationToken: ct);
            var yaml = (input?.Yaml ?? string.Empty).Trim();
            if (yaml.Length == 0)
                return Results.BadRequest(new { error = "yaml is required" });

            var workflowName = (input?.Name ?? "workspace_mesh").Trim();
            var dir = SessionWorkflowYamlBootstrap.GetWorkflowDirectory();
            Directory.CreateDirectory(dir);
            var path = SessionWorkflowYamlBootstrap.GetWorkflowPath(workflowName);
            await File.WriteAllTextAsync(path, yaml, ct);

            var result = await mesh.LoadFromYamlAsync(yaml, ct);
            if (!result.Ok)
            {
                return Results.Ok(new { ok = false, errors = result.Errors.Select(e => new { e.Code, e.Message, e.Path }) });
            }

            return Results.Ok(new
            {
                ok = true,
                workflowId = result.Graph?.WorkflowId ?? workflowName,
                graph = result.Graph,
                agents = result.Agents
            });
        });

        app.MapGet("/api/workflow/graph", (WorkflowMeshService mesh) =>
        {
            return Results.Ok(new { graph = mesh.GetLatestGraph() });
        });
    }

    private static List<object> DescribeHandlers(IGAgent agent)
    {
        var list = new List<object>();
        var methods = agent.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var method in methods)
        {
            var eventAttr = method.GetCustomAttribute<EventHandlerAttribute>();
            var allAttr = method.GetCustomAttribute<AllEventHandlerAttribute>();
            if (eventAttr == null && allAttr == null)
                continue;

            var visibility = method.IsPublic
                ? "public"
                : method.IsFamily
                    ? "protected"
                    : method.IsPrivate
                        ? "private"
                        : "internal";

            var parameter = method.GetParameters().FirstOrDefault();
            var paramType = parameter?.ParameterType.FullName ?? "(none)";
            list.Add(new
            {
                name = method.Name,
                visibility,
                parameterType = paramType,
                handlerType = allAttr != null ? "all" : "event",
                priority = eventAttr?.Priority ?? allAttr?.Priority,
                allowSelfHandling = eventAttr?.AllowSelfHandling ?? allAttr?.AllowSelfHandling ?? false
            });
        }

        return list
            .OrderBy(x => ((dynamic)x).visibility)
            .ThenBy(x => ((dynamic)x).name)
            .ToList();
    }

    private static ChatRequestEvent BuildChatRequest(ChatRequestInput input)
    {
        var request = new ChatRequestEvent
        {
            RequestId = (input.RequestId ?? string.Empty).Trim(),
            UserId = (input.UserId ?? string.Empty).Trim(),
            Message = (input.Message ?? string.Empty).Trim(),
            Temperature = input.Temperature ?? 0,
            MaxTokens = input.MaxTokens ?? 0,
            StreamChunkEveryN = input.StreamChunkEveryN ?? 0,
            Timestamp = ParseTimestampOrNow(input.Timestamp)
        };

        if (input.Context != null)
        {
            foreach (var (key, value) in input.Context)
            {
                if (!string.IsNullOrWhiteSpace(key))
                    request.Context[key] = value ?? string.Empty;
            }
        }

        return request;
    }

    private static Timestamp ParseTimestampOrNow(string? timestamp)
    {
        if (!string.IsNullOrWhiteSpace(timestamp) &&
            DateTimeOffset.TryParse(timestamp, out var parsed))
        {
            return Timestamp.FromDateTime(parsed.UtcDateTime);
        }

        return Timestamp.FromDateTime(DateTime.UtcNow);
    }

    private static int ReadInt(HttpRequest request, string key, int fallback, int min, int max)
    {
        var raw = request.Query.TryGetValue(key, out var values) ? values.ToString() : string.Empty;
        if (!int.TryParse(raw, out var value))
            return fallback;
        return Math.Clamp(value, min, max);
    }

    private static IEnumerable<AevatarChatMessage> TrimHistory(
        IEnumerable<AevatarChatMessage> history,
        int limit)
    {
        if (limit <= 0)
            return history;

        var list = history.ToList();
        if (list.Count <= limit)
            return list;

        return list.TakeLast(limit);
    }

    private static object MapEntry(MemoryEntry entry)
    {
        return new
        {
            id = entry.EntryId,
            role = entry.Role,
            content = entry.Content ?? string.Empty,
            timestamp = entry.CreatedAt?.ToDateTime().ToUniversalTime().ToString("O")
        };
    }
}
