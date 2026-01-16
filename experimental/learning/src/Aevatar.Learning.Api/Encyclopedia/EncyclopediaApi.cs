using Aevatar.Learning.Encyclopedia;
using Aevatar.Learning.Notebooks;

namespace Aevatar.Learning.Api.Encyclopedia;

// ============================================================
//  Encyclopedia API (notebook-scoped)
//
//  Endpoints:
//  - POST /api/notebooks/{id}/encyclopedia:build
//  - POST /api/notebooks/{id}/encyclopedia:query
// ============================================================
internal static class EncyclopediaApi
{
    public static void MapEncyclopediaApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        MapBuild(app);
        MapQuery(app);
    }

    private static void MapBuild(WebApplication app)
    {
        app.MapPost("/api/notebooks/{notebookId}/encyclopedia:build", async (
            string notebookId,
            BuildInDto input,
            NotebookDirectoryStore notebooks,
            EncyclopediaService encyclopedia,
            CancellationToken ct) =>
        {
            var ws = ResolveWorkspace(notebooks, notebookId);
            if (ws == null)
                return Results.NotFound(new { error = "notebook not found" });

            try
            {
                var result = await encyclopedia.BuildAsync(
                    ws,
                    providerName: string.IsNullOrWhiteSpace(input.ProviderName) ? null : input.ProviderName.Trim(),
                    ct: ct);

                return Results.Json(new { ok = true, meta = result.Meta });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }

    private static void MapQuery(WebApplication app)
    {
        app.MapPost("/api/notebooks/{notebookId}/encyclopedia:query", async (
            string notebookId,
            QueryInDto input,
            NotebookDirectoryStore notebooks,
            EncyclopediaService encyclopedia,
            CancellationToken ct) =>
        {
            var ws = ResolveWorkspace(notebooks, notebookId);
            if (ws == null)
                return Results.NotFound(new { error = "notebook not found" });

            if (string.IsNullOrWhiteSpace(input.Query))
                return Results.BadRequest(new { error = "query is required" });

            try
            {
                var result = await encyclopedia.QueryAsync(
                    ws,
                    query: input.Query.Trim(),
                    providerName: string.IsNullOrWhiteSpace(input.ProviderName) ? null : input.ProviderName.Trim(),
                    selectedSourceIds: input.SelectedSourceIds,
                    ct: ct);

                var rawPreview = result.RawText.Length <= 2000
                    ? result.RawText
                    : result.RawText[..2000] + "\n...(truncated)";

                // Keep payload bounded: return structured fields + a small raw preview for debugging.
                return Results.Json(new
                {
                    ok = true,
                    result = new
                    {
                        result.Query,
                        result.ProviderName,
                        result.RecommendedItems,
                        result.WhyItWorks,
                        result.MassageTechniques,
                        result.RelatedTheory,
                        result.Cautions,
                        result.RelatedConcepts,
                        result.SourceIds,
                        rawPreview
                    }
                });
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

    private sealed record BuildInDto(string? ProviderName);

    private sealed record QueryInDto(
        string Query,
        string? ProviderName,
        IReadOnlyList<string>? SelectedSourceIds);
}


