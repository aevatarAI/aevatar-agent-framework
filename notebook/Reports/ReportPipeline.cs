using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.AI;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;

namespace Aevatar.Notebook.Reports;

// ============================================================
//  ReportPipeline (MVP)
//
//  Goal:
//  - Generate a structured report via multi-step prompting:
//    outline -> draft -> refine
//  - Persist each generated version as MemoryEntry under memoryId "report::<reportId>"
//
//  Notes:
//  - Prompting is designed to be deterministic-ish via stage hints.
//  - Persistence is append-only; version is derived from existing entries.
//  - Best-effort: persistence failures must not crash report generation.
// ============================================================
internal sealed class ReportPipeline
{
    public const string NotebookContextKey = "notebook_context";

    private readonly IMemoryStore _store;
    private readonly ILogger<ReportPipeline> _logger;

    public ReportPipeline(IMemoryStore store, ILogger<ReportPipeline> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ReportPipelineResult> GenerateAsync(
        ReportPipelineRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var topic = (request.Topic ?? string.Empty).Trim();
        if (topic.Length == 0)
            topic = "Untitled Report";

        var reportId = (request.ReportId ?? string.Empty).Trim();
        if (reportId.Length == 0)
            reportId = Guid.NewGuid().ToString("N")[..12];

        var memoryId = $"report::{reportId}";
        var version = await NextVersionAsync(memoryId, ct);

        // ------------------------------------------------------------
        //  Multi-step prompting (context is injected via ChatRequest.Context)
        // ------------------------------------------------------------
        var outline = await AskAsync(
            request.ChatAsync,
            message:
            $"""
             Generate a report OUTLINE only.

             Topic: {topic}

             Requirements:
             - Use the Notebook context as the only source of truth.
             - Provide a clear section outline (H2/H3) and bullet points.
             - When citing, refer to sourceIds like [source:abc123...].
             """.Trim(),
            stageHint: "report:outline",
            request.NotebookContext,
            request.ExecutionId,
            ct);

        var draft = await AskAsync(
            request.ChatAsync,
            message:
            $"""
             Write the FULL report draft in Markdown.

             Topic: {topic}

             Outline:
             {outline}

             Requirements:
             - Use the Notebook context as the only source of truth.
             - Include an executive summary.
             - Include 3-8 bullet key points.
             - Include citations by referring to sourceIds like [source:abc123...].
             """.Trim(),
            stageHint: "report:draft",
            request.NotebookContext,
            request.ExecutionId,
            ct);

        var final = await AskAsync(
            request.ChatAsync,
            message:
            $"""
             Refine the report for clarity and accuracy.

             Topic: {topic}

             Draft:
             {draft}

             Requirements:
             - Keep it concise and structured.
             - Ensure claims are grounded in Notebook context.
             - Ensure citations exist when making factual claims.
             """.Trim(),
            stageHint: "report:refine",
            request.NotebookContext,
            request.ExecutionId,
            ct);

        // ------------------------------------------------------------
        //  Persist report version (best-effort)
        // ------------------------------------------------------------
        await PersistBestEffortAsync(
            reportId,
            version,
            topic,
            request.SourceIds,
            request.CitationChunkIds,
            final,
            ct);

        return new ReportPipelineResult
        {
            ReportId = reportId,
            Version = version,
            Topic = topic,
            Content = final,
            Outline = outline,
            Draft = draft
        };
    }

    // ============================================================
    //  Streaming Report Generation (MVP)
    //
    //  WHY:
    //  - Report is multi-step and can take minutes. Non-streaming UX looks "stuck".
    //  - We stream stage progress + tokens to UI via /api/report/stream (NDJSON).
    //
    //  STAGES:
    //  - outline -> draft -> refine
    // ============================================================
    public async Task<ReportPipelineResult> GenerateStreamingAsync(
        ReportPipelineRequest request,
        Func<ChatRequest, CancellationToken, IAsyncEnumerable<string>> chatStreamAsync,
        Func<ReportPipelineStreamEvent, CancellationToken, Task> onEvent,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(chatStreamAsync);
        ArgumentNullException.ThrowIfNull(onEvent);
        ct.ThrowIfCancellationRequested();

        var topic = (request.Topic ?? string.Empty).Trim();
        if (topic.Length == 0)
            topic = "Untitled Report";

        var reportId = (request.ReportId ?? string.Empty).Trim();
        if (reportId.Length == 0)
            reportId = Guid.NewGuid().ToString("N")[..12];

        var memoryId = $"report::{reportId}";
        var version = await NextVersionAsync(memoryId, ct);

        async Task EmitAsync(ReportPipelineStreamEvent evt)
        {
            try
            {
                await onEvent(evt, ct);
            }
            catch
            {
                // Best-effort: never fail report generation because UI stream failed.
            }
        }

        var outline = await AskStreamStageAsync(
            chatStreamAsync,
            stage: "outline",
            stageHint: "report:outline",
            message:
            $"""
             Generate a report OUTLINE only.

             Topic: {topic}

             Requirements:
             - Use the Notebook context as the only source of truth.
             - Provide a clear section outline (H2/H3) and bullet points.
             - When citing, refer to sourceIds like [source:abc123...].
             """.Trim(),
            notebookContext: request.NotebookContext,
            executionId: request.ExecutionId,
            emit: EmitAsync,
            ct);

        var draft = await AskStreamStageAsync(
            chatStreamAsync,
            stage: "draft",
            stageHint: "report:draft",
            message:
            $"""
             Write the FULL report draft in Markdown.

             Topic: {topic}

             Outline:
             {outline}

             Requirements:
             - Use the Notebook context as the only source of truth.
             - Include an executive summary.
             - Include 3-8 bullet key points.
             - Include citations by referring to sourceIds like [source:abc123...].
             """.Trim(),
            notebookContext: request.NotebookContext,
            executionId: request.ExecutionId,
            emit: EmitAsync,
            ct);

        var final = await AskStreamStageAsync(
            chatStreamAsync,
            stage: "refine",
            stageHint: "report:refine",
            message:
            $"""
             Refine the report for clarity and accuracy.

             Topic: {topic}

             Draft:
             {draft}

             Requirements:
             - Keep it concise and structured.
             - Ensure claims are grounded in Notebook context.
             - Ensure citations exist when making factual claims.
             """.Trim(),
            notebookContext: request.NotebookContext,
            executionId: request.ExecutionId,
            emit: EmitAsync,
            ct);

        // Persist report version (best-effort)
        await PersistBestEffortAsync(
            reportId,
            version,
            topic,
            request.SourceIds,
            request.CitationChunkIds,
            final,
            ct);

        await EmitAsync(new ReportPipelineStreamEvent
        {
            Type = "saved",
            Stage = "refine",
            ReportId = reportId,
            Version = version,
            Chars = final.Length
        });

        return new ReportPipelineResult
        {
            ReportId = reportId,
            Version = version,
            Topic = topic,
            Content = final,
            Outline = outline,
            Draft = draft
        };
    }

    private async Task<string> AskAsync(
        Func<ChatRequest, CancellationToken, Task<ChatResponse>> chatAsync,
        string message,
        string stageHint,
        string notebookContext,
        string? executionId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(chatAsync);

        var req = new ChatRequest
        {
            Message = message,
            RequestId = Guid.NewGuid().ToString("N"),
            StageHint = stageHint
        };

        if (!string.IsNullOrWhiteSpace(executionId))
            req.Context["execution_id"] = executionId.Trim();

        req.Context[NotebookContextKey] = notebookContext ?? string.Empty;

        var resp = await chatAsync(req, ct);
        return (resp.Content ?? string.Empty).Trim();
    }

    private async Task<string> AskStreamStageAsync(
        Func<ChatRequest, CancellationToken, IAsyncEnumerable<string>> chatStreamAsync,
        string stage,
        string stageHint,
        string message,
        string notebookContext,
        string? executionId,
        Func<ReportPipelineStreamEvent, Task> emit,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(chatStreamAsync);
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(stageHint);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(emit);

        var req = new ChatRequest
        {
            Message = message,
            RequestId = Guid.NewGuid().ToString("N"),
            StageHint = stageHint
        };

        if (!string.IsNullOrWhiteSpace(executionId))
            req.Context["execution_id"] = executionId.Trim();

        req.Context[NotebookContextKey] = notebookContext ?? string.Empty;

        var sb = new StringBuilder(capacity: 2048);
        var sw = Stopwatch.StartNew();

        await emit(new ReportPipelineStreamEvent { Type = "stage_start", Stage = stage });

        try
        {
            await foreach (var chunk in chatStreamAsync(req, ct))
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(chunk))
                    continue;

                sb.Append(chunk);
                await emit(new ReportPipelineStreamEvent { Type = "stage_delta", Stage = stage, Content = chunk });
            }
        }
        finally
        {
            sw.Stop();
        }

        var text = sb.ToString().Trim();
        await emit(new ReportPipelineStreamEvent
        {
            Type = "stage_end",
            Stage = stage,
            DurationMs = (long)sw.Elapsed.TotalMilliseconds,
            Chars = text.Length
        });

        return text;
    }

    private async Task<int> NextVersionAsync(string reportMemoryId, CancellationToken ct)
    {
        try
        {
            var entries = await _store.ListEntriesAsync(reportMemoryId, limit: 2000, ct: ct);
            var max = 0;
            foreach (var e in entries)
            {
                if (e == null) continue;
                if (!string.Equals(e.Role, "report", StringComparison.Ordinal)) continue;
                if (!e.Tags.TryGetValue("version", out var v)) continue;
                if (!int.TryParse(v, out var i)) continue;
                if (i > max) max = i;
            }
            return max + 1;
        }
        catch
        {
            // Best-effort: default to v1 if store read fails.
            return 1;
        }
    }

    private async Task PersistBestEffortAsync(
        string reportId,
        int version,
        string topic,
        IReadOnlyList<string> sourceIds,
        IReadOnlyList<string> citationChunkIds,
        string content,
        CancellationToken ct)
    {
        try
        {
            var memoryId = $"report::{reportId}";
            var scope = new MemoryScope { Type = MemoryScopeType.Graph, ScopeId = reportId };
            var now = Timestamp.FromDateTime(DateTime.UtcNow);

            var entry = new MemoryEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                MemoryId = memoryId,
                Scope = scope,
                Role = "report",
                Content = (content ?? string.Empty).Trim(),
                CreatedAt = now
            };

            entry.Tags["report_id"] = reportId;
            entry.Tags["version"] = version.ToString();
            entry.Tags["topic"] = topic;

            // Keep tags bounded.
            entry.Tags["source_ids"] = JoinBounded(sourceIds, 50);
            entry.Tags["citation_chunk_ids"] = JoinBounded(citationChunkIds, 100);

            await _store.AppendAsync(entry, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[Notebook] Failed to persist report (best-effort).");
        }
    }

    private static string JoinBounded(IReadOnlyList<string> ids, int maxItems)
    {
        if (ids == null || ids.Count == 0)
            return string.Empty;

        var take = Math.Clamp(maxItems, 0, 500);
        var list = ids
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => x.Length > 0)
            .Take(take);

        return string.Join(",", list);
    }
}

internal sealed record ReportPipelineStreamEvent
{
    public required string Type { get; init; } // stage_start | stage_delta | stage_end | saved
    public string Stage { get; init; } = string.Empty; // outline | draft | refine

    public string? Content { get; init; }
    public long? DurationMs { get; init; }
    public int? Chars { get; init; }

    // Optional (only on saved)
    public string? ReportId { get; init; }
    public int? Version { get; init; }
}

internal sealed record ReportPipelineRequest
{
    public required Func<ChatRequest, CancellationToken, Task<ChatResponse>> ChatAsync { get; init; }
    public required string NotebookContext { get; init; }
    public required IReadOnlyList<string> SourceIds { get; init; }
    public required IReadOnlyList<string> CitationChunkIds { get; init; }

    public string? Topic { get; init; }
    public string? ReportId { get; init; }

    // Optional correlation id used for trace/memory linking (if upstream provides one).
    public string? ExecutionId { get; init; }
}

internal sealed record ReportPipelineResult
{
    public required string ReportId { get; init; }
    public required int Version { get; init; }
    public required string Topic { get; init; }
    public required string Content { get; init; }

    // Debug-grade intermediate results (useful for trace/replay; not necessarily returned to UI).
    public string Outline { get; init; } = string.Empty;
    public string Draft { get; init; } = string.Empty;
}


