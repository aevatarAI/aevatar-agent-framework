using System.Text;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Aevatar.Novel.Contracts;
using Aevatar.Novel.Sidecar.Services;
using Aevatar.Novel.Sidecar.Services.NarrativeTests;
using Aevatar.Novel.Sidecar.Services.SetupPayoff;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Novel.Sidecar.Agents;

// ============================================================
//  SetupPayoffLedgerAgent (Workflow: Setup/Payoff Ledger)
//
//  Input:
//  - SstFileChangedEvent (chapter .txt or setup_payoff_ledger.md)
//
//  Output (derived):
//  - artifacts/ledger/reports/<run_id>_setup_payoff_report.md
//  - SidecarEvent(setup_payoff_scan_completed=SetupPayoffLedgerScanCompletedEvent)
// ============================================================

public sealed class SetupPayoffLedgerAgent : GAgentBase<Empty>
{
    private readonly SidecarEventHub _eventHub;
    private readonly SetupPayoffLedgerRunner _runner;

    public SetupPayoffLedgerAgent(
        SidecarEventHub eventHub,
        SetupPayoffLedgerRunner runner)
    {
        _eventHub = eventHub;
        _runner = runner;
    }

    public override Task<string> GetDescriptionAsync()
        => Task.FromResult($"SetupPayoffLedgerAgent (id={Id})");

    [EventHandler]
    public async Task HandleSstFileChangedEvent(SstFileChangedEvent fc)
    {
        var fullPath = (fc.FullPath ?? string.Empty).Replace('\\', '/');
        if (!ShouldHandle(fullPath))
            return;

        var projectRoot = fc.ProjectRoot ?? string.Empty;
        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
            return;

        if (!IsUnderRoot(projectRoot, fc.FullPath ?? string.Empty))
            return;

        var storyRoot = NarrativeTestsWorkflow.TryGetStoryRoot(fc.FullPath ?? string.Empty);
        if (string.IsNullOrWhiteSpace(storyRoot) || !Directory.Exists(storyRoot))
            return;

        var storyId = Path.GetFileName(storyRoot);
        var triggerRel = !string.IsNullOrWhiteSpace(fc.RelativePath)
            ? fc.RelativePath.Replace('\\', '/')
            : fullPath;

        var run = await _runner.RunAsync(
            projectRoot: projectRoot,
            storyRoot: storyRoot,
            triggerRelativePath: triggerRel,
            ct: CancellationToken.None);

        var reportsDir = Path.Combine(storyRoot, "artifacts", "ledger", "reports");
        Directory.CreateDirectory(reportsDir);
        var reportPath = Path.Combine(reportsDir, $"{run.RunId}_setup_payoff_report.md");

        await AtomicWriteAsync(reportPath, _runner.BuildReportMarkdown(run, storyId));

        var ledgerPath = Path.Combine(storyRoot, "artifacts", "ledger", "setup_payoff_ledger.md");

        var completed = new SetupPayoffLedgerScanCompletedEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            ProjectRoot = projectRoot,
            StoryId = storyId,
            TriggerRelativePath = triggerRel,
            OpenCount = run.Summary.OpenCount,
            PaidCount = run.Summary.PaidCount,
            BrokenCount = run.Summary.BrokenCount,
            DueSoonCount = run.Summary.DueSoonCount,
            Ledger = new ArtifactRef
            {
                ArtifactId = "setup-payoff-ledger",
                Kind = ArtifactKind.SetupPayoffLedger,
                Title = "Setup/Payoff Ledger",
                Uri = $"file://{ledgerPath.Replace('\\', '/')}"
            },
            Report = new ArtifactRef
            {
                ArtifactId = run.RunId,
                Kind = ArtifactKind.SetupPayoffReport,
                Title = "Setup/Payoff Ledger Report",
                Uri = $"file://{reportPath.Replace('\\', '/')}"
            }
        };

        _eventHub.Publish(new SidecarEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            SetupPayoffScanCompleted = completed
        });
    }

    private static bool ShouldHandle(string normalizedFullPath)
    {
        // Ignore derived reports (avoid infinite loops).
        if (normalizedFullPath.Contains("/artifacts/ledger/reports/", StringComparison.OrdinalIgnoreCase))
            return false;

        if (normalizedFullPath.Contains("/chapters/", StringComparison.OrdinalIgnoreCase) &&
            normalizedFullPath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            return true;

        if (normalizedFullPath.EndsWith("/artifacts/ledger/setup_payoff_ledger.md", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
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


