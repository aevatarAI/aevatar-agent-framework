using System.Text.Json;
using Aevatar.Learning.Notebooks;
using Aevatar.Learning.Skills;

namespace Aevatar.Learning.Api.Skills;

// ============================================================
//  Skills API (notebook-scoped)
//
//  Endpoints:
//  - POST /api/notebooks/{id}/skills:generate
//  - GET  /api/notebooks/{id}/skills
//  - GET  /api/notebooks/{id}/skills/{version}
// ============================================================
internal static class SkillsApi
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void MapSkillsApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        MapGenerate(app);
        MapList(app);
        MapGet(app);
    }

    private static void MapGenerate(WebApplication app)
    {
        app.MapPost("/api/notebooks/{notebookId}/skills:generate", async (
            string notebookId,
            GenerateInDto input,
            NotebookDirectoryStore notebooks,
            SkillsService skills,
            CancellationToken ct) =>
        {
            var ws = ResolveWorkspace(notebooks, notebookId);
            if (ws == null)
                return Results.NotFound(new { error = "notebook not found" });

            try
            {
                var result = await skills.GenerateAsync(
                    ws,
                    theme: string.IsNullOrWhiteSpace(input.Theme) ? null : input.Theme.Trim(),
                    providerName: string.IsNullOrWhiteSpace(input.ProviderName) ? null : input.ProviderName.Trim(),
                    selectedSourceIds: input.SelectedSourceIds,
                    ct: ct);

                return Results.Json(new
                {
                    ok = true,
                    meta = result.Meta,
                    bundle = result.Bundle
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }

    private static void MapList(WebApplication app)
    {
        app.MapGet("/api/notebooks/{notebookId}/skills", (
            string notebookId,
            NotebookDirectoryStore notebooks) =>
        {
            var ws = ResolveWorkspace(notebooks, notebookId);
            if (ws == null)
                return Results.NotFound(new { error = "notebook not found" });

            if (!Directory.Exists(ws.SkillsDir))
                return Results.Json(new { ok = true, count = 0, versions = Array.Empty<object>() });

            var items = new List<SkillsBundleMeta>(capacity: 32);
            foreach (var dir in Directory.EnumerateDirectories(ws.SkillsDir))
            {
                var metaPath = Path.Combine(dir, "meta.json");
                if (!File.Exists(metaPath))
                    continue;

                try
                {
                    var json = File.ReadAllText(metaPath);
                    var meta = JsonSerializer.Deserialize<SkillsBundleMeta>(json, Json);
                    if (meta == null || string.IsNullOrWhiteSpace(meta.Version))
                        continue;
                    items.Add(meta);
                }
                catch
                {
                    // best-effort
                }
            }

            var sorted = items
                .OrderByDescending(x => x.CreatedAt)
                .ThenBy(x => x.Version, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Results.Json(new { ok = true, count = sorted.Count, versions = sorted });
        });
    }

    private static void MapGet(WebApplication app)
    {
        app.MapGet("/api/notebooks/{notebookId}/skills/{version}", (
            string notebookId,
            string version,
            NotebookDirectoryStore notebooks) =>
        {
            var ws = ResolveWorkspace(notebooks, notebookId);
            if (ws == null)
                return Results.NotFound(new { error = "notebook not found" });

            version = (version ?? string.Empty).Trim();
            if (version.Length == 0)
                return Results.BadRequest(new { error = "version is required" });

            var dir = Path.Combine(ws.SkillsDir, version);
            var metaPath = Path.Combine(dir, "meta.json");
            var jsonPath = Path.Combine(dir, "skill.json");
            var mdPath = Path.Combine(dir, "skill.md");

            if (!File.Exists(metaPath) || !File.Exists(jsonPath))
                return Results.NotFound(new { error = "skill bundle not found" });

            try
            {
                var metaJson = File.ReadAllText(metaPath);
                var meta = JsonSerializer.Deserialize<SkillsBundleMeta>(metaJson, Json);
                if (meta == null)
                    return Results.NotFound(new { error = "skill bundle not found" });

                var bundleJson = File.ReadAllText(jsonPath);
                var bundle = JsonSerializer.Deserialize<SkillsBundle>(bundleJson, Json);
                if (bundle == null)
                    return Results.NotFound(new { error = "skill bundle not found" });

                var md = File.Exists(mdPath) ? File.ReadAllText(mdPath) : "";

                // Keep payload bounded: include structured bundle + markdown (already bounded by generator).
                return Results.Json(new { ok = true, meta, bundle, markdown = md });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }

    private static NotebookWorkspace? ResolveWorkspace(NotebookDirectoryStore notebooks, string notebookId)
    {
        var nb = notebooks.GetNotebook(notebookId);
        if (nb == null)
            return null;

        var rootDir = Path.GetDirectoryName(nb.DirectoryPath) ?? string.Empty;
        return new NotebookWorkspace(nb.NotebookId, rootDir, nb.DirectoryPath);
    }

    private sealed record GenerateInDto(
        string? Theme,
        string? ProviderName,
        IReadOnlyList<string>? SelectedSourceIds);
}


