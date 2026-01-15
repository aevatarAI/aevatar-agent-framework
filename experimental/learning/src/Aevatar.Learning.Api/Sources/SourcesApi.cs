using System.Text;
using Aevatar.Learning.Notebooks;
using Aevatar.Learning.Sources;

namespace Aevatar.Learning.Api.Sources;

// ============================================================
//  Sources API (notebook-scoped)
//
//  Endpoints:
//  - POST /api/notebooks/{id}/sources/text
//  - POST /api/notebooks/{id}/sources/file   (txt/md only)
//  - GET  /api/notebooks/{id}/sources
//  - GET  /api/notebooks/{id}/sources/{sourceId}
// ============================================================
internal static class SourcesApi
{
    public static void MapSourcesApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        MapCreateText(app);
        MapCreateFile(app);
        MapList(app);
        MapGet(app);
    }

    private static void MapCreateText(WebApplication app)
    {
        app.MapPost("/api/notebooks/{notebookId}/sources/text", async (
            string notebookId,
            CreateTextSourceInDto input,
            NotebookDirectoryStore notebooks,
            SourceStore sources,
            CancellationToken ct) =>
        {
            var ws = ResolveWorkspace(notebooks, notebookId);
            if (ws == null)
                return Results.NotFound(new { error = "notebook not found" });

            if (string.IsNullOrWhiteSpace(input.Title))
                return Results.BadRequest(new { error = "title is required" });
            if (string.IsNullOrWhiteSpace(input.Content))
                return Results.BadRequest(new { error = "content is required" });

            try
            {
                var meta = await sources.CreateTextAsync(
                    ws,
                    input.Title.Trim(),
                    input.Content,
                    input.MimeType,
                    ct);

                return Results.Json(new { ok = true, source = meta });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }

    private static void MapCreateFile(WebApplication app)
    {
        app.MapPost("/api/notebooks/{notebookId}/sources/file", async (
            HttpRequest request,
            string notebookId,
            NotebookDirectoryStore notebooks,
            SourceStore sources,
            CancellationToken ct) =>
        {
            var ws = ResolveWorkspace(notebooks, notebookId);
            if (ws == null)
                return Results.NotFound(new { error = "notebook not found" });

            if (!request.HasFormContentType)
                return Results.BadRequest(new { error = "multipart/form-data is required" });

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file == null)
                return Results.BadRequest(new { error = "file field is required" });

            var fileName = (file.FileName ?? string.Empty).Trim();
            if (fileName.Length == 0)
                fileName = "upload.txt";

            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            var (mimeType, allowed) = ext switch
            {
                ".txt" => ("text/plain", true),
                ".md" => ("text/markdown", true),
                ".markdown" => ("text/markdown", true),
                _ => ("", false)
            };

            if (!allowed)
                return Results.BadRequest(new { error = "only .txt/.md files are supported in MVP" });

            // Bound by SourceStore.MaxSourceChars (best-effort).
            string content;
            try
            {
                content = await ReadFormFileTextWithLimitAsync(file, SourceStore.MaxSourceChars, ct);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            var title = (form.TryGetValue("title", out var t) ? t.ToString() : string.Empty).Trim();
            if (title.Length == 0)
                title = Path.GetFileNameWithoutExtension(fileName);

            try
            {
                var meta = await sources.CreateTextAsync(ws, title, content, mimeType, ct);
                return Results.Json(new { ok = true, source = meta });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }

    private static void MapList(WebApplication app)
    {
        app.MapGet("/api/notebooks/{notebookId}/sources", async (
            string notebookId,
            NotebookDirectoryStore notebooks,
            SourceStore sources,
            CancellationToken ct) =>
        {
            var ws = ResolveWorkspace(notebooks, notebookId);
            if (ws == null)
                return Results.NotFound(new { error = "notebook not found" });

            var list = await sources.ListAsync(ws, ct);
            return Results.Json(new { count = list.Count, sources = list });
        });
    }

    private static void MapGet(WebApplication app)
    {
        app.MapGet("/api/notebooks/{notebookId}/sources/{sourceId}", async (
            string notebookId,
            string sourceId,
            NotebookDirectoryStore notebooks,
            SourceStore sources,
            CancellationToken ct) =>
        {
            var ws = ResolveWorkspace(notebooks, notebookId);
            if (ws == null)
                return Results.NotFound(new { error = "notebook not found" });

            try
            {
                var (meta, content) = await sources.GetAsync(ws, sourceId, ct);
                return Results.Json(new { meta, content });
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound(new { error = "source not found" });
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

    private static async Task<string> ReadFormFileTextWithLimitAsync(IFormFile file, int maxChars, CancellationToken ct)
    {
        if (file.Length <= 0)
            return string.Empty;

        // Guard bytes too (best-effort): UTF-8 worst-case 4 bytes/char.
        var maxBytes = (long)maxChars * 4;
        if (file.Length > maxBytes)
            throw new InvalidOperationException($"file too large: {file.Length} bytes (max {maxBytes}).");

        await using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 16 * 1024);

        var sb = new StringBuilder(capacity: Math.Min(maxChars, 16 * 1024));
        var buffer = new char[4096];
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
            if (read <= 0) break;

            var remaining = maxChars - sb.Length;
            if (remaining <= 0)
                throw new InvalidOperationException($"content too large (>{maxChars} chars)");

            sb.Append(buffer, 0, Math.Min(read, remaining));
        }

        return sb.ToString();
    }

    private sealed record CreateTextSourceInDto(string Title, string Content, string? MimeType);
}


