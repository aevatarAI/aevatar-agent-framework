using Aevatar.Agents.Core.Secrets;
using Aevatar.Agents.Knowledge.Graph;
using Aevatar.Agents.Persistence.InMemory.Graph;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using VibeResearching.Api.Facts;
using VibeResearching.Api.Paper;
using VibeResearching.Api.Sessions;
using VibeResearching.Api.Workspace;
using VibeResearching.Api.Vibe.Dag;
using VibeResearching.Contracts.Collab;

namespace VibeResearching.Api.Tests;

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
        Directory.Exists(ws.DeliverablesDir).ShouldBeTrue();
        Directory.Exists(ws.TmpDir).ShouldBeTrue();

        File.Exists(ws.PaperOutlinePath).ShouldBeTrue();
        File.Exists(ws.PaperDraftPath).ShouldBeTrue();
    }

    [Fact]
    public async Task FactLifecycle_PromoteByVotes_WritesFinalDecisionAndDagNode()
    {
        using var env = TestEnv.Create();

        var workspace = new WorkspaceService(env.Host, NullLogger<WorkspaceService>.Instance);
        var dag = CreateDagStore(workspace);
        var facts = new FactLifecycleService(workspace, dag, NullLogger<FactLifecycleService>.Instance);

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

        var decision = await facts.EvaluateAndPromoteAsync(
            sessionId,
            ResearchSession.GlobalDagId,
            proposal.FactId,
            finalizedBy: "tester",
            CancellationToken.None);
        decision.ShouldNotBeNull();
        decision!.Decision.ShouldBe(FactDecisionValue.Promote);

        var ws = workspace.EnsureSessionWorkspace(sessionId);
        File.Exists(Path.Combine(ws.DecisionsDir, "final", $"{proposal.FactId}.json")).ShouldBeTrue();
        File.Exists(Path.Combine(ws.FactsProposedDir, $"{proposal.FactId}.json")).ShouldBeTrue(); // audit copy kept

        var snap = await dag.LoadSnapshotAsync(ResearchSession.GlobalDagId, CancellationToken.None);
        snap.Nodes.Any(n => n.Id == proposal.FactId).ShouldBeTrue();
    }

    [Fact]
    public async Task FactLifecycle_PromoteByVerification_WritesDagNode()
    {
        using var env = TestEnv.Create();

        var workspace = new WorkspaceService(env.Host, NullLogger<WorkspaceService>.Instance);
        var dag = CreateDagStore(workspace);
        var facts = new FactLifecycleService(workspace, dag, NullLogger<FactLifecycleService>.Instance);

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

        var decision = await facts.EvaluateAndPromoteAsync(
            sessionId,
            ResearchSession.GlobalDagId,
            proposal.FactId,
            finalizedBy: "tester",
            CancellationToken.None);
        decision.ShouldNotBeNull();
        decision!.Decision.ShouldBe(FactDecisionValue.Promote);

        var snap = await dag.LoadSnapshotAsync(ResearchSession.GlobalDagId, CancellationToken.None);
        snap.Nodes.Any(n => n.Id == proposal.FactId).ShouldBeTrue();
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
            var systemRoot = Path.Combine(root, "Aevatar.VibeResearching");
            var contentRoot = Path.Combine(systemRoot, "src", "VibeResearching.Api");
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
        public string ApplicationName { get; set; } = "VibeResearching.Api.Tests";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private static DagStore CreateDagStore(WorkspaceService workspace)
    {
        var services = new ServiceCollection();
        services.AddAevatarGraphInMemory();
        services.AddKnowledgeGraph();

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IKnowledgeGraphClientFactory>();
        var secrets = new TestSecretsStore();
        return new DagStore(workspace, factory, secrets, NullLogger<DagStore>.Instance);
    }

    private sealed class TestSecretsStore : IAevatarUserSecretsStore
    {
        private readonly Dictionary<string, string> _data = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, string> GetAll() => _data;

        public bool TryGet(string key, out string value) => _data.TryGetValue(key, out value!);

        public void Set(string key, string value) => _data[key] = value;

        public bool Remove(string key) => _data.Remove(key);
    }
}


