using System.IO;
using System.Text;
using System.Text.Json;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.Cognitive.Researching.Sessions;
using Aevatar.Agents.Cognitive.Researching.Mesh;

namespace Aevatar.Agents.Cognitive.Researching.Round;

public sealed partial class ResearchingRoundServices
{
    // ============================================================
    //  Mesh integration helpers (Option B)
    // ============================================================

    private void HandleMeshErrors(
        ResearchSession session,
        Action<string> emit,
        string kind,
        IReadOnlyList<Aevatar.CognitiveMesh.Dsl.Validation.DslValidationError> errors,
        IDictionary<string, string> outputs)
    {
        errors ??= Array.Empty<Aevatar.CognitiveMesh.Dsl.Validation.DslValidationError>();

        // Publish a best-effort event for observability.
        try
        {
            session.Events.Publish(new CustomEvent
            {
                Timestamp = NowMs(),
                Name = "aevatar.vibe.mesh_error",
                Value = new
                {
                    sessionId = session.Id,
                    kind,
                    count = errors.Count
                }
            });
        }
        catch
        {
            // best-effort only
        }

        var rendered = RenderMeshErrors(errors);
        outputs["mesh_error_kind"] = kind;
        outputs["mesh_error"] = rendered;

        EmitSection(emit, "### Mesh orchestration failed; worker phase skipped\n");
        emit(Bound(rendered, 2500) + "\n\n");
    }

    private static string RenderMeshErrors(IReadOnlyList<Aevatar.CognitiveMesh.Dsl.Validation.DslValidationError> errors)
    {
        if (errors is not { Count: > 0 })
            return "_(no errors)_";

        var sb = new StringBuilder(1024);
        sb.AppendLine("Errors:");
        foreach (var e in errors.Take(20))
        {
            var code = (e.Code ?? string.Empty).Trim();
            var path = (e.Path ?? string.Empty).Trim();
            var msg = (e.Message ?? string.Empty).Trim();
            if (code.Length == 0) code = "error";
            if (path.Length > 0)
                sb.Append("- ").Append(code).Append(" @ ").Append(path).Append(": ").AppendLine(msg);
            else
                sb.Append("- ").Append(code).Append(": ").AppendLine(msg);
        }

        if (errors.Count > 20)
            sb.AppendLine($"... ({errors.Count - 20} more)");

        return Bound(sb.ToString().Trim(), 8000);
    }

    private void TryWriteMeshRunArtifacts(
        string sessionId,
        string runId,
        string rawMeshJson,
        MeshExecutionPlan plan,
        MeshRunResult run)
    {
        try
        {
            var ws = _core.Workspace.EnsureSessionWorkspace(sessionId);
            var dir = Path.Combine(ws.ArtifactsDir, "mesh");
            Directory.CreateDirectory(dir);

            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");

            var meshPath = Path.Combine(dir, $"{runId}_{stamp}.mesh.json");
            File.WriteAllText(meshPath, rawMeshJson, Encoding.UTF8);

            var planPath = Path.Combine(dir, $"{runId}_{stamp}.plan.json");
            File.WriteAllText(planPath, JsonSerializer.Serialize(plan, Json), Encoding.UTF8);

            var outputsPath = Path.Combine(dir, $"{runId}_{stamp}.outputs.json");
            File.WriteAllText(outputsPath, JsonSerializer.Serialize(run, Json), Encoding.UTF8);
        }
        catch
        {
            // best-effort only
        }
    }
}


