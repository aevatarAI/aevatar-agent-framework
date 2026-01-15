using Aevatar.Learning.Notebooks;
using Aevatar.Learning.Progress;

namespace Aevatar.Learning.Api.Notebooks;

// ============================================================
//  Notebooks API (directory-backed)
//
//  Endpoints:
//  - POST /api/notebooks
//  - GET  /api/notebooks
//  - GET  /api/notebooks/{id}
// ============================================================
internal static class NotebooksApi
{
    public static void MapNotebooksApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        MapCreate(app);
        MapList(app);
        MapGet(app);
    }

    private static void MapCreate(WebApplication app)
    {
        app.MapPost("/api/notebooks", (CreateNotebookInDto input, NotebookDirectoryStore store) =>
        {
            if (string.IsNullOrWhiteSpace(input.DisplayName))
                return Results.BadRequest(new { error = "displayName is required" });

            try
            {
                var nb = store.CreateNotebook(input.DisplayName.Trim(), input.RootPath);
                return Results.Json(new { ok = true, notebook = nb });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }

    private static void MapList(WebApplication app)
    {
        app.MapGet("/api/notebooks", (NotebookDirectoryStore store) =>
        {
            var list = store.ListNotebooks();
            return Results.Json(new { count = list.Count, notebooks = list });
        });
    }

    private static void MapGet(WebApplication app)
    {
        app.MapGet("/api/notebooks/{notebookId}", async (
            string notebookId,
            NotebookDirectoryStore store,
            ProgressService progress,
            CancellationToken ct) =>
        {
            var nb = store.GetNotebook(notebookId);
            if (nb == null)
                return Results.NotFound(new { error = "notebook not found" });

            var rootDir = Path.GetDirectoryName(nb.DirectoryPath) ?? string.Empty;
            var ws = new NotebookWorkspace(nb.NotebookId, rootDir, nb.DirectoryPath);

            var summary = await progress.GetSummaryAsync(ws, nowUtc: null, ct);
            var tags = summary.Tags.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

            // Avoid leaking filesystem paths unless explicitly needed.
            var notebook = new
            {
                notebookId = nb.NotebookId,
                displayName = nb.DisplayName,
                createdAt = nb.CreatedAt,
                updatedAt = nb.UpdatedAt
            };

            return Results.Json(new
            {
                notebook,
                progressSummary = new
                {
                    dueCards = summary.DueCards,
                    newCards = summary.NewCards,
                    totalSources = summary.TotalSources,
                    quizzesTaken = summary.QuizzesTaken,
                    totalReports = tags.TryGetValue("total_reports", out var tr) && int.TryParse(tr, out var v) ? v : 0,
                    partial = tags.TryGetValue("partial", out var p) && string.Equals(p, "true", StringComparison.OrdinalIgnoreCase),
                    lastActivity = summary.LastActivity?.ToDateTimeOffset().ToString("O") ?? "",
                    tags
                }
            });
        });
    }

    private sealed record CreateNotebookInDto(string DisplayName, string? RootPath);
}


