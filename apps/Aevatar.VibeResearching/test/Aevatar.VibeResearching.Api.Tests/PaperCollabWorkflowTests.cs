using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aevatar.Agents.Cognitive.Primitives;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using VibeResearching.Api.Facts;
using VibeResearching.Api.Paper;
using VibeResearching.Api.Sessions;
using VibeResearching.Api.Workspace;
using VibeResearching.Api.Vibe.Dag;
using VibeResearching.Api.Vibe.Steps;
using VibeResearching.Contracts.Collab;
using static VibeResearching.Api.Tests.WorkflowTestHelpers;

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

    [Fact]
    public async Task WorkflowSteps_EndToEnd_WritesDagDeliveryTrace()
    {
        using var env = VibeStepEnv.Create();

        var sessionId = "session01";
        var runId = "run01";
        var session = env.Sessions.GetOrCreate(sessionId);
        session.DagId = sessionId;
        var dagId = session.EffectiveDagId;

        var now = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow);
        var seed = new SraDagMutation
        {
            SessionId = sessionId,
            MutationId = "seed1",
            AuthorAgent = "tester",
            CreatedAt = now
        };
        seed.Labels["planKind"] = "milestone";
        seed.UpsertNodes.Add(new SraDagNode
        {
            Id = "plan_m1",
            Kind = SraDagNodeKind.Plan,
            Type = SraDagNodeType.Unknown,
            Label = "Milestone 1",
            UpdatedAt = now,
            SessionId = sessionId,
            Tags =
            {
                ["planKind"] = "milestone",
                ["milestoneRoundIndex"] = "1"
            }
        });
        seed.UpsertNodes.Add(new SraDagNode
        {
            Id = "plan_r1",
            Kind = SraDagNodeKind.Plan,
            Type = SraDagNodeType.Unknown,
            Label = "Round Plan",
            UpdatedAt = now,
            SessionId = sessionId,
            Tags =
            {
                ["planKind"] = "round"
            }
        });
        seed.UpsertNodes.Add(new SraDagNode
        {
            Id = "k1",
            Kind = SraDagNodeKind.Knowledge,
            Type = SraDagNodeType.Axiom,
            Label = "Known Fact",
            Proof = "Fact proof",
            UpdatedAt = now,
            SessionId = sessionId
        });
        await env.DagStore.ApplyMutationAsync(dagId, seed, CancellationToken.None);

        var ws = await env.Paper.EnsurePaperFilesAsync(sessionId, CancellationToken.None);
        await File.WriteAllTextAsync(ws.PaperOutlinePath, "Outline\n- item1\n", CancellationToken.None);
        await File.WriteAllTextAsync(ws.PaperDraftPath, "Line1\nLine2\nLine3\n", CancellationToken.None);

        var agent = CreateAgent(new Dictionary<string, object?>
        {
            ["session_id"] = sessionId,
            ["run_id"] = runId,
            ["question"] = "What is foo?"
        });

        var pivotStep = new VibePivotDetectionStepModule(
            CreateNoopPivot(),
            env.Brief,
            env.Sessions,
            NullLogger<VibePivotDetectionStepModule>.Instance);
        var pivotRes = await pivotStep.ExecuteAsync(
            agent,
            new StepDefinition { Id = "pivot", Type = "vibe_pivot_detection" },
            null,
            null,
            CancellationToken.None);
        pivotRes.Success.ShouldBeTrue();

        var contextStep = new VibeContextStepModule(
            env.Materials,
            env.DagStore,
            env.Paper,
            env.Sessions,
            NullLogger<VibeContextStepModule>.Instance);
        var contextRes = await contextStep.ExecuteAsync(
            agent,
            new StepDefinition { Id = "context", Type = "vibe_context" },
            null,
            null,
            CancellationToken.None);
        contextRes.Success.ShouldBeTrue();
        var ctx = ShouldBeDictionary(contextRes.Value);
        var planContext = ctx["plan_context"]?.ToString() ?? string.Empty;
        planContext.ShouldNotBeEmpty();
        var materialsContext = ctx["materials_context"]?.ToString() ?? string.Empty;
        materialsContext.ShouldNotBeEmpty();
        var outlineExcerpt = ctx["outline_excerpt"]?.ToString() ?? string.Empty;
        outlineExcerpt.ShouldContain("Outline");
        SetWorkflowVar(agent, "vibe_context", ctx);

        var librarianJson = """
        {
          "FactsWrite": [
            { "Title": "Fact A", "Content": "Fact content" }
          ],
          "AxiomsForDag": [
            { "Id": "AX1", "Label": "Axiom 1", "Citation": "C1" }
          ]
        }
        """;
        SetWorkflowVar(agent, "librarian", librarianJson);
        var librarianStep = new VibeLibrarianEffectsStepModule(
            env.Parsing,
            env.Sessions,
            NullLogger<VibeLibrarianEffectsStepModule>.Instance);
        var librarianRes = await librarianStep.ExecuteAsync(
            agent,
            new StepDefinition { Id = "librarian_effects", Type = "vibe_librarian_effects" },
            null,
            null,
            CancellationToken.None);
        librarianRes.Success.ShouldBeTrue();
        var librarianEffects = ShouldBeDictionary(librarianRes.Value);
        ((List<string>)librarianEffects["facts_written"]).Count.ShouldBeGreaterThan(0);
        SetWorkflowVar(agent, "librarian_effects", librarianEffects);

        var candidateJson = """
        {
          "MutationId": "m2",
          "AuthorAgent": "dag_builder",
          "Nodes": [
            { "Id": "K2", "Type": "axiom", "Kind": "knowledge", "Label": "New Node", "Proof": "Proof" }
          ],
          "Edges": []
        }
        """;
        SetWorkflowVar(agent, "dag_builder", candidateJson);
        SetWorkflowVar(agent, "dag_consensus", new Dictionary<string, object?>
        {
            ["accept"] = true,
            ["red_flags"] = new List<string>()
        });

        var dagApplyStep = new VibeDagApplyStepModule(
            env.DagStore,
            env.Sessions,
            NullLogger<VibeDagApplyStepModule>.Instance);
        var dagApplyRes = await dagApplyStep.ExecuteAsync(
            agent,
            new StepDefinition { Id = "dag_apply", Type = "vibe_dag_apply" },
            null,
            null,
            CancellationToken.None);
        dagApplyRes.Success.ShouldBeTrue();
        var dagApply = ShouldBeDictionary(dagApplyRes.Value);
        ((bool)dagApply["accepted"]).ShouldBeTrue();
        SetWorkflowVar(agent, "dag_apply", dagApply);

        var paperEditorJson = """
        {
          "paperPatches": [
            {
              "targetFile": "draft",
              "format": "replace_span",
              "replaceStartLine": 2,
              "replaceEndLineExclusive": 3,
              "replaceText": "Updated line"
            }
          ],
          "delivery": {
            "changedSummary": "updated",
            "conclusions": [
              { "cardId": "c1", "claim": "claim", "confidence": "medium" }
            ],
            "evidence": [
              { "evidenceId": "e1", "title": "paper", "path": "sources/p1.pdf" }
            ],
            "tasks": [
              { "taskId": "t1", "title": "task", "detail": "detail", "priority": 1 }
            ]
          }
        }
        """;
        SetWorkflowVar(agent, "paper_editor", paperEditorJson);
        var deliveryStep = new VibeDeliveryApplyStepModule(
            env.Paper,
            env.Delivery,
            env.Sessions,
            NullLogger<VibeDeliveryApplyStepModule>.Instance);
        var deliveryRes = await deliveryStep.ExecuteAsync(
            agent,
            new StepDefinition { Id = "delivery_apply", Type = "vibe_delivery_apply" },
            null,
            null,
            CancellationToken.None);
        deliveryRes.Success.ShouldBeTrue();
        var deliveryApply = ShouldBeDictionary(deliveryRes.Value);
        ((int)deliveryApply["patches_applied"]).ShouldBeGreaterThan(0);
        SetWorkflowVar(agent, "delivery_apply", deliveryApply);

        var updatedDraft = await File.ReadAllTextAsync(ws.PaperDraftPath, CancellationToken.None);
        updatedDraft.ShouldContain("Updated line");

        SetWorkflowVar(agent, "planner", "plan");
        SetWorkflowVar(agent, "reasoner", "reason");
        SetWorkflowVar(agent, "librarian", "librarian");
        SetWorkflowVar(agent, "verifier", "verifier");
        SetWorkflowVar(agent, "round_summary", "summary");

        var traceStep = new VibeTraceAppendStepModule(
            env.Trace,
            env.Workspace,
            env.Sessions,
            NullLogger<VibeTraceAppendStepModule>.Instance);
        var traceRes = await traceStep.ExecuteAsync(
            agent,
            new StepDefinition { Id = "trace_append", Type = "vibe_trace_append" },
            null,
            null,
            CancellationToken.None);
        traceRes.Success.ShouldBeTrue();

        var summaryPath = Path.Combine(ws.RunsDir, runId, "summary.md");
        File.Exists(summaryPath).ShouldBeTrue();

        var finalSnap = await env.DagStore.LoadSnapshotAsync(dagId, CancellationToken.None);
        finalSnap.Nodes.Any(n => n.Id == "K2").ShouldBeTrue();
    }

    [Fact]
    public async Task VibeDagApply_ReturnsBlocked_WhenConsensusRejects()
    {
        using var env = VibeStepEnv.Create();

        var sessionId = "session02";
        var runId = "run02";
        _ = env.Sessions.GetOrCreate(sessionId);

        var agent = CreateAgent(new Dictionary<string, object?>
        {
            ["session_id"] = sessionId,
            ["run_id"] = runId,
            ["question"] = "Question?"
        });

        var candidateJson = """
        {
          "MutationId": "m3",
          "AuthorAgent": "dag_builder",
          "Nodes": [
            { "Id": "K3", "Type": "axiom", "Kind": "knowledge", "Label": "Blocked Node", "Proof": "Proof" }
          ],
          "Edges": []
        }
        """;
        SetWorkflowVar(agent, "dag_builder", candidateJson);
        SetWorkflowVar(agent, "dag_consensus", new Dictionary<string, object?>
        {
            ["accept"] = false,
            ["red_flags"] = new List<string> { "bad evidence" }
        });

        var dagApplyStep = new VibeDagApplyStepModule(
            env.DagStore,
            env.Sessions,
            NullLogger<VibeDagApplyStepModule>.Instance);
        var dagApplyRes = await dagApplyStep.ExecuteAsync(
            agent,
            new StepDefinition { Id = "dag_apply", Type = "vibe_dag_apply" },
            null,
            null,
            CancellationToken.None);
        dagApplyRes.Success.ShouldBeTrue();
        var dagApply = ShouldBeDictionary(dagApplyRes.Value);
        ((bool)dagApply["accepted"]).ShouldBeFalse();
        ((bool)dagApply["blocked"]).ShouldBeTrue();

        var snap = await env.DagStore.LoadSnapshotAsync(ResearchSession.GlobalDagId, CancellationToken.None);
        snap.Nodes.Any(n => n.Id == "K3").ShouldBeFalse();
    }
}
