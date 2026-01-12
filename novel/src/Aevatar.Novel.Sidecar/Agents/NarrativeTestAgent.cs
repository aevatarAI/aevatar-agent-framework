using System.Text;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Aevatar.Novel.Contracts;
using Aevatar.Novel.Sidecar.Services;
using Aevatar.Novel.Sidecar.Services.NarrativeTests;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Novel.Sidecar.Agents;

// ============================================================
//  NarrativeTestAgent
//
//  ROLE:
//  - Consume SSOT file change events (Protobuf) from sidecar.
//  - Run Narrative Tests (workflow F) and persist the report as .md.
//  - Publish UnitTestsCompletedEvent back to SidecarEventHub (SSE).
//
//  NOTE:
//  - This agent is created inside the sidecar (Local runtime).
// ============================================================

public sealed class NarrativeTestAgent : GAgentBase<Empty>
{
    private readonly SidecarEventHub _eventHub;
    private readonly NarrativeTestRunner _runner;

    public NarrativeTestAgent(SidecarEventHub eventHub, NarrativeTestRunner runner)
    {
        _eventHub = eventHub;
        _runner = runner;
    }

    public override Task<string> GetDescriptionAsync()
        => Task.FromResult($"NarrativeTestAgent (id={Id})");

    [EventHandler]
    public async Task HandleSstFileChangedEvent(SstFileChangedEvent fc)
    {
        if (!NarrativeTestsWorkflow.ShouldTriggerTests(fc))
            return;

        var root = fc.ProjectRoot ?? string.Empty;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return;

        var storyRoot = string.IsNullOrWhiteSpace(fc.FullPath) ? null : NarrativeTestsWorkflow.TryGetStoryRoot(fc.FullPath);
        if (string.IsNullOrWhiteSpace(storyRoot) || !Directory.Exists(storyRoot))
            return;

        var storyId = Path.GetFileName(storyRoot);
        var chapterId = "";
        var normalized = (fc.FullPath ?? string.Empty).Replace('\\', '/');
        if (normalized.Contains("/chapters/", StringComparison.OrdinalIgnoreCase) &&
            normalized.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            chapterId = Path.GetFileNameWithoutExtension(fc.FullPath) ?? string.Empty;
        }

        var result = await _runner.RunAsync(root, storyRoot, triggerChapterPath: fc.FullPath, CancellationToken.None);

        var reportPath = Path.Combine(storyRoot, "artifacts", "tests", $"{result.Summary.RunId}_test_report.md");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);

        var reportMarkdown = NarrativeTestsWorkflow.BuildReportMarkdown(result, storyId, chapterId, storyRoot);
        await AtomicWriteAsync(reportPath, reportMarkdown);

        var reportRef = new ArtifactRef
        {
            ArtifactId = result.Summary.RunId,
            Kind = ArtifactKind.NarrativeTestReport,
            Title = "Narrative Test Report",
            Uri = $"file://{reportPath.Replace('\\', '/')}"
        };

        _eventHub.Publish(new SidecarEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            UnitTestsCompleted = new UnitTestsCompletedEvent
            {
                EventId = Guid.NewGuid().ToString("N"),
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                ProjectRoot = root,
                StoryId = storyId,
                ChapterId = chapterId,
                Summary = result.Summary,
                TestReport = reportRef
            }
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
}


