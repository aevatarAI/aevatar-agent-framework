using System.Text.Json;
using Aevatar.Learning.Notebooks;
using Aevatar.Learning.Reports;

namespace Aevatar.Learning.Api.Reports;

// ============================================================
//  Reports API (notebook-scoped)
//
//  Endpoints:
//  - POST /api/notebooks/{id}/reports:generate
//  - GET  /api/notebooks/{id}/reports
//  - GET  /api/notebooks/{id}/reports/{reportId}
// ============================================================
internal static class ReportsApi
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void MapReportsApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        MapGenerate(app);
        MapList(app);
        MapGet(app);
    }

    private static void MapGenerate(WebApplication app)
    {
        app.MapPost("/api/notebooks/{notebookId}/reports:generate", async (
            string notebookId,
            GenerateReportInDto input,
            NotebookDirectoryStore notebooks,
            LearningReportService reports,
            CancellationToken ct) =>
        {
            var ws = ResolveWorkspace(notebooks, notebookId);
            if (ws == null)
                return Results.NotFound(new { error = "notebook not found" });

            if (string.IsNullOrWhiteSpace(input.Topic))
                return Results.BadRequest(new { error = "topic is required" });

            try
            {
                var result = await reports.GenerateAsync(
                    ws,
                    topic: input.Topic.Trim(),
                    providerName: string.IsNullOrWhiteSpace(input.ProviderName) ? null : input.ProviderName.Trim(),
                    selectedSourceIds: input.SelectedSourceIds,
                    budget: null,
                    ct: ct);

                return Results.Json(new
                {
                    ok = true,
                    report = result.Meta,
                    // Keep payload bounded: return preview only in API response
                    preview = result.Content.Length <= 2000 ? result.Content : result.Content[..2000] + "\n...(truncated)"
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
        app.MapGet("/api/notebooks/{notebookId}/reports", (
            string notebookId,
            NotebookDirectoryStore notebooks) =>
        {
            var ws = ResolveWorkspace(notebooks, notebookId);
            if (ws == null)
                return Results.NotFound(new { error = "notebook not found" });

            if (!Directory.Exists(ws.ReportsDir))
                return Results.Json(new { count = 0, reports = Array.Empty<object>() });

            var list = new List<object>(capacity: 32);
            foreach (var dir in Directory.EnumerateDirectories(ws.ReportsDir))
            {
                var metaPath = Path.Combine(dir, "meta.json");
                if (!File.Exists(metaPath))
                    continue;

                try
                {
                    var json = File.ReadAllText(metaPath);
                    var meta = JsonSerializer.Deserialize<LearningReportMeta>(json, Json);
                    if (meta == null || string.IsNullOrWhiteSpace(meta.ReportId))
                        continue;
                    list.Add(meta);
                }
                catch
                {
                    // best-effort
                }
            }

            // Sort by updatedAt desc when available.
            var sorted = list
                .OfType<LearningReportMeta>()
                .OrderByDescending(x => x.UpdatedAt)
                .ThenBy(x => x.Topic, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Results.Json(new { count = sorted.Count, reports = sorted });
        });
    }

    private static void MapGet(WebApplication app)
    {
        app.MapGet("/api/notebooks/{notebookId}/reports/{reportId}", (
            string notebookId,
            string reportId,
            NotebookDirectoryStore notebooks) =>
        {
            var ws = ResolveWorkspace(notebooks, notebookId);
            if (ws == null)
                return Results.NotFound(new { error = "notebook not found" });

            reportId = (reportId ?? string.Empty).Trim();
            if (reportId.Length == 0)
                return Results.BadRequest(new { error = "reportId is required" });

            var dir = Path.Combine(ws.ReportsDir, reportId);
            var metaPath = Path.Combine(dir, "meta.json");
            if (!File.Exists(metaPath))
                return Results.NotFound(new { error = "report not found" });

            try
            {
                var metaJson = File.ReadAllText(metaPath);
                var meta = JsonSerializer.Deserialize<LearningReportMeta>(metaJson, Json);
                if (meta == null)
                    return Results.NotFound(new { error = "report not found" });

                var version = meta.CurrentVersion <= 0 ? 1 : meta.CurrentVersion;
                var filePath = Path.Combine(dir, $"v{version}.md");
                if (!File.Exists(filePath))
                    return Results.NotFound(new { error = "report content not found" });

                var content = File.ReadAllText(filePath);
                return Results.Json(new { meta, version, content });
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

    private sealed record GenerateReportInDto(
        string Topic,
        string? ProviderName,
        IReadOnlyList<string>? SelectedSourceIds);
}


