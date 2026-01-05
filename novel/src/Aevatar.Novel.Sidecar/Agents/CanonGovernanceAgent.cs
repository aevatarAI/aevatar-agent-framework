using System.Text;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Aevatar.Novel.Contracts;
using Aevatar.Novel.Sidecar.Services;
using Aevatar.Novel.Sidecar.Services.CanonGovernance;
using Aevatar.Novel.Sidecar.Services.NarrativeTests;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Novel.Sidecar.Agents;

// ============================================================
//  CanonGovernanceAgent (v1)
//
//  Trigger:
//  - artifacts/canon/*.md changes (SSOT)
//
//  Output:
//  - artifacts/canon/changes/<record_id>_canon_change.md
//  - SidecarEvent(canon_change_recorded)
// ============================================================

public sealed class CanonGovernanceAgent : GAgentBase<Empty>
{
    private readonly SidecarEventHub _hub;
    private readonly CanonAssetRevisionStore _revisions;

    public CanonGovernanceAgent(SidecarEventHub hub, CanonAssetRevisionStore revisions)
    {
        _hub = hub;
        _revisions = revisions;
    }

    public override Task<string> GetDescriptionAsync()
        => Task.FromResult($"CanonGovernanceAgent (id={Id})");

    [EventHandler]
    public async Task HandleSstFileChangedEvent(SstFileChangedEvent fc)
    {
        var rel = (fc.RelativePath ?? string.Empty).Replace('\\', '/');
        var fullPath = fc.FullPath ?? string.Empty;
        if (!ShouldTrigger(rel, fullPath))
            return;

        var projectRoot = fc.ProjectRoot ?? string.Empty;
        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
            return;

        var storyRoot = NarrativeTestsWorkflow.TryGetStoryRoot(fullPath);
        if (string.IsNullOrWhiteSpace(storyRoot) || !Directory.Exists(storyRoot))
            return;

        var storyId = Path.GetFileName(storyRoot);
        var assetKey = MakeAssetKey(rel);

        var change = await _revisions.TryRegisterRevisionAsync(storyRoot, assetKey, fullPath, CancellationToken.None);
        if (change is null)
            return;

        var recordId = Guid.NewGuid().ToString("N");
        var changesDir = Path.Combine(storyRoot, "artifacts", "canon", "changes");
        Directory.CreateDirectory(changesDir);

        var recordPath = Path.Combine(changesDir, $"{recordId}_canon_change.md");
        var md = CanonGovernanceMarkdown.BuildCanonChangeRecordMarkdown(
            recordId,
            storyId,
            rel,
            change.BaseRevision,
            change.EditedRevision,
            change.BaseText,
            change.EditedText);

        await AtomicWriteAsync(recordPath, md);

        var recordRef = new ArtifactRef
        {
            ArtifactId = recordId,
            Kind = ArtifactKind.Custom,
            Title = "Canon Change Record",
            Uri = $"file://{recordPath.Replace('\\', '/')}"
        };
        recordRef.Labels["type"] = "canon_change_record";
        recordRef.Labels["changed_relative_path"] = rel;

        _hub.Publish(new SidecarEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            CanonChangeRecorded = new CanonChangeRecordedEvent
            {
                EventId = Guid.NewGuid().ToString("N"),
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                ProjectRoot = projectRoot,
                StoryId = storyId,
                ChangedRelativePath = rel,
                CanonChangeRecord = recordRef
            }
        });
    }

    private static bool ShouldTrigger(string rel, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return false;
        if (!fullPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return false;

        var p = (rel ?? fullPath).Replace('\\', '/');
        if (!p.Contains("/artifacts/canon/", StringComparison.OrdinalIgnoreCase))
            return false;

        // Ignore governance records themselves to avoid loops.
        if (p.Contains("/artifacts/canon/changes/", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static string MakeAssetKey(string relativePath)
    {
        // Turn "volumes/.../artifacts/canon/x.md" into a stable short key.
        var p = (relativePath ?? string.Empty).Replace('\\', '/').ToLowerInvariant();
        var idx = p.IndexOf("/artifacts/canon/", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
            p = p[(idx + "/artifacts/canon/".Length)..];

        p = p.Replace("/", "_");
        p = Path.GetFileNameWithoutExtension(p) ?? p;
        if (string.IsNullOrWhiteSpace(p))
            p = "canon_asset";

        // Keep it filesystem-safe.
        var sb = new StringBuilder();
        foreach (var ch in p)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')
                sb.Append(ch);
            else
                sb.Append('_');
        }

        return sb.ToString();
    }

    private static async Task AtomicWriteAsync(string path, string content)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        var tmp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tmp, content, Encoding.UTF8);
        File.Move(tmp, path, overwrite: true);
    }
}


