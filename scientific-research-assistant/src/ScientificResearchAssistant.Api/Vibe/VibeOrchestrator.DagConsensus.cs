using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    // ============================================================
    //  DAG consensus + persistence
    // ============================================================

    private static readonly Regex SourceRefRegex =
        new(@"(?:(?:\bsource:)|(?:\bsources/))(?<rel>[a-zA-Z0-9_\-./]+?\.(?:md|txt))", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private async Task<DagRoundResult> RunDagApplyAsync(
        ResearchSession session,
        string runId,
        string question,
        MaterialsSnapshot materials,
        SraDagSnapshot currentDag,
        IReadOnlyDictionary<string, string> outputs,
        Action<string> emit,
        CancellationToken ct)
    {
        // Parse candidate mutation from dag_builder output (JSON).
        var candidateText = outputs.TryGetValue("dag_builder", out var x) ? x : string.Empty;
        var candidate = TryParseDagBuilderCandidate(session.Id, candidateText);

        if (candidate == null)
        {
            EmitSection(emit, "### DAG Apply (no verification)\n");
            emit("_No DAG candidate produced._\n\n");
            return new DagRoundResult(false, false, null, null, null, [], null);
        }

        // Before apply: auto-create placeholder sources for any referenced-but-missing files.
        // Even without verifiers, this keeps the workspace consistent and avoids broken references.
        materials = await TryEnsureSourcePlaceholdersAsync(session.Id, question, materials, candidate, emit, ct);

        // EMPTY mutation means "no change" (do not stage / do not block).
        if (candidate.UpsertNodes.Count == 0 && candidate.UpsertEdges.Count == 0)
        {
            EmitSection(emit, "### DAG Apply (no verification)\n");
            emit("_No DAG changes proposed._\n\n");
            return new DagRoundResult(false, false, candidate, null, null, [], null);
        }

        // ------------------------------------------------------------
        // No verification / no consensus:
        // - Apply candidate directly.
        // - Future: we can attach a verifier pass that annotates nodes, but not block writes.
        // ------------------------------------------------------------
        try
        {
            var dagId = session.EffectiveDagId;
            var applied = await _core.Dag.ApplyMutationAsync(dagId, candidate, ct);

            session.Events.Publish(new CustomEvent
            {
                Timestamp = NowMs(),
                Name = "aevatar.vibe.dag_updated",
                Value = new
                {
                    sessionId = session.Id,
                    dagId,
                    runId,
                    mutationId = candidate.MutationId,
                    nodes = candidate.UpsertNodes.Count,
                    edges = candidate.UpsertEdges.Count,
                    updatedAt = applied.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? ""
                }
            });

            EmitSection(emit, "### DAG Apply (no verification)\n");
            emit($"**Applied** (mutationId: `{candidate.MutationId}`)\n\n");

            return new DagRoundResult(true, false, candidate, candidate, null, [], null);
        }
        catch (Exception ex)
        {
            EmitSection(emit, "### DAG Apply (no verification)\n");
            emit($"[dag apply error] {ex.Message}\n\n");
            return new DagRoundResult(false, true, candidate, null, null, ["apply_exception"], null);
        }
    }

    private async Task<MaterialsSnapshot> TryEnsureSourcePlaceholdersAsync(
        string sessionId,
        string question,
        MaterialsSnapshot materials,
        SraDagMutation candidate,
        Action<string> emit,
        CancellationToken ct)
    {
        try
        {
            var referenced = ExtractReferencedSourceRelPaths(candidate);
            if (referenced.Count == 0)
                return materials;

            var existing = new HashSet<string>(
                materials.Sources.Select(s => (s.RelativePath ?? string.Empty).Trim()),
                StringComparer.OrdinalIgnoreCase);

            var missing = referenced
                .Where(r => !string.IsNullOrWhiteSpace(r) && !existing.Contains(r))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(24)
                .ToList();

            if (missing.Count == 0)
                return materials;

            var created = new List<string>(capacity: missing.Count);
            foreach (var rel in missing)
            {
                ct.ThrowIfCancellationRequested();

                var content = BuildPlaceholderSource(rel);
                try
                {
                    await _core.Materials.SaveSourceAsync(
                        title: InferTitleForPlaceholder(rel),
                        content: content,
                        relativePath: rel,
                        ct);
                    created.Add($"sources/{rel}".Replace('\\', '/').Trim('/'));
                }
                catch
                {
                    // If writes are disabled (Materials:AllowWrite=false) or path is unsafe, keep going.
                }
            }

            if (created.Count > 0)
            {
                EmitSection(emit, "### Librarian auto-filled missing sources (placeholders)\n");
                foreach (var p in created)
                    emit($"- `{p}`\n");
                emit("\n");

                // Refresh materials so verifiers see the newly created sources in the MATERIAL INDEX.
                return await _core.Materials.LoadAsync(sessionId, query: question, ct);
            }

            return materials;
        }
        catch
        {
            // best-effort: never break the run because of missing sources handling
            return materials;
        }
    }

    private static HashSet<string> ExtractReferencedSourceRelPaths(SraDagMutation candidate)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Scan(string? text)
        {
            var s = (text ?? string.Empty).Replace("\r", "");
            if (s.Length == 0) return;

            foreach (Match m in SourceRefRegex.Matches(s))
            {
                var rel = (m.Groups["rel"].Value ?? string.Empty).Replace('\\', '/').Trim('/');
                if (string.IsNullOrWhiteSpace(rel))
                    continue;
                // Reject traversal segments early; MaterialsService will also enforce safety.
                if (rel.Contains("..", StringComparison.Ordinal))
                    continue;
                set.Add(rel);
            }
        }

        foreach (var n in candidate.UpsertNodes)
        {
            if (n == null) continue;
            Scan(n.Label);
            Scan(n.Proof);
            foreach (var kv in n.Tags)
            {
                Scan(kv.Key);
                Scan(kv.Value);
            }
        }

        foreach (var e in candidate.UpsertEdges)
        {
            if (e == null) continue;
            Scan(e.Type);
        }

        return set;
    }

    private static string InferTitleForPlaceholder(string rel)
    {
        var name = Path.GetFileNameWithoutExtension(rel ?? string.Empty) ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return "Placeholder source";
        return $"Placeholder: {name}";
    }

    private static string BuildPlaceholderSource(string rel)
    {
        var p = (rel ?? string.Empty).Replace('\\', '/').Trim('/');
        return $"""
               # Placeholder source

               This file was auto-created because a DAG candidate referenced it but it did not exist under `sources/` at runtime.

               ## Status
               - grounded: NO
               - action: replace this placeholder with real, verifiable source material (quotes / citations / links to papers).

               ## How to fix
               - Put the actual referenced material here, or upload/copy it into `sources/{p}`.
               - Keep it concise and cite where it comes from (paper name + section/page).
               """;
    }

    private async Task<int> PredictNextRoundIndexAsync(string sessionId, CancellationToken ct)
    {
        try
        {
            var prev = await _core.Trace.LoadLatestAsync(sessionId, max: 1, ct);
            return prev.Count == 0 ? 0 : prev[^1].RoundIndex + 1;
        }
        catch
        {
            return 0;
        }
    }

}
