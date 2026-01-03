using System.Text;
using System.Text.Json;
using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Notebook.Agents;
using Aevatar.Notebook.Context;
using Aevatar.Notebook.Streaming;
using Aevatar.Notebook.Tracing;
using Microsoft.Extensions.Options;

namespace Aevatar.Notebook.Reports;

// ============================================================
//  Report Streaming API (MVP)
//
//  Endpoint:
//  - POST /api/report/stream : generate report with NDJSON progress
//
//  NDJSON events (one JSON per line):
//  - meta        : reportId/executionId/context summary
//  - stage_start : outline|draft|refine
//  - stage_delta : token chunk (per stage)
//  - stage_end   : durationMs/chars (per stage)
//  - tool_start  : (via NotebookStreamEventContext) tool progress
//  - tool_end    : (via NotebookStreamEventContext) tool progress
//  - saved       : persisted report id + version + viewerUrl
//  - error       : error message
//  - done        : stream finished
//
//  Notes:
//  - Best-effort: if client disconnects, generation may be canceled via ct.
//  - Any streaming write failure must NOT crash report generation.
// ============================================================
internal static class ReportStreamApi
{
    public static void MapReportStreamApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/api/report/stream", async (
            HttpContext http,
            ReportInDto input,
            NotebookRuntime runtime,
            NotebookContextBuilder contextBuilder,
            ReportPipeline pipeline,
            IExecutionTraceStore traceStore,
            IOptions<LLMProvidersConfig> llm,
            ILogger<NotebookRuntime> logger,
            CancellationToken ct) =>
        {
            var topic = string.IsNullOrWhiteSpace(input.Topic) ? "Untitled Report" : input.Topic.Trim();
            var reportId = string.IsNullOrWhiteSpace(input.ReportId)
                ? Guid.NewGuid().ToString("N")[..12]
                : input.ReportId.Trim();

            var startedAtUtc = DateTime.UtcNow;
            var executionId = $"notebook-report-{Guid.NewGuid():N}";

            ReportPipelineResult? report = null;
            NotebookContextBuildResult? ctxResult = null;
            string? error = null;

            string? agentId = null;

            // Pre-create JSON options (camelCase) so frontend can parse consistent fields.
            var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

            // ------------------------------------------------------------
            //  Start streaming response ASAP
            // ------------------------------------------------------------
            http.Response.StatusCode = StatusCodes.Status200OK;
            http.Response.Headers.ContentType = "application/x-ndjson; charset=utf-8";
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers.Pragma = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";

            await http.Response.StartAsync(ct);

            await using var writer = new StreamWriter(
                http.Response.Body,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 16 * 1024,
                leaveOpen: true);

            var writeLock = new SemaphoreSlim(1, 1);

            async Task SafeWriteNdjsonAsync(object payload, CancellationToken cancellationToken)
            {
                try
                {
                    var line = JsonSerializer.Serialize(payload, json);
                    await writeLock.WaitAsync(cancellationToken);
                    try
                    {
                        await writer.WriteLineAsync(line);
                        await writer.FlushAsync();
                    }
                    finally
                    {
                        writeLock.Release();
                    }
                }
                catch
                {
                    // Best-effort: ignore write failures.
                }
            }

            try
            {
                var (agent, aid) = await runtime.GetAgentAsync(ct);
                agentId = aid;

                ctxResult = await contextBuilder.BuildAsync(
                    new NotebookContextBuildRequest
                    {
                        Query = topic,
                        SelectedSourceIds = input.SelectedSourceIds
                    },
                    ct: ct);

                var chunkIds = ctxResult.Context.Slices
                    .Where(s => s.Kind == Aevatar.Notebook.Contracts.NotebookContextSliceKind.SourceChunk)
                    .Select(s => (s.ChunkId ?? string.Empty).Trim())
                    .Where(s => s.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                var citations = ctxResult.Context.Slices
                    .Where(s => s.Kind == Aevatar.Notebook.Contracts.NotebookContextSliceKind.SourceChunk)
                    .Select(s => new
                    {
                        sourceId = s.SourceId,
                        chunkId = s.ChunkId,
                        score = s.Score,
                        reason = s.Reason,
                        preview = string.IsNullOrWhiteSpace(s.Content) ? "" : (s.Content.Length <= 180 ? s.Content : s.Content[..180])
                    })
                    .ToList();

                // 1) meta
                await SafeWriteNdjsonAsync(new
                {
                    type = "meta",
                    agentId = agentId ?? string.Empty,
                    executionId,
                    reportId,
                    topic,
                    context = new
                    {
                        sourceIds = ctxResult.SourceIds,
                        strategy = ctxResult.Context.Tags.TryGetValue("strategy", out var st) ? st : "",
                        chunkSlices = citations.Count,
                        renderedChars = (ctxResult.Rendered ?? string.Empty).Length
                    },
                    citations,
                    stages = new[] { "outline", "draft", "refine" }
                }, ct);

                // Bind tool progress events to this streaming request (best-effort).
                var prevSink = NotebookStreamEventContext.Current;
                NotebookStreamEventContext.Current = new NdjsonNotebookStreamEventSink(writer, json, writeLock);
                try
                {
                    // 2) pipeline stages (stream)
                    report = await pipeline.GenerateStreamingAsync(
                        new ReportPipelineRequest
                        {
                            ChatAsync = agent.ChatAsync,
                            NotebookContext = ctxResult.Rendered ?? string.Empty,
                            SourceIds = ctxResult.SourceIds,
                            CitationChunkIds = chunkIds,
                            Topic = topic,
                            ReportId = reportId,
                            ExecutionId = executionId
                        },
                        chatStreamAsync: agent.ChatStreamAsync,
                        onEvent: async (evt, token) =>
                        {
                            // Enrich with viewerUrl on saved event.
                            if (string.Equals(evt.Type, "saved", StringComparison.Ordinal))
                            {
                                await SafeWriteNdjsonAsync(new
                                {
                                    type = evt.Type,
                                    stage = evt.Stage,
                                    reportId = evt.ReportId,
                                    version = evt.Version,
                                    chars = evt.Chars,
                                    viewerUrl = $"/report.html?reportId={Uri.EscapeDataString(reportId)}"
                                }, token);
                                return;
                            }

                            await SafeWriteNdjsonAsync(new
                            {
                                type = evt.Type,
                                stage = evt.Stage,
                                content = evt.Content,
                                durationMs = evt.DurationMs,
                                chars = evt.Chars
                            }, token);
                        },
                        ct);
                }
                finally
                {
                    NotebookStreamEventContext.Current = prevSink;
                }

                // 3) done
                await SafeWriteNdjsonAsync(new { type = "done" }, ct);
            }
            catch (OperationCanceledException ex)
            {
                var providerName = string.IsNullOrWhiteSpace(llm.Value.Default) ? "default" : llm.Value.Default;
                llm.Value.Providers.TryGetValue(providerName, out var cfg);
                var timeoutMs = cfg?.TimeoutMilliseconds ?? 0;

                error =
                    $"LLM 请求超时/被取消（provider={providerName}, model={cfg?.Model ?? ""}, timeoutMs={timeoutMs}）。" +
                    "请检查网络/Endpoint 是否可达，或在配置里调大 `LLMProviders:Providers:<provider>:TimeoutMilliseconds`。";

                logger.LogWarning(ex, "[Notebook] Report(stream) canceled/timeout: {Message}", ex.Message);

                await SafeWriteNdjsonAsync(new { type = "error", error }, ct);
                await SafeWriteNdjsonAsync(new { type = "done" }, ct);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                logger.LogError(ex, "[Notebook] Report(stream) failed: {Message}", ex.Message);

                await SafeWriteNdjsonAsync(new { type = "error", error }, ct);
                await SafeWriteNdjsonAsync(new { type = "done" }, ct);
            }
            finally
            {
                var endedAtUtc = DateTime.UtcNow;

                // Best-effort trace save (auto graph projection)
                try
                {
                    var trace = NotebookTraceBuilder.BuildReportTrace(new NotebookReportTraceData
                    {
                        ExecutionId = executionId,
                        AgentId = agentId ?? string.Empty,
                        ReportId = report?.ReportId ?? reportId,
                        Version = report?.Version ?? 0,
                        Topic = topic,
                        SourceIds = ctxResult?.SourceIds ?? new List<string>(),
                        Context = ctxResult?.Context,
                        RenderedContext = ctxResult?.Rendered,
                        Outline = report?.Outline ?? string.Empty,
                        Draft = report?.Draft ?? string.Empty,
                        Content = report?.Content ?? string.Empty,
                        Error = error,
                        LlmProvider = llm.Value.Default,
                        StartedAtUtc = startedAtUtc,
                        EndedAtUtc = endedAtUtc
                    });

                    await traceStore.SaveAsync(trace, ct);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "[Notebook] Report(stream) trace save failed (best-effort).");
                }
            }
        });
    }
}


