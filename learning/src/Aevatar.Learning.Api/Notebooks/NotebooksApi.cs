using Aevatar.Learning.Notebooks;

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
        app.MapGet("/api/notebooks/{notebookId}", (string notebookId, NotebookDirectoryStore store) =>
        {
            var nb = store.GetNotebook(notebookId);
            if (nb == null)
                return Results.NotFound(new { error = "notebook not found" });

            // progressSummary will be enriched in later tasks.
            return Results.Json(new
            {
                notebook = nb,
                progressSummary = new
                {
                    dueCards = 0,
                    newCards = 0,
                    totalSources = 0,
                    quizzesTaken = 0
                }
            });
        });
    }

    private sealed record CreateNotebookInDto(string DisplayName, string? RootPath);
}


