using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Workshop;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Options;

namespace Aevatar.Workshop.RoleWorkspace;

public static class WorkshopRoleWorkspaceEndpoints
{
    public static void MapRoleWorkspaceEndpoints(this WebApplication app)
    {
        app.MapGet("/api/roles", (WorkshopRoleWorkspace workspace) =>
        {
            return Results.Ok(new
            {
                rootRole = workspace.RootRole,
                roles = workspace.GetKnownRoles(),
                instances = workspace.GetInstances()
            });
        });

        app.MapGet("/api/roles/graph", async (WorkshopRoleWorkspace workspace, CancellationToken ct) =>
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
            WorkshopRoleWorkspace workspace,
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

        app.MapPost("/api/roles/instances/remove", async (
            RoleInstanceRemoveInput input,
            WorkshopRoleWorkspace workspace,
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
            WorkshopRoleWorkspace workspace,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(input?.ParentRole) || string.IsNullOrWhiteSpace(input.ChildRole))
                return Results.BadRequest(new { error = "parentRole and childRole are required" });

            await workspace.LinkAsync(input.ParentRole, input.ChildRole, ct);
            return Results.Ok(new { ok = true });
        });

        app.MapPost("/api/roles/unlink", async (
            RoleLinkInput input,
            WorkshopRoleWorkspace workspace,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(input?.ParentRole) || string.IsNullOrWhiteSpace(input.ChildRole))
                return Results.BadRequest(new { error = "parentRole and childRole are required" });

            await workspace.UnlinkAsync(input.ParentRole, input.ChildRole, ct);
            return Results.Ok(new { ok = true });
        });

        app.MapPost("/api/roles/chat", async (
            HttpRequest req,
            WorkshopRoleWorkspace workspace,
            CancellationToken ct) =>
        {
            var input = await req.ReadFromJsonAsync<ChatRequestInput>(cancellationToken: ct);
            var message = (input?.Message ?? string.Empty).Trim();
            if (message.Length == 0)
                return Results.BadRequest(new { error = "message is required" });

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

            var runId = await workspace.PublishChatAsync(request, ct);
            return Results.Ok(new { runId });
        });

        app.MapGet("/api/roles/agui/events", async (
            HttpContext http,
            WorkshopRoleWorkspace workspace,
            IOptions<WorkshopOptions> options,
            CancellationToken ct) =>
        {
            await workspace.EnsureRootAsync(ct);

            http.Response.StatusCode = StatusCodes.Status200OK;
            http.Response.Headers.ContentType = "text/event-stream; charset=utf-8";
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers.Pragma = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";

            await http.Response.StartAsync(ct);

            await using var writer = new StreamWriter(
                http.Response.Body,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 256,
                leaveOpen: true);

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            async Task WriteSseAsync(AgUiEvent evt, CancellationToken token)
            {
                var line = JsonSerializer.Serialize((object)evt, evt.GetType(), jsonOptions);
                await writer.WriteAsync("data: ");
                await writer.WriteLineAsync(line);
                await writer.WriteLineAsync();
                await writer.FlushAsync();
            }

            var snapshot = workspace.GetMessagesSnapshot(options.Value.MaxSnapshotMessages);
            await WriteSseAsync(new MessagesSnapshotEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Messages = snapshot.ToList()
            }, ct);

            await foreach (var evt in workspace.SubscribeAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                await WriteSseAsync(evt, ct);
            }
        });
    }
}
