using System.Text.Json;
using Aevatar.Agents.Cognitive.Execution;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using VibeResearching.Api.Paper;
using VibeResearching.Api.Sessions;
using VibeResearching.Api.Vibe.Delivery;
using VibeResearching.Contracts.Collab;

namespace VibeResearching.Api.Vibe.Steps;

internal sealed class VibeDeliveryApplyStepModule : VibeStepModuleBase
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly PaperService _paper;
    private readonly DeliveryCenterStore _delivery;
    private readonly ResearchSessionManager _sessions;
    private readonly ILogger<VibeDeliveryApplyStepModule> _logger;

    public VibeDeliveryApplyStepModule(
        PaperService paper,
        DeliveryCenterStore delivery,
        ResearchSessionManager sessions,
        ILogger<VibeDeliveryApplyStepModule> logger)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _delivery = delivery ?? throw new ArgumentNullException(nameof(delivery));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override string Name => "vibe_delivery_apply";
    public override string StepType => "vibe_delivery_apply";

    public override async Task<PrimitiveResult> ExecuteAsync(
        IWorkflowCoordinatorRuntime coordinator,
        StepDefinition step,
        string? preRenderedPrompt,
        string? preRenderedSystem,
        CancellationToken ct)
    {
        var sessionId = ResolveSessionId(coordinator);
        if (string.IsNullOrWhiteSpace(sessionId))
            return PrimitiveResult.Fail("vibe_delivery_apply requires session_id");

        var sourceKey = ResolveStringParameter(step.Parameters, "source", "paper_editor");
        var raw = ResolveStringVar(coordinator, sourceKey);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return PrimitiveResult.Ok(new Dictionary<string, object>
            {
                ["patches_applied"] = 0,
                ["lists_written"] = 0,
                ["changed_summary"] = string.Empty,
                ["version"] = 0
            });
        }

        var session = _sessions.GetOrCreate(sessionId);
        var runId = ResolveRunId(coordinator);

        var applied = await TryApplyPaperEditorOutputAsync(session, runId, raw, ct);
        if (applied == null)
        {
            return PrimitiveResult.Ok(new Dictionary<string, object>
            {
                ["patches_applied"] = 0,
                ["lists_written"] = 0,
                ["changed_summary"] = string.Empty,
                ["version"] = 0
            });
        }

        return PrimitiveResult.Ok(new Dictionary<string, object>
        {
            ["patches_applied"] = applied.PatchesApplied,
            ["lists_written"] = applied.ListsWritten,
            ["changed_summary"] = applied.ChangedSummary ?? string.Empty,
            ["version"] = applied.Version
        });
    }

    private sealed record DeliveryUpdateResult(int PatchesApplied, int ListsWritten, string? ChangedSummary, int Version);

    private sealed class PaperEditorOutputJson
    {
        public List<PaperPatchJson>? PaperPatches { get; init; }
        public PaperEditorDeliveryJson? Delivery { get; init; }
    }

    private sealed class PaperPatchJson
    {
        public string? TargetFile { get; init; } // "outline" | "draft"
        public string? Format { get; init; } // "replace_span"
        public int? ReplaceStartLine { get; init; }
        public int? ReplaceEndLineExclusive { get; init; }
        public string? ReplaceText { get; init; }
    }

    private sealed class PaperEditorDeliveryJson
    {
        public string? ChangedSummary { get; init; }
        public List<ConclusionJson>? Conclusions { get; init; }
        public List<EvidenceJson>? Evidence { get; init; }
        public List<NextTaskJson>? Tasks { get; init; }
    }

    private sealed class ConclusionJson
    {
        public string? CardId { get; init; }
        public string? Claim { get; init; }
        public string? Confidence { get; init; } // low|medium|high
        public List<string>? EvidencePaths { get; init; }
        public List<string>? CounterEvidencePaths { get; init; }
        public List<string>? RelatedDagNodeIds { get; init; }
        public string? Notes { get; init; }
    }

    private sealed class EvidenceJson
    {
        public string? EvidenceId { get; init; }
        public string? Title { get; init; }
        public string? Path { get; init; }
        public string? Excerpt { get; init; }
        public string? Relevance { get; init; }
    }

    private sealed class NextTaskJson
    {
        public string? TaskId { get; init; }
        public string? Title { get; init; }
        public string? Detail { get; init; }
        public int? Priority { get; init; }
        public List<string>? BlockedBy { get; init; }
    }

    private async Task<DeliveryUpdateResult?> TryApplyPaperEditorOutputAsync(
        ResearchSession session,
        string runId,
        string raw,
        CancellationToken ct)
    {
        if (!TryExtractJson(raw, out var json) || string.IsNullOrWhiteSpace(json))
            return null;

        PaperEditorOutputJson? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<PaperEditorOutputJson>(json!, Json);
        }
        catch
        {
            return null;
        }

        if (parsed == null)
            return null;

        var patchesApplied = 0;
        var listsWritten = 0;

        // 1) Apply paper patches (best-effort)
        if (parsed.PaperPatches is { Count: > 0 })
        {
            var now = Timestamp.FromDateTime(DateTime.UtcNow);
            var idx = 0;
            foreach (var p in parsed.PaperPatches.Take(3))
            {
                ct.ThrowIfCancellationRequested();
                if (p == null) continue;

                var target = (p.TargetFile ?? string.Empty).Trim().ToLowerInvariant();
                var format = (p.Format ?? string.Empty).Trim().ToLowerInvariant();
                if (format.Length == 0) format = "replace_span";
                if (format != "replace_span") continue;

                var start = p.ReplaceStartLine ?? 0;
                var endExcl = p.ReplaceEndLineExclusive ?? 0;
                var text = (p.ReplaceText ?? string.Empty).Replace("\r", "");
                if (start <= 0 || endExcl <= 0 || endExcl < start) continue;
                if (text.Length == 0) continue;

                var targetFile = target switch
                {
                    "outline" => PaperTargetFile.Outline,
                    "draft" => PaperTargetFile.Draft,
                    _ => PaperTargetFile.Draft
                };

                var proposal = new PaperPatchProposal
                {
                    PatchId = $"paper_patch_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{idx++}",
                    AuthorAgent = "paper_editor",
                    CorrelationId = runId ?? string.Empty,
                    TargetFile = targetFile,
                    Format = PaperPatchFormat.ReplaceSpan,
                    ReplaceStartLine = start,
                    ReplaceEndLineExclusive = endExcl,
                    ReplaceText = text,
                    CreatedAt = now
                };

                try
                {
                    await _paper.ApplyPatchAsync(session.Id, runId, proposal, ct);
                    patchesApplied++;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Paper patch apply failed (best-effort).");
                }
            }
        }

        // 2) Delivery updates (best-effort)
        var changedSummary = string.Empty;
        if (parsed.Delivery != null)
        {
            var delivery = parsed.Delivery;
            changedSummary = Bound((delivery.ChangedSummary ?? string.Empty).Trim(), 4000);

            var (deliverySnap, conclusions, evidence, tasks) =
                await _delivery.LoadAllAsync(session.Id, ct);

            var now = Timestamp.FromDateTime(DateTime.UtcNow);
            var version = deliverySnap.Version + 1;
            if (version <= 0) version = 1;
            deliverySnap.Version = version;
            deliverySnap.ChangedSummary = changedSummary;
            deliverySnap.UpdatedAt = now;

            if (delivery.Conclusions is { Count: > 0 })
            {
                conclusions.Items.Clear();
                conclusions.Version = version;
                conclusions.UpdatedAt = now;
                var i = 0;
                foreach (var c in delivery.Conclusions.Take(32))
                {
                    if (c == null) continue;
                    var claim = (c.Claim ?? string.Empty).Replace("\r", "").Trim();
                    if (claim.Length == 0) continue;
                    i++;
                    var item = new SraConclusionCard
                    {
                        CardId = Bound((c.CardId ?? $"c{i}").Trim(), 80),
                        Claim = Bound(claim, 220),
                        Notes = Bound((c.Notes ?? string.Empty).Replace("\r", "").Trim(), 600),
                        Confidence = ParseConfidence(c.Confidence),
                        UpdatedAt = now
                    };
                    AddRefs(item.EvidencePaths, c.EvidencePaths, maxItems: 30, maxChars: 240);
                    AddRefs(item.CounterEvidencePaths, c.CounterEvidencePaths, maxItems: 30, maxChars: 240);
                    AddRefs(item.RelatedDagNodeIds, c.RelatedDagNodeIds, maxItems: 30, maxChars: 120);
                    conclusions.Items.Add(item);
                }
                await _delivery.SaveConclusionsAsync(session.Id, conclusions, ct);
                listsWritten++;
            }

            if (delivery.Evidence is { Count: > 0 })
            {
                evidence.Items.Clear();
                evidence.Version = version;
                evidence.UpdatedAt = now;
                var i = 0;
                foreach (var e in delivery.Evidence.Take(80))
                {
                    if (e == null) continue;
                    var path = (e.Path ?? string.Empty).Replace("\r", "").Trim();
                    if (path.Length == 0) continue;
                    i++;
                    evidence.Items.Add(new SraEvidenceItem
                    {
                        EvidenceId = Bound((e.EvidenceId ?? $"e{i}").Trim(), 80),
                        Title = Bound((e.Title ?? string.Empty).Replace("\r", "").Trim(), 160),
                        Path = path,
                        Excerpt = Bound((e.Excerpt ?? string.Empty).Replace("\r", "").Trim(), 280),
                        Relevance = Bound((e.Relevance ?? string.Empty).Replace("\r", "").Trim(), 240),
                        UpdatedAt = now
                    });
                }
                await _delivery.SaveEvidenceAsync(session.Id, evidence, ct);
                listsWritten++;
            }

            if (delivery.Tasks is { Count: > 0 })
            {
                tasks.Items.Clear();
                tasks.Version = version;
                tasks.UpdatedAt = now;
                var i = 0;
                foreach (var t in delivery.Tasks.Take(64))
                {
                    if (t == null) continue;
                    var title = (t.Title ?? string.Empty).Replace("\r", "").Trim();
                    if (title.Length == 0) continue;
                    i++;
                    var item = new SraNextTaskItem
                    {
                        TaskId = Bound((t.TaskId ?? $"t{i}").Trim(), 80),
                        Title = Bound(title, 160),
                        Detail = Bound((t.Detail ?? string.Empty).Replace("\r", "").Trim(), 280),
                        Priority = Math.Clamp(t.Priority ?? 0, 0, 100),
                        UpdatedAt = now
                    };
                    AddRefs(item.BlockedBy, t.BlockedBy, maxItems: 20, maxChars: 120);
                    tasks.Items.Add(item);
                }
                await _delivery.SaveTasksAsync(session.Id, tasks, ct);
                listsWritten++;
            }

            await _delivery.SaveDeliverySnapshotAsync(session.Id, deliverySnap, ct);
            listsWritten++;

            session.Events.Publish(new CustomEvent
            {
                Timestamp = NowMs(),
                Name = "aevatar.vibe.delivery_updated",
                Value = new
                {
                    sessionId = session.Id,
                    version = deliverySnap.Version,
                    patchesApplied,
                    listsWritten,
                    changedSummary = deliverySnap.ChangedSummary
                }
            });

            return new DeliveryUpdateResult(patchesApplied, listsWritten, deliverySnap.ChangedSummary, deliverySnap.Version);
        }

        if (patchesApplied == 0 && listsWritten == 0)
            return null;

        return new DeliveryUpdateResult(patchesApplied, listsWritten, changedSummary, Version: 0);
    }

    private static void AddRefs(RepeatedField<string> dst, List<string>? src, int maxItems, int maxChars)
    {
        if (src == null || src.Count == 0) return;
        maxItems = Math.Clamp(maxItems, 0, 500);
        maxChars = Math.Clamp(maxChars, 20, 2000);
        foreach (var s in src)
        {
            var v = (s ?? string.Empty).Replace("\r", "").Trim();
            if (v.Length == 0) continue;
            dst.Add(v.Length <= maxChars ? v : v[..maxChars]);
            if (dst.Count >= maxItems) break;
        }
    }

    private static SraConfidenceLevel ParseConfidence(string? value)
    {
        var s = (value ?? string.Empty).Trim().ToLowerInvariant();
        return s switch
        {
            "low" => SraConfidenceLevel.Low,
            "medium" => SraConfidenceLevel.Medium,
            "high" => SraConfidenceLevel.High,
            _ => SraConfidenceLevel.Unspecified
        };
    }

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static string Bound(string? s, int max)
    {
        var t = (s ?? string.Empty).Replace("\r", "").Trim();
        if (t.Length <= max) return t;
        return t[..max];
    }
}
