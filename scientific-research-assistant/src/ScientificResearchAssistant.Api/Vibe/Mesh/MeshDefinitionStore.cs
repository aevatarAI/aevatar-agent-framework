using System.Text;
using ScientificResearchAssistant.Api.Workspace;

namespace ScientificResearchAssistant.Api.Vibe.Mesh;

// ============================================================
//  MeshDefinitionStore (File-SSoT)
//
//  Canonical storage:
//    workspace/sessions/{sessionId}/decisions/mesh.yaml   (preferred)
//    workspace/sessions/{sessionId}/decisions/mesh.json   (also supported)
//
//  Audit artifacts (best-effort):
//    workspace/sessions/{sessionId}/artifacts/mesh/*.{yaml|json}
//
//  Notes:
//  - The store is intentionally "raw text" only (YAML/JSON). Compilation/validation is done elsewhere.
//  - Writes are atomic: write to session tmp then move(override) into decisions/.
// ============================================================

public sealed class MeshDefinitionStore
{
    private const string FileJson = "mesh.json";
    private const string FileYaml = "mesh.yaml";
    private const string FileYml = "mesh.yml";
    private const int MaxChars = 500_000; // safety bound (raw JSON)

    private readonly WorkspaceService _workspace;
    private readonly ILogger<MeshDefinitionStore> _logger;

    public MeshDefinitionStore(WorkspaceService workspace, ILogger<MeshDefinitionStore> logger)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string GetMeshPath(string sessionId, string? format)
    {
        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        var f = NormalizeFormat(format);
        var file = f == "json" ? FileJson : FileYaml;
        return Path.Combine(ws.DecisionsDir, file);
    }

    public async Task<(string? Raw, string? Format)> TryLoadRawAsync(string sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        var pathYaml = Path.Combine(ws.DecisionsDir, FileYaml);
        var pathYml = Path.Combine(ws.DecisionsDir, FileYml);
        var pathJson = Path.Combine(ws.DecisionsDir, FileJson);

        try
        {
            // Prefer YAML if present (user-edit friendly).
            if (File.Exists(pathYaml))
                return (await LoadBoundedAsync(pathYaml, ct), "yaml");
            if (File.Exists(pathYml))
                return (await LoadBoundedAsync(pathYml, ct), "yaml");
            if (File.Exists(pathJson))
                return (await LoadBoundedAsync(pathJson, ct), "json");

            return (null, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load mesh definition (best-effort).");
            return (null, null);
        }
    }

    public async Task SaveAsync(string sessionId, string raw, string? format, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ws = _workspace.EnsureSessionWorkspace(sessionId);

        raw = (raw ?? string.Empty).Replace("\r", "").Trim();
        if (raw.Length == 0)
            throw new ArgumentException("mesh content is empty", nameof(raw));

        if (raw.Length > MaxChars)
            raw = raw[..MaxChars];

        Directory.CreateDirectory(ws.TmpDir);
        var tmp = Path.Combine(ws.TmpDir, $"{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tmp, raw, Encoding.UTF8, ct);

        Directory.CreateDirectory(ws.DecisionsDir);
        var f = NormalizeFormat(format);
        var file = f == "json" ? FileJson : FileYaml;
        var target = Path.Combine(ws.DecisionsDir, file);
        File.Move(tmp, target, overwrite: true);

        // Best-effort audit copy.
        TryWriteAuditCopy(ws, raw, f);
    }

    private async Task<string?> LoadBoundedAsync(string path, CancellationToken ct)
    {
        var text = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
        text = (text ?? string.Empty).Replace("\r", "").Trim();
        if (text.Length == 0)
            return null;
        if (text.Length > MaxChars)
            text = text[..MaxChars];
        return text;
    }

    private static string NormalizeFormat(string? format)
    {
        format = (format ?? string.Empty).Trim().ToLowerInvariant();
        return format switch
        {
            "json" => "json",
            "yaml" => "yaml",
            "yml" => "yaml",
            _ => "yaml" // default to YAML (human-friendly)
        };
    }

    private void TryWriteAuditCopy(WorkspacePaths ws, string raw, string format)
    {
        try
        {
            var dir = Path.Combine(ws.ArtifactsDir, "mesh");
            Directory.CreateDirectory(dir);

            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
            var ext = format == "json" ? "json" : "yaml";
            var full = Path.Combine(dir, $"mesh_{stamp}.{ext}");
            File.WriteAllText(full, raw, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to write mesh audit copy (best-effort).");
        }
    }
}


