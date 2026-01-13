using System.Text.Json;
using Aevatar.Agents.AGUI;
using Microsoft.Extensions.Hosting;
using ScientificResearchAssistant.Api.Sessions;
using ScientificResearchAssistant.Api.Vibe.Mesh;

namespace ScientificResearchAssistant.Api.Vibe;

internal sealed partial class VibeOrchestrator
{
    // ============================================================
    //  Mesh seed (Option B)
    //
    //  Why:
    //  - Mesh orchestration is now enabled by default.
    //  - To make the change meaningful and discoverable, we auto-seed a valid
    //    mesh.json when missing (equivalent to the legacy default pipeline).
    //
    //  Rules:
    //  - Never overwrite an existing mesh.json
    //  - Seed must be acyclic (planner forbids cycles)
    // ============================================================

    private async Task<string?> TryLoadOrSeedMeshAsync(
        ResearchSession session,
        string runId,
        CancellationToken ct)
    {
        var (raw, _) = await _meshStore.TryLoadRawAsync(session.Id, ct);
        if (!string.IsNullOrWhiteSpace(raw))
            return raw;

        // Seed from repo template YAML (preferred).
        var seeded = TryReadDefaultMeshTemplateYaml() ?? BuildDefaultSeedMeshYamlFallback();
        try
        {
            await _meshStore.SaveAsync(session.Id, seeded, format: "yaml", ct);

            session.Events.Publish(new CustomEvent
            {
                Timestamp = NowMs(),
                Name = "aevatar.vibe.mesh_seeded",
                Value = new { sessionId = session.Id, runId }
            });

            return seeded;
        }
        catch
        {
            // If we cannot write (permissions, disk issues), keep best-effort behavior:
            // caller will fallback or fail-fast depending on options.
            return null;
        }
    }

    private string? TryReadDefaultMeshTemplateYaml()
    {
        try
        {
            var path = Path.Combine(_env.ContentRootPath, "Vibe", "Mesh", "default_mesh.yaml");
            if (!File.Exists(path))
                return null;
            return (File.ReadAllText(path) ?? string.Empty).Replace("\r", "").Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string BuildDefaultSeedMeshYamlFallback()
    {
        // Last-resort fallback (keep it minimal and valid).
        return """
               dsl_version: "0.1"
               goal: { name: "vibe default pipeline (seeded)" }
               strategy: "cot"
               budget: { max_steps: 30, token_limit: 20000 }
               nodes:
                 - { id: planner, type: planner }
                 - { id: reasoner, type: reasoner }
                 - { id: librarian, type: librarian }
                 - { id: dag_builder, type: dag_builder }
               edges:
                 - { from: planner, to: reasoner, channel: upstream_output }
                 - { from: reasoner, to: librarian, channel: upstream_output }
                 - { from: librarian, to: dag_builder, channel: upstream_output }
                 - { from: planner, to: dag_builder, channel: dag_snapshot }
               constraints: []
               """.Replace("\r", "").Trim();
    }
}


