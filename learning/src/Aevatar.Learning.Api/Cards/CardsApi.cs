using Aevatar.Learning.Cards;
using Aevatar.Learning.Notebooks;

namespace Aevatar.Learning.Api.Cards;

// ============================================================
//  Cards API (notebook-scoped)
//
//  Endpoints:
//  - POST /api/notebooks/{id}/cards:generate
//  - GET  /api/notebooks/{id}/cards:daily
//  - POST /api/notebooks/{id}/cards:review
//  - GET  /api/notebooks/{id}/cards:stats
// ============================================================
internal static class CardsApi
{
    public static void MapCardsApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        MapGenerate(app);
        MapDaily(app);
        MapReview(app);
        MapStats(app);
    }

    private static void MapGenerate(WebApplication app)
    {
        app.MapPost("/api/notebooks/{notebookId}/cards:generate", async (
            string notebookId,
            GenerateInDto input,
            NotebookDirectoryStore notebooks,
            CardsService cards,
            CancellationToken ct) =>
        {
            var ws = ResolveWorkspace(notebooks, notebookId);
            if (ws == null)
                return Results.NotFound(new { error = "notebook not found" });

            if (string.IsNullOrWhiteSpace(input.Topic))
                return Results.BadRequest(new { error = "topic is required" });

            var count = input.Count ?? 10;
            try
            {
                var created = await cards.GenerateAsync(
                    ws,
                    topic: input.Topic.Trim(),
                    count: count,
                    providerName: string.IsNullOrWhiteSpace(input.ProviderName) ? null : input.ProviderName.Trim(),
                    selectedSourceIds: input.SelectedSourceIds,
                    ct: ct);

                return Results.Json(new { ok = true, count = created.Count, cards = created });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }

    private static void MapDaily(WebApplication app)
    {
        app.MapGet("/api/notebooks/{notebookId}/cards:daily", async (
            string notebookId,
            int? maxDue,
            int? maxNew,
            NotebookDirectoryStore notebooks,
            CardsService cards,
            CancellationToken ct) =>
        {
            var ws = ResolveWorkspace(notebooks, notebookId);
            if (ws == null)
                return Results.NotFound(new { error = "notebook not found" });

            try
            {
                var queue = await cards.GetDailyQueueAsync(
                    ws,
                    nowUtc: null,
                    maxDue: maxDue ?? 20,
                    maxNew: maxNew ?? 20,
                    ct: ct);

                return Results.Json(new { ok = true, queue });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }

    private static void MapReview(WebApplication app)
    {
        app.MapPost("/api/notebooks/{notebookId}/cards:review", async (
            string notebookId,
            ReviewInDto input,
            NotebookDirectoryStore notebooks,
            CardsService cards,
            CancellationToken ct) =>
        {
            var ws = ResolveWorkspace(notebooks, notebookId);
            if (ws == null)
                return Results.NotFound(new { error = "notebook not found" });

            if (string.IsNullOrWhiteSpace(input.CardId))
                return Results.BadRequest(new { error = "cardId is required" });

            try
            {
                var result = await cards.ReviewAsync(
                    ws,
                    cardId: input.CardId.Trim(),
                    grade: input.Grade,
                    generateMnemonic: input.GenerateMnemonic,
                    providerName: string.IsNullOrWhiteSpace(input.ProviderName) ? null : input.ProviderName.Trim(),
                    ct: ct);

                return Results.Json(new { ok = true, result });
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound(new { error = "card not found" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }

    private static void MapStats(WebApplication app)
    {
        app.MapGet("/api/notebooks/{notebookId}/cards:stats", async (
            string notebookId,
            NotebookDirectoryStore notebooks,
            CardsService cards,
            CancellationToken ct) =>
        {
            var ws = ResolveWorkspace(notebooks, notebookId);
            if (ws == null)
                return Results.NotFound(new { error = "notebook not found" });

            try
            {
                var stats = await cards.GetStatsAsync(ws, nowUtc: null, ct: ct);
                return Results.Json(new { ok = true, stats });
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
        string Topic,
        int? Count,
        string? ProviderName,
        IReadOnlyList<string>? SelectedSourceIds);

    private sealed record ReviewInDto(
        string CardId,
        int Grade,
        bool GenerateMnemonic,
        string? ProviderName);
}


