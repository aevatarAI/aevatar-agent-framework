using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using ScientificResearchAssistant.Api.Facts;
using ScientificResearchAssistant.Api.Paper;
using ScientificResearchAssistant.Api.Workspace;
using ScientificResearchAssistant.Contracts.Collab;

namespace ScientificResearchAssistant.Api.Tests;

public sealed class PaperCollabWorkflowTests
{
    [Fact]
    public async Task SessionWorkspace_CreatesDirectoriesAndPaperFiles()
    {
        using var env = TestEnv.Create();

        var workspace = new WorkspaceService(env.Host, NullLogger<WorkspaceService>.Instance);
        var mailbox = new FileMailboxService(workspace, NullLogger<FileMailboxService>.Instance);
        var paper = new PaperService(workspace, mailbox, NullLogger<PaperService>.Instance);

        var sessionId = "testsession1";
        var ws = await paper.EnsurePaperFilesAsync(sessionId, CancellationToken.None);

        Directory.Exists(ws.SessionRoot).ShouldBeTrue();
        Directory.Exists(ws.PaperDir).ShouldBeTrue();
        Directory.Exists(ws.FactsProposedDir).ShouldBeTrue();
        Directory.Exists(ws.DecisionsDir).ShouldBeTrue();
        Directory.Exists(ws.MailboxDir).ShouldBeTrue();
        Directory.Exists(ws.RunsDir).ShouldBeTrue();
        Directory.Exists(ws.ArtifactsDir).ShouldBeTrue();
        Directory.Exists(ws.TmpDir).ShouldBeTrue();

        File.Exists(ws.PaperOutlinePath).ShouldBeTrue();
        File.Exists(ws.PaperDraftPath).ShouldBeTrue();
    }

    [Fact]
    public async Task FactLifecycle_PromoteByVotes_WritesFinalDecisionAndFactsFile()
    {
        using var env = TestEnv.Create();

        var workspace = new WorkspaceService(env.Host, NullLogger<WorkspaceService>.Instance);
        var facts = new FactLifecycleService(workspace, NullLogger<FactLifecycleService>.Instance);

        var sessionId = "testsession2";
        var proposal = await facts.CreateProposalAsync(
            sessionId,
            title: "Claim: 1+1=2",
            content: "In Peano arithmetic, 1+1=2.",
            evidencePaths: null,
            proposedBy: "tester",
            ct: CancellationToken.None);

        await facts.RecordVoteAsync(sessionId, new FactVote
        {
            FactId = proposal.FactId,
            ReviewerId = "reviewer_1",
            Vote = FactVoteValue.Approve
        }, CancellationToken.None);

        await facts.RecordVoteAsync(sessionId, new FactVote
        {
            FactId = proposal.FactId,
            ReviewerId = "reviewer_2",
            Vote = FactVoteValue.Approve
        }, CancellationToken.None);

        var decision = await facts.EvaluateAndPromoteAsync(sessionId, proposal.FactId, finalizedBy: "tester", CancellationToken.None);
        decision.ShouldNotBeNull();
        decision!.Decision.ShouldBe(FactDecisionValue.Promote);

        var ws = workspace.EnsureSessionWorkspace(sessionId);
        File.Exists(Path.Combine(ws.DecisionsDir, "final", $"{proposal.FactId}.json")).ShouldBeTrue();
        File.Exists(Path.Combine(ws.FactsDir, $"{proposal.FactId}.json")).ShouldBeTrue();
        File.Exists(Path.Combine(ws.FactsProposedDir, $"{proposal.FactId}.json")).ShouldBeTrue(); // audit copy kept
    }

    [Fact]
    public async Task FactLifecycle_PromoteByVerification_WritesFactsFile()
    {
        using var env = TestEnv.Create();

        var workspace = new WorkspaceService(env.Host, NullLogger<WorkspaceService>.Instance);
        var facts = new FactLifecycleService(workspace, NullLogger<FactLifecycleService>.Instance);

        var sessionId = "testsession3";
        var proposal = await facts.CreateProposalAsync(
            sessionId,
            title: "Claim: Verified by tool",
            content: "This claim is verified by a tool.",
            evidencePaths: new[] { "artifacts/run1/output.txt" },
            proposedBy: "tester",
            ct: CancellationToken.None);

        await facts.RecordVerificationAsync(sessionId, new FactVerification
        {
            FactId = proposal.FactId,
            VerifierId = "python_verifier",
            Tool = "python_exec",
            Result = true,
            LogExcerpt = "ok"
        }, CancellationToken.None);

        var decision = await facts.EvaluateAndPromoteAsync(sessionId, proposal.FactId, finalizedBy: "tester", CancellationToken.None);
        decision.ShouldNotBeNull();
        decision!.Decision.ShouldBe(FactDecisionValue.Promote);

        var ws = workspace.EnsureSessionWorkspace(sessionId);
        File.Exists(Path.Combine(ws.FactsDir, $"{proposal.FactId}.json")).ShouldBeTrue();
    }

    [Fact]
    public async Task PaperPatch_ProposeViaMailbox_AndApplyReplaceSpan_UpdatesDraft()
    {
        using var env = TestEnv.Create();

        var workspace = new WorkspaceService(env.Host, NullLogger<WorkspaceService>.Instance);
        var mailbox = new FileMailboxService(workspace, NullLogger<FileMailboxService>.Instance);
        var paper = new PaperService(workspace, mailbox, NullLogger<PaperService>.Instance);

        var sessionId = "testsession4";
        var ws = await paper.EnsurePaperFilesAsync(sessionId, CancellationToken.None);

        await File.WriteAllTextAsync(ws.PaperDraftPath, "line1\nline2\nline3\n", CancellationToken.None);

        var patch = new PaperPatchProposal
        {
            SessionId = sessionId,
            PatchId = "p1",
            AuthorAgent = "writer_helper",
            TargetFile = PaperTargetFile.Draft,
            Format = PaperPatchFormat.ReplaceSpan,
            ReplaceStartLine = 2,
            ReplaceEndLineExclusive = 3,
            ReplaceText = "NEW_LINE_2",
            CorrelationId = "c1"
        };

        var inboxPath = await paper.ProposePatchAsync(sessionId, patch, CancellationToken.None);
        File.Exists(inboxPath).ShouldBeTrue();

        await paper.ApplyPatchAsync(sessionId, runId: "run1", patch, CancellationToken.None);

        var updated = await File.ReadAllTextAsync(ws.PaperDraftPath, CancellationToken.None);
        updated.ShouldContain("NEW_LINE_2");
        updated.ShouldNotContain("line2");

        // Run log should exist (best-effort)
        var logFile = Path.Combine(ws.RunsDir, "run1", "paper_patch_p1.json");
        File.Exists(logFile).ShouldBeTrue();
    }

    private sealed class TestEnv : IDisposable
    {
        public required string Root { get; init; }
        public required IHostEnvironment Host { get; init; }

        public static TestEnv Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "sra-paper-collab-tests", Guid.NewGuid().ToString("N"));
            var systemRoot = Path.Combine(root, "scientific-research-assistant");
            var contentRoot = Path.Combine(systemRoot, "src", "ScientificResearchAssistant.Api");
            Directory.CreateDirectory(contentRoot);

            // Facts/sources are optional; facts dir may be created later by promote.
            Directory.CreateDirectory(Path.Combine(systemRoot, "sources"));

            var host = new SimpleHostEnvironment { ContentRootPath = contentRoot };
            return new TestEnv { Root = root, Host = host };
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                    Directory.Delete(Root, recursive: true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    private sealed class SimpleHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "ScientificResearchAssistant.Api.Tests";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}


