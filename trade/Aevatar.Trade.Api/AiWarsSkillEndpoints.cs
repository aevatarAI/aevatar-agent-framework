using System.Text.Json;
using Aevatar.Agents.AI.WithTool.Abstractions;
using Aevatar.Agents.AI.WithTool.Tools.CustomTools;
using Aevatar.Trade.Tools;
using Google.Protobuf;
using Microsoft.AspNetCore.Mvc;

namespace Aevatar.Trade.Api;

/// <summary>
/// AI Wars DotNetSkills → HTTP endpoints (for Swagger + Frontend).
///
/// Design:
/// - Scan `Tools/DotNetSkills/ai-wars/**.cs`
/// - Each skill becomes a POST endpoint:
///   /api/ai-wars/{toolName}
/// - The request body is the tool parameters JSON object (same as dotnet-file skill STDIN contract).
///
/// Safety:
/// - If a tool is marked RequiresConfirmation or IsDangerous, caller must pass `?confirm=true`.
/// </summary>
public static class AiWarsSkillEndpoints
{
    public sealed record AiWarsToolDto
    {
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required string Version { get; init; }
        public required string Category { get; init; }
        public required IReadOnlyList<string> Tags { get; init; }
        public required bool RequiresConfirmation { get; init; }
        public required bool IsDangerous { get; init; }
        public required ToolParameters Parameters { get; init; }
        public required string Route { get; init; }
        public required string File { get; init; }
    }

    public static async Task MapAiWarsSkillEndpointsAsync(this WebApplication app, CancellationToken ct = default)
    {
        var logger = app.Logger;

        // Build tool registry once at startup.
        var tools = new List<AiWarsToolDto>(capacity: 64);
        var toolByName = new Dictionary<string, (DotNetFileSkillTool Tool, ToolDefinition Def, string FilePath)>(
            StringComparer.OrdinalIgnoreCase);

        var ctx = new ToolContext
        {
            AgentId = "trade-api",
            AgentType = "Aevatar.Trade.Api",
            Logger = logger
        };

        foreach (var file in TradeDotNetSkillPaths.WeexAiWarsAll)
        {
            try
            {
                var tool = await DotNetFileSkillTool.LoadFromFileAsync(file, logger: logger, cancellationToken: ct);
                var def = tool.CreateToolDefinition(ctx, logger);
                var route = $"/api/ai-wars/{def.Name}";
                var displayFile = ToDisplayPath(file);

                toolByName[def.Name] = (tool, def, file);
                tools.Add(new AiWarsToolDto
                {
                    Name = def.Name,
                    Description = def.Description,
                    Version = def.Version,
                    Category = def.Category.ToString(),
                    Tags = def.Tags.ToList(),
                    RequiresConfirmation = def.RequiresConfirmation,
                    IsDangerous = def.IsDangerous,
                    Parameters = def.Parameters,
                    Route = route,
                    File = displayFile
                });

                // Map each tool as its own endpoint (so Swagger shows every AI Wars API skill).
                app.MapPost(
                        route,
                        async (
                            [FromBody] JsonElement body,
                            CancellationToken requestCt,
                            [FromQuery] bool confirm = false) =>
                        {
                            if (!toolByName.TryGetValue(def.Name, out var entry))
                            {
                                return Results.NotFound(new { error = $"Tool not found: {def.Name}" });
                            }

                            var toolDef = entry.Def;
                            if ((toolDef.IsDangerous || toolDef.RequiresConfirmation) && !confirm)
                            {
                                return Results.BadRequest(new
                                {
                                    error = "This endpoint is marked dangerous and/or requires confirmation.",
                                    hint = "Pass ?confirm=true to execute.",
                                    tool = toolDef.Name
                                });
                            }

                            // Tool contract: parameters JSON object
                            var parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                            if (body.ValueKind == JsonValueKind.Object)
                            {
                                foreach (var p in body.EnumerateObject())
                                {
                                    parameters[p.Name] = p.Value.Clone();
                                }
                            }

                            var result = await entry.Tool.ExecuteAsync(parameters, ctx, logger, requestCt);
                            var json = Google.Protobuf.JsonFormatter.Default.Format(result);
                            return Results.Text(json, "application/json");
                        })
                    .WithTags("AI Wars (DotNetSkills)")
                    .WithSummary(def.Description)
                    .WithDescription($"DotNetSkill file: {displayFile}");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load AI Wars skill file: {File}", file);
            }
        }

        // Index endpoints (so user can discover routes & parameters quickly).
        app.MapGet("/api/ai-wars", () => Results.Ok(new
            {
                count = tools.Count,
                tools = tools
                    .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            }))
            .WithTags("AI Wars (DotNetSkills)")
            .WithSummary("List all AI Wars DotNetSkills exposed as HTTP endpoints");
    }

    private static string ToDisplayPath(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return string.Empty;

        // Try to strip to repo-relative style: ".../Tools/DotNetSkills/xxx.cs" -> "Tools/DotNetSkills/xxx.cs"
        var normalized = fullPath.Replace('\\', '/');
        const string marker = "/Tools/DotNetSkills/";
        var idx = normalized.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
            return "Tools/DotNetSkills/" + normalized[(idx + marker.Length)..];

        return Path.GetFileName(normalized);
    }
}


