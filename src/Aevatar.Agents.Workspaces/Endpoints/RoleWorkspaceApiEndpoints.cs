using System.Text.Json;
using System.Text.Json.Serialization;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.Tooling.Catalog;
using Aevatar.Agents.Workspaces.Core;
using Aevatar.Agents.Workspaces.Hubs;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using AgUiTextMessageStartEvent = Aevatar.Agents.AGUI.TextMessageStartEvent;
using AgUiTextMessageContentEvent = Aevatar.Agents.AGUI.TextMessageContentEvent;
using AgUiTextMessageEndEvent = Aevatar.Agents.AGUI.TextMessageEndEvent;

namespace Aevatar.Agents.Workspaces.Endpoints;

public static class RoleWorkspaceApiEndpoints
{
    public static void MapRoleWorkspaceEndpoints(this WebApplication app)
    {
        app.MapGet("/api/roles", (RoleWorkspaceService workspace) =>
        {
            return Results.Ok(new
            {
                rootRole = workspace.RootRole,
                roles = workspace.GetKnownRoles(),
                instances = workspace.GetInstances()
            });
        });

        app.MapGet("/api/roles/graph", async (RoleWorkspaceService workspace, CancellationToken ct) =>
        {
            await workspace.EnsureRootAsync(ct);
            return Results.Ok(workspace.GetGraphSnapshot());
        });

        app.MapGet("/api/roles/yaml", (string? role) =>
        {
            var key = GlobalAgentYamlRegistry.NormalizeRoleKey(role);
            if (string.IsNullOrWhiteSpace(key))
                return Results.BadRequest(new { error = "role is required" });

            var path = AgentYamlConfigLoader.GetConfigFilePath(key);
            if (!File.Exists(path))
                return Results.NotFound(new { error = "role yaml not found", path });

            var yaml = File.ReadAllText(path);
            return Results.Ok(new { role = key, path, yaml });
        });

        app.MapPost("/api/roles/yaml", async (
            HttpRequest req,
            CancellationToken ct) =>
        {
            var input = await req.ReadFromJsonAsync<RoleYamlInput>(cancellationToken: ct);
            var yaml = input?.Yaml ?? string.Empty;
            if (string.IsNullOrWhiteSpace(yaml))
                return Results.BadRequest(new { error = "yaml is required" });

            var loader = new AgentYamlConfigLoader();
            AgentYamlConfig cfg;
            try
            {
                cfg = loader.LoadFromString(yaml);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = $"invalid yaml: {ex.Message}" });
            }

            var role = GlobalAgentYamlRegistry.NormalizeRoleKey(cfg.Id);
            if (string.IsNullOrWhiteSpace(role))
                return Results.BadRequest(new { error = "agent id is required" });

            var dir = AgentYamlConfigLoader.GetDefaultConfigDirectory();
            Directory.CreateDirectory(dir);
            var path = AgentYamlConfigLoader.GetConfigFilePath(role);
            await File.WriteAllTextAsync(path, yaml, ct);

            return Results.Ok(new { role, path });
        });

        app.MapPost("/api/roles/instances", async (
            RoleInstanceInput input,
            RoleWorkspaceService workspace,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(input?.Role))
                return Results.BadRequest(new { error = "role is required" });

            var snapshot = await workspace.EnsureRoleAsync(
                input.Role,
                linkToRoot: input.LinkToRoot ?? true,
                ct);

            if (input.SetAsRoot == true)
            {
                await workspace.SetRootAsync(input.Role, ct);
            }

            return Results.Ok(snapshot);
        });

        app.MapPost("/api/roles/instances/rebuild", async (
            RoleInstanceRebuildInput input,
            RoleWorkspaceService workspace,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(input?.Role))
                return Results.BadRequest(new { error = "role is required" });

            var snapshot = await workspace.RebuildRoleAsync(
                input.Role,
                linkToRoot: input.LinkToRoot ?? true,
                setAsRoot: input.SetAsRoot ?? false,
                ct);

            return Results.Ok(snapshot);
        });

        app.MapPost("/api/roles/instances/remove", async (
            RoleInstanceRemoveInput input,
            RoleWorkspaceService workspace,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(input?.Role))
                return Results.BadRequest(new { error = "role is required" });

            try
            {
                var removed = await workspace.RemoveInstanceAsync(input.Role, ct);
                return Results.Ok(new { ok = removed });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/api/roles/link", async (
            RoleLinkInput input,
            RoleWorkspaceService workspace,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(input?.ParentRole) || string.IsNullOrWhiteSpace(input.ChildRole))
                return Results.BadRequest(new { error = "parentRole and childRole are required" });

            await workspace.LinkAsync(input.ParentRole, input.ChildRole, ct);
            return Results.Ok(new { ok = true });
        });

        app.MapPost("/api/roles/unlink", async (
            RoleLinkInput input,
            RoleWorkspaceService workspace,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(input?.ParentRole) || string.IsNullOrWhiteSpace(input.ChildRole))
                return Results.BadRequest(new { error = "parentRole and childRole are required" });

            await workspace.UnlinkAsync(input.ParentRole, input.ChildRole, ct);
            return Results.Ok(new { ok = true });
        });

        app.MapGet("/api/roles/{role}/tools/catalog", async (
            string role,
            RoleWorkspaceService workspace,
            AgentToolCatalog catalog,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(role))
                return Results.BadRequest(new { error = "role is required" });

            var agent = await workspace.GetRoleAgentAsync(role, ct);
            var tools = await catalog.GetToolsAsync(agent, ct);
            return Results.Ok(new { role, tools });
        });

        app.MapPost("/api/roles/{role}/tools/dotnet/register", async (
            string role,
            HttpRequest req,
            RoleWorkspaceService workspace,
            AgentToolCatalog catalog,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(role))
                return Results.BadRequest(new { error = "role is required" });

            var input = await req.ReadFromJsonAsync<RegisterRoleToolInput>(cancellationToken: ct);
            if (string.IsNullOrWhiteSpace(input?.FilePath))
                return Results.BadRequest(new { error = "filePath is required" });

            var agent = await workspace.GetRoleAgentAsync(role, ct);
            var tool = await catalog.RegisterDotNetFileAsync(agent, input.FilePath, ct);
            if (tool == null)
                return Results.BadRequest(new { error = "dotnet tool file not allowed or invalid" });

            return Results.Ok(new { tool });
        });

        app.MapPost("/api/roles/{role}/chat", async (
            string role,
            HttpRequest req,
            RoleWorkspaceService workspace,
            CancellationToken ct) =>
        {
            var input = await req.ReadFromJsonAsync<RoleChatRequestInput>(cancellationToken: ct);
            var message = (input?.Message ?? string.Empty).Trim();
            if (message.Length == 0)
                return Results.BadRequest(new { error = "message is required" });

            var request = BuildChatRequest(input, message);
            var runId = await workspace.PublishRoleChatAsync(role, request, ct);
            return Results.Ok(new { runId });
        });

        app.MapPost("/api/roles/chat", async (
            HttpRequest req,
            RoleWorkspaceService workspace,
            CancellationToken ct) =>
        {
            var input = await req.ReadFromJsonAsync<RoleChatRequestInput>(cancellationToken: ct);
            var message = (input?.Message ?? string.Empty).Trim();
            if (message.Length == 0)
                return Results.BadRequest(new { error = "message is required" });

            var request = BuildChatRequest(input, message);
            var runId = await workspace.PublishChatAsync(request, ct);
            return Results.Ok(new { runId });
        });

        app.MapGet("/api/roles/{role}/agui/events", async (
            HttpContext http,
            string role,
            RoleWorkspaceService workspace,
            IOptions<RoleWorkspaceOptions> options,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(role))
                return Results.BadRequest(new { error = "role is required" });

            http.Response.StatusCode = StatusCodes.Status200OK;
            http.Response.Headers.ContentType = "text/event-stream; charset=utf-8";
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers.Pragma = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";

            await http.Response.StartAsync(ct);

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            await using var writer = new AgUiSseWriter(http.Response, jsonOptions);

            var snapshot = workspace.GetMessagesSnapshot(role, options.Value.MaxSnapshotMessages);
            await writer.WriteAsync(new MessagesSnapshotEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Messages = snapshot.ToList()
            }, ct);

            var roleKey = GlobalAgentYamlRegistry.NormalizeRoleKey(role);
            if (string.IsNullOrWhiteSpace(roleKey))
                roleKey = "assistant";
            var prefix = $"msg:role:{roleKey}:";

            await foreach (var evt in workspace.SubscribeAsync(ct))
            {
                ct.ThrowIfCancellationRequested();

                if (evt is MessagesSnapshotEvent snapshotEvent)
                {
                    var filtered = snapshotEvent.Messages
                        .Where(m => m.Id.StartsWith(prefix, StringComparison.Ordinal))
                        .ToList();
                    await writer.WriteAsync(snapshotEvent with { Messages = filtered }, ct);
                    continue;
                }

                if (evt is AgUiTextMessageStartEvent start &&
                    start.MessageId.StartsWith(prefix, StringComparison.Ordinal))
                {
                    await writer.WriteAsync(start, ct);
                    continue;
                }

                if (evt is AgUiTextMessageContentEvent content &&
                    content.MessageId.StartsWith(prefix, StringComparison.Ordinal))
                {
                    await writer.WriteAsync(content, ct);
                    continue;
                }

                if (evt is AgUiTextMessageEndEvent end &&
                    end.MessageId.StartsWith(prefix, StringComparison.Ordinal))
                {
                    await writer.WriteAsync(end, ct);
                }
            }

            return Results.Ok();
        });

        app.MapGet("/api/roles/agui/events", async (
            HttpContext http,
            RoleWorkspaceService workspace,
            IOptions<RoleWorkspaceOptions> options,
            CancellationToken ct) =>
        {
            await workspace.EnsureRootAsync(ct);

            http.Response.StatusCode = StatusCodes.Status200OK;
            http.Response.Headers.ContentType = "text/event-stream; charset=utf-8";
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers.Pragma = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";

            await http.Response.StartAsync(ct);

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            await using var writer = new AgUiSseWriter(http.Response, jsonOptions);

            var snapshot = workspace.GetMessagesSnapshot(options.Value.MaxSnapshotMessages);
            await writer.WriteAsync(new MessagesSnapshotEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Messages = snapshot.ToList()
            }, ct);

            await foreach (var evt in workspace.SubscribeAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                await writer.WriteAsync(evt, ct);
            }

            return Results.Ok();
        });
    }

    private static ChatRequestEvent BuildChatRequest(RoleChatRequestInput? input, string message)
    {
        var request = new ChatRequestEvent
        {
            RequestId = (input?.RequestId ?? string.Empty).Trim(),
            UserId = (input?.UserId ?? string.Empty).Trim(),
            Message = message,
            Temperature = input?.Temperature ?? 0,
            MaxTokens = input?.MaxTokens ?? 0,
            StreamChunkEveryN = input?.StreamChunkEveryN ?? 0
        };

        if (input?.Context != null)
        {
            foreach (var (key, value) in input.Context)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    request.Context[key] = value ?? string.Empty;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(input?.Timestamp) &&
            DateTimeOffset.TryParse(input.Timestamp, out var parsedTimestamp))
        {
            request.Timestamp = Timestamp.FromDateTime(parsedTimestamp.UtcDateTime);
        }
        else
        {
            request.Timestamp = Timestamp.FromDateTime(DateTime.UtcNow);
        }

        return request;
    }
}
