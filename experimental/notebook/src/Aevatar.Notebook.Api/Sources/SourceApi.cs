using System.Text;
using Aevatar.Agents.Abstractions.Memory;

namespace Aevatar.Notebook.Sources;

// ============================================================
//  Sources API (MVP)
//
//  Endpoints:
//  - POST /api/sources/text   : add a text source (paste)
//  - POST /api/sources/file   : upload a txt file (multipart/form-data)
//  - GET  /api/sources        : list sources
//  - GET  /api/sources/{id}   : list recent entries for a source (debug-grade)
//
//  Notes:
//  - We intentionally keep uploads bounded; MemoryStore is not a blob store.
//  - Write path uses SourceIndexer so chunks + vectors are best-effort populated.
// ============================================================
internal static class SourceApi
{
    public static void MapSourceApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        MapCreateText(app);
        MapUploadFile(app);
        MapList(app);
        MapGet(app);
    }

    private static void MapCreateText(WebApplication app)
    {
        app.MapPost("/api/sources/text", async (
            SourceTextInDto input,
            SourceIndexer indexer,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(input.Text))
                return Results.BadRequest(new { error = "text is required" });

            var sourceId = Guid.NewGuid().ToString("N")[..12];

            var result = await indexer.IndexTextSourceAsync(
                new SourceIndexRequest
                {
                    SourceId = sourceId,
                    Title = input.Title,
                    MimeType = "text/plain",
                    Text = input.Text,
                    ChunkingOptions = input.ChunkingOptions
                },
                ct: ct);

            return Results.Json(new
            {
                ok = true,
                sourceId = result.SourceId,
                memoryId = result.MemoryId,
                sourceEntryId = result.SourceEntryId,
                chunkCount = result.ChunkCount,
                vectorUpsertCount = result.VectorUpsertCount
            });
        });
    }

    private static void MapUploadFile(WebApplication app)
    {
        app.MapPost("/api/sources/file", async (
            IFormFile file,
            SourceIndexer indexer,
            CancellationToken ct) =>
        {
            if (file == null)
                return Results.BadRequest(new { error = "file is required" });

            // ------------------------------------------------------------
            //  Hard limits (prevent using MemoryStore as blob store)
            // ------------------------------------------------------------
            const long maxBytes = 1_000_000; // ~1MB
            if (file.Length <= 0)
                return Results.BadRequest(new { error = "file is empty" });
            if (file.Length > maxBytes)
                return Results.BadRequest(new { error = $"file too large (max {maxBytes} bytes)" });

            var fileName = (file.FileName ?? string.Empty).Trim();
            if (fileName.Length == 0)
                fileName = "Untitled.txt";

            // Only accept .txt for MVP (avoid arbitrary binary).
            if (!fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "only .txt files are supported for now" });

            string text;
            await using (var stream = file.OpenReadStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 16 * 1024, leaveOpen: false))
            {
                text = await reader.ReadToEndAsync(ct);
            }

            // Basic binary guard (best-effort)
            if (text.IndexOf('\0') >= 0)
                return Results.BadRequest(new { error = "file looks like binary (contains NUL)" });

            var sourceId = Guid.NewGuid().ToString("N")[..12];
            var title = fileName;

            var result = await indexer.IndexTextSourceAsync(
                new SourceIndexRequest
                {
                    SourceId = sourceId,
                    Title = title,
                    MimeType = "text/plain",
                    Text = text
                },
                ct: ct);

            return Results.Json(new
            {
                ok = true,
                sourceId = result.SourceId,
                memoryId = result.MemoryId,
                title,
                fileName,
                chunkCount = result.ChunkCount,
                vectorUpsertCount = result.VectorUpsertCount
            });
        })
        // Explicitly require multipart form upload.
        .Accepts<IFormFile>("multipart/form-data")
        // NOTE:
        // - ASP.NET Core will attach anti-forgery metadata to endpoints that bind form data (like IFormFile).
        // - This Notebook MVP has no auth cookies/session, and the SPA calls this API via fetch() without tokens.
        // - So we explicitly disable anti-forgery for this upload endpoint.
        .DisableAntiforgery();
    }

    private static void MapList(WebApplication app)
    {
        app.MapGet("/api/sources", async (IMemoryStore store, CancellationToken ct) =>
        {
            var list = await store.ListResourcesAsync(scopeTypeFilter: MemoryScopeType.Graph, limit: 200, ct: ct);
            var resources = list
                .Where(r => (r.MemoryId ?? string.Empty).StartsWith("source::", StringComparison.Ordinal))
                .OrderBy(r => r.MemoryId ?? string.Empty, StringComparer.Ordinal)
                .ToList();

            var sources = new List<object>(capacity: resources.Count);
            foreach (var r in resources)
            {
                ct.ThrowIfCancellationRequested();

                var sourceId = (r.MemoryId ?? string.Empty).Replace("source::", "", StringComparison.Ordinal);

                // Best-effort: try to read title from the latest entry tags.
                string? title = null;
                try
                {
                    var tail = await store.ListEntriesAsync(r.MemoryId ?? string.Empty, limit: 1, ct: ct);
                    var last = tail.LastOrDefault();
                    if (last?.Tags != null &&
                        last.Tags.TryGetValue("title", out var t) &&
                        !string.IsNullOrWhiteSpace(t))
                    {
                        title = t.Trim();
                    }
                }
                catch
                {
                    // ignore (best-effort)
                }

                var displayName = !string.IsNullOrWhiteSpace(title) ? title : sourceId;

                sources.Add(new
                {
                    sourceId,
                    displayName,
                    title = title ?? string.Empty,
                    memoryId = r.MemoryId,
                    entryCount = r.EntryCount,
                    latestAt = r.LatestAt?.ToDateTime().ToString("O") ?? ""
                });
            }

            return Results.Json(new { count = sources.Count, sources });
        });
    }

    private static void MapGet(WebApplication app)
    {
        app.MapGet("/api/sources/{sourceId}", async (
            string sourceId,
            int? limit,
            IMemoryStore store,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(sourceId))
                return Results.BadRequest(new { error = "sourceId is required" });

            var memoryId = $"source::{sourceId.Trim()}";
            var take = Math.Clamp(limit ?? 50, 1, 2000);
            var entries = await store.ListEntriesAsync(memoryId, limit: take, ct: ct);

            return Results.Json(new
            {
                sourceId = sourceId.Trim(),
                memoryId,
                count = entries.Count,
                entries
            });
        });
    }
}

internal sealed record SourceTextInDto(string Text)
{
    public string? Title { get; init; }
    public SourceChunkingOptions? ChunkingOptions { get; init; }
}


