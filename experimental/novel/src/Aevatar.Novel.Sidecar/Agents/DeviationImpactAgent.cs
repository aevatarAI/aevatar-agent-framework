using System.Text;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Aevatar.Novel.Contracts;
using Aevatar.Novel.Sidecar.Services;
using Aevatar.Novel.Sidecar.Services.DeviationImpact;
using Aevatar.Novel.Sidecar.Services.NarrativeTests;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Novel.Sidecar.Agents;

// ============================================================
//  DeviationImpactAgent (Workflow E executor)
//
//  Input:
//  - SstFileChangedEvent (chapter .txt)
//
//  Output:
//  - deviation_report.md
//  - deviation_prompt.md
//  - change_impact_report.md
//  - backup_options_report.md
//  - SidecarEvent(deviation_impact_completed=StoryDeviationImpactAnalysisCompletedEvent)
//
//  NOTE:
//  - v1 is deterministic (diff + keyword scan). Later we can enrich using LLM.
// ============================================================

public sealed class DeviationImpactAgent : GAgentBase<Empty>
{
    private readonly SidecarEventHub _eventHub;
    private readonly ChapterRevisionStore _revisions;
    private readonly DeviationImpactAnalyzer _analyzer;

    public DeviationImpactAgent(
        SidecarEventHub eventHub,
        ChapterRevisionStore revisions,
        DeviationImpactAnalyzer analyzer)
    {
        _eventHub = eventHub;
        _revisions = revisions;
        _analyzer = analyzer;
    }

    public override Task<string> GetDescriptionAsync()
        => Task.FromResult($"DeviationImpactAgent (id={Id})");

    [EventHandler]
    public async Task HandleSstFileChangedEvent(SstFileChangedEvent fc)
    {
        var fullPath = (fc.FullPath ?? string.Empty).Replace('\\', '/');
        if (!fullPath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            return;
        if (!fullPath.Contains("/chapters/", StringComparison.OrdinalIgnoreCase))
            return;

        var projectRoot = fc.ProjectRoot ?? string.Empty;
        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
            return;

        var storyRoot = NarrativeTestsWorkflow.TryGetStoryRoot(fc.FullPath ?? string.Empty);
        if (string.IsNullOrWhiteSpace(storyRoot) || !Directory.Exists(storyRoot))
            return;

        // Ensure path is under project root (belt & suspenders).
        if (!IsUnderRoot(projectRoot, fc.FullPath ?? string.Empty))
            return;

        var storyId = Path.GetFileName(storyRoot);
        var chapterId = Path.GetFileNameWithoutExtension(fc.FullPath) ?? string.Empty;

        var change = await _revisions.TryRegisterRevisionAsync(storyRoot, chapterId, fc.FullPath!, CancellationToken.None);
        if (change is null)
            return; // no previous revision or no content change

        var requestId = Guid.NewGuid().ToString("N");

        var deviationSummary = _analyzer.BuildDeviationSummary(
            projectId: projectRoot,
            storyId: storyId,
            chapterId: chapterId,
            baseRevision: change.BaseRevision,
            editedRevision: change.EditedRevision,
            baseText: change.BaseText,
            editedText: change.EditedText);

        var artifactsDir = Path.Combine(storyRoot, "artifacts", "deviation");
        Directory.CreateDirectory(artifactsDir);

        var deviationReportPath = Path.Combine(artifactsDir, $"{requestId}_deviation_report.md");
        var deviationPromptPath = Path.Combine(artifactsDir, $"{requestId}_deviation_prompt.md");
        var impactReportPath = Path.Combine(artifactsDir, $"{requestId}_change_impact_report.md");
        var backupOptionsPath = Path.Combine(artifactsDir, $"{requestId}_backup_options_report.md");

        var scanTargets = _analyzer.SuggestScanTargets(storyRoot, fc.FullPath!);

        await AtomicWriteAsync(deviationReportPath, _analyzer.BuildDeviationReportMarkdown(deviationSummary));
        await AtomicWriteAsync(deviationPromptPath, _analyzer.BuildDeviationPromptMarkdown(deviationSummary, scanTargets));
        await AtomicWriteAsync(impactReportPath, _analyzer.BuildChangeImpactReportMarkdown(deviationSummary, storyRoot, fc.FullPath!));
        await AtomicWriteAsync(backupOptionsPath, _analyzer.BuildBackupOptionsMarkdown(deviationSummary));

        var completed = new StoryDeviationImpactAnalysisCompletedEvent
        {
            RequestId = requestId,
            ProjectId = projectRoot,
            StoryId = storyId,
            ChapterId = chapterId,
            BaseRevision = change.BaseRevision,
            EditedRevision = change.EditedRevision,
            CompletedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            DeviationSummary = deviationSummary,
            DeviationReport = new ArtifactRef
            {
                ArtifactId = requestId,
                Kind = ArtifactKind.DeviationReport,
                Title = "Deviation Report",
                Uri = $"file://{deviationReportPath.Replace('\\', '/')}"
            },
            DeviationPrompt = new ArtifactRef
            {
                ArtifactId = requestId,
                Kind = ArtifactKind.DeviationPrompt,
                Title = "Deviation Prompt",
                Uri = $"file://{deviationPromptPath.Replace('\\', '/')}"
            },
            ChangeImpactReport = new ArtifactRef
            {
                ArtifactId = requestId,
                Kind = ArtifactKind.ChangeImpactReport,
                Title = "Change Impact Report",
                Uri = $"file://{impactReportPath.Replace('\\', '/')}"
            },
            BackupOptionsReport = new ArtifactRef
            {
                ArtifactId = requestId,
                Kind = ArtifactKind.BackupOptionsReport,
                Title = "Backup Options Report",
                Uri = $"file://{backupOptionsPath.Replace('\\', '/')}"
            }
        };

        _eventHub.Publish(new SidecarEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            DeviationImpactCompleted = completed
        });
    }

    private static async Task AtomicWriteAsync(string path, string content)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        var tmp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tmp, content, Encoding.UTF8);
        File.Move(tmp, path, overwrite: true);
    }

    private static bool IsUnderRoot(string projectRoot, string fullPath)
    {
        try
        {
            var root = Path.GetFullPath(projectRoot.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            var candidate = Path.GetFullPath(fullPath.Trim());
            return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}


