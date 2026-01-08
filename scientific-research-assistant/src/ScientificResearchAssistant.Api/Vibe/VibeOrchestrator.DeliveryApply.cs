using System.Text;
using System.Text.Json;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using ScientificResearchAssistant.Api.Materials;
using ScientificResearchAssistant.Api.Paper;
using ScientificResearchAssistant.Api.Sessions;
using ScientificResearchAssistant.Api.Vibe.Brief;
using ScientificResearchAssistant.Api.Vibe.Delivery;
using ScientificResearchAssistant.Api.Vibe.Dag;
using ScientificResearchAssistant.Api.Vibe.Goals;
using ScientificResearchAssistant.Api.Vibe.Trace;
using ScientificResearchAssistant.Api.Workspace;
using ScientificResearchAssistant.Contracts.Collab;

namespace ScientificResearchAssistant.Api.Vibe;

internal sealed partial class VibeOrchestrator
{
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

                idx++;
                var proposal = new PaperPatchProposal
                {
                    SessionId = session.Id,
                    PatchId = $"paper_editor_{runId}_{idx}",
                    AuthorAgent = "paper_editor",
                    TargetFile = targetFile,
                    Format = PaperPatchFormat.ReplaceSpan,
                    ReplaceStartLine = start,
                    ReplaceEndLineExclusive = endExcl,
                    ReplaceText = text,
                    CorrelationId = $"run:{runId}",
                    CreatedAt = now
                };

                try
                {
                    await _paper.ApplyPatchAsync(session.Id, runId, proposal, ct);
                    patchesApplied++;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[VibeOrchestrator] paper patch apply failed (best-effort).");
                }
            }
        }

        // 2) Write delivery lists + snapshot (best-effort)
        var delivery = parsed.Delivery;
        var changedSummary = delivery?.ChangedSummary?.Replace("\r", "").Trim();
        if (delivery != null)
        {
            var existing = await _delivery.LoadDeliverySnapshotAsync(session.Id, ct);
            var version = existing.Version + 1;
            if (version <= 0) version = 1;

            var now = Timestamp.FromDateTime(DateTime.UtcNow);

            if (delivery.Conclusions != null)
            {
                var snap = new SraConclusionCardsSnapshot { SessionId = session.Id, Version = version, UpdatedAt = now };
                var i = 0;
                foreach (var c in delivery.Conclusions.Take(32))
                {
                    if (c == null) continue;
                    var claim = (c.Claim ?? string.Empty).Replace("\r", "").Trim();
                    if (claim.Length == 0) continue;
                    i++;
                    snap.Items.Add(new SraConclusionCard
                    {
                        CardId = Bound((c.CardId ?? $"c{i}").Trim(), 80),
                        Claim = Bound(claim, 220),
                        Confidence = ParseConfidence(c.Confidence),
                        Notes = Bound((c.Notes ?? string.Empty).Replace("\r", "").Trim(), 600),
                        UpdatedAt = now
                    });

                    AddRefs(snap.Items[^1].EvidencePaths, c.EvidencePaths, 30, 240);
                    AddRefs(snap.Items[^1].CounterEvidencePaths, c.CounterEvidencePaths, 30, 240);
                    AddRefs(snap.Items[^1].RelatedDagNodeIds, c.RelatedDagNodeIds, 30, 120);
                }

                await _delivery.SaveConclusionsAsync(session.Id, snap, ct);
                listsWritten++;
            }

            if (delivery.Evidence != null)
            {
                var snap = new SraEvidenceTableSnapshot { SessionId = session.Id, Version = version, UpdatedAt = now };
                var i = 0;
                foreach (var e in delivery.Evidence.Take(80))
                {
                    if (e == null) continue;
                    var path = (e.Path ?? string.Empty).Replace("\r", "").Trim();
                    if (path.Length == 0) continue;
                    i++;
                    snap.Items.Add(new SraEvidenceItem
                    {
                        EvidenceId = Bound((e.EvidenceId ?? $"e{i}").Trim(), 80),
                        Title = Bound((e.Title ?? string.Empty).Replace("\r", "").Trim(), 160),
                        Path = path,
                        Excerpt = Bound((e.Excerpt ?? string.Empty).Replace("\r", "").Trim(), 280),
                        Relevance = Bound((e.Relevance ?? string.Empty).Replace("\r", "").Trim(), 240),
                        UpdatedAt = now
                    });
                }

                await _delivery.SaveEvidenceAsync(session.Id, snap, ct);
                listsWritten++;
            }

            if (delivery.Tasks != null)
            {
                var snap = new SraNextTasksSnapshot { SessionId = session.Id, Version = version, UpdatedAt = now };
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
                    AddRefs(item.BlockedBy, t.BlockedBy, 20, 240);
                    snap.Items.Add(item);
                }

                await _delivery.SaveTasksAsync(session.Id, snap, ct);
                listsWritten++;
            }

            // Write delivery snapshot last (paths are stable).
            var deliverySnap = new SraDeliveryCenterSnapshot
            {
                SessionId = session.Id,
                Version = version,
                PaperOutlinePath = "paper/outline.md",
                PaperDraftPath = "paper/draft.md",
                ConclusionsPath = "deliverables/conclusions.json",
                EvidencePath = "deliverables/evidence.json",
                TasksPath = "deliverables/tasks.json",
                ChangedSummary = changedSummary ?? string.Empty,
                UpdatedAt = now
            };

            await _delivery.SaveDeliverySnapshotAsync(session.Id, deliverySnap, ct);
            listsWritten++;

            // UI: notify delivery updated (best-effort)
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

    private static IReadOnlyList<string> ExtractHighlights(string text, int max)
    {
        max = Math.Clamp(max, 0, 10);
        if (max == 0) return [];

        var lines = (text ?? string.Empty)
            .Replace("\r", "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        return lines.Take(max).ToList();
    }

    private static void EmitSection(Action<string> emit, string header)
    {
        emit(header);
        if (!header.EndsWith("\n", StringComparison.Ordinal))
            emit("\n");
    }

    private sealed record DagRoundResult(
        bool Accepted,
        bool Blocked,
        SraDagMutation? Candidate,
        SraDagMutation? AcceptedMutation,
        string? StagedPath,
        IReadOnlyList<string> RedFlags,
        string? ConsensusArtifactPath);

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static string Bound(string s, int max)
    {
        var t = (s ?? string.Empty).Replace("\r", "").Trim();
        if (t.Length <= max) return t;
        return t[..max];
    }

}
