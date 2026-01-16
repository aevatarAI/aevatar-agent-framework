using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using VibeResearching.Api.Vibe.Brief;
using VibeResearching.Api.Vibe.Delivery;
using VibeResearching.Api.Workspace;
using VibeResearching.Contracts.Collab;

namespace VibeResearching.Api.Tests;

public sealed class DeliverablesStoresTests
{
    [Fact]
    public async Task BriefStore_SaveAndLoad_WritesBriefJson_AndBoundsFields()
    {
        using var env = TestEnv.Create();

        var workspace = new WorkspaceService(env.Host, NullLogger<WorkspaceService>.Instance);
        var brief = new BriefStore(workspace, NullLogger<BriefStore>.Instance);

        var sessionId = "briefsession1";
        var ws = workspace.EnsureSessionWorkspace(sessionId);

        var tooLong = new string('x', 5000);
        var snap = new SraResearchBriefSnapshot
        {
            SessionId = sessionId,
            Version = 1,
            RewrittenQuestion = tooLong,
            Scope = tooLong,
            SuccessCriteria = tooLong
        };
        snap.Assumptions.AddRange(Enumerable.Range(0, 200).Select(i => $"a{i}"));
        snap.Risks.AddRange(Enumerable.Range(0, 200).Select(i => $"r{i}"));
        snap.Uncertainties.AddRange(Enumerable.Range(0, 200).Select(i => $"u{i}"));
        snap.Terms.Add(new SraResearchTerm { Term = "t1", Meaning = "m1" });
        snap.Milestones.Add(new SraResearchMilestone { RoundIndex = 1, ExpectedOutput = "o1" });

        var saved = await brief.SaveAsync(sessionId, snap, CancellationToken.None);
        saved.Version.ShouldBe(1);

        var path = Path.Combine(ws.DeliverablesDir, "brief.json");
        File.Exists(path).ShouldBeTrue();

        var loaded = await brief.LoadAsync(sessionId, CancellationToken.None);
        loaded.Version.ShouldBe(1);
        loaded.RewrittenQuestion.Length.ShouldBeLessThanOrEqualTo(1200);
        loaded.Scope.Length.ShouldBeLessThanOrEqualTo(2000);
        loaded.SuccessCriteria.Length.ShouldBeLessThanOrEqualTo(1200);
        loaded.Assumptions.Count.ShouldBeLessThanOrEqualTo(80);
        loaded.Risks.Count.ShouldBeLessThanOrEqualTo(80);
        loaded.Uncertainties.Count.ShouldBeLessThanOrEqualTo(80);
    }

    [Fact]
    public async Task DeliveryCenterStore_SaveAndLoad_WritesAllFiles_AndUiSnapshot()
    {
        using var env = TestEnv.Create();

        var workspace = new WorkspaceService(env.Host, NullLogger<WorkspaceService>.Instance);
        var store = new DeliveryCenterStore(workspace, NullLogger<DeliveryCenterStore>.Instance);

        var sessionId = "deliverysession1";
        var ws = workspace.EnsureSessionWorkspace(sessionId);

        var now = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow);

        var conclusions = new SraConclusionCardsSnapshot { SessionId = sessionId, Version = 1, UpdatedAt = now };
        conclusions.Items.Add(new SraConclusionCard { CardId = "c1", Claim = "claim", Confidence = SraConfidenceLevel.Medium, UpdatedAt = now });
        await store.SaveConclusionsAsync(sessionId, conclusions, CancellationToken.None);

        var evidence = new SraEvidenceTableSnapshot { SessionId = sessionId, Version = 1, UpdatedAt = now };
        evidence.Items.Add(new SraEvidenceItem { EvidenceId = "e1", Title = "paper", Path = "sources/p1.pdf", UpdatedAt = now });
        await store.SaveEvidenceAsync(sessionId, evidence, CancellationToken.None);

        var tasks = new SraNextTasksSnapshot { SessionId = sessionId, Version = 1, UpdatedAt = now };
        tasks.Items.Add(new SraNextTaskItem { TaskId = "t1", Title = "do", Detail = "detail", Priority = 1, UpdatedAt = now });
        await store.SaveTasksAsync(sessionId, tasks, CancellationToken.None);

        var delivery = new SraDeliveryCenterSnapshot
        {
            SessionId = sessionId,
            Version = 1,
            PaperOutlinePath = "paper/outline.md",
            PaperDraftPath = "paper/draft.md",
            ConclusionsPath = "deliverables/conclusions.json",
            EvidencePath = "deliverables/evidence.json",
            TasksPath = "deliverables/tasks.json",
            ChangedSummary = "updated",
            UpdatedAt = now
        };
        await store.SaveDeliverySnapshotAsync(sessionId, delivery, CancellationToken.None);

        var (pCon, pEv, pTasks, pDelivery) = store.GetPaths(sessionId);
        File.Exists(pCon).ShouldBeTrue();
        File.Exists(pEv).ShouldBeTrue();
        File.Exists(pTasks).ShouldBeTrue();
        File.Exists(pDelivery).ShouldBeTrue();

        var all = await store.LoadAllAsync(sessionId, CancellationToken.None);
        all.Delivery.Version.ShouldBe(1);
        all.Conclusions.Items.Count.ShouldBe(1);
        all.Evidence.Items.Count.ShouldBe(1);
        all.Tasks.Items.Count.ShouldBe(1);

        var ui = await store.GetSnapshotForUiAsync(sessionId, CancellationToken.None);
        ui.ShouldNotBeNull();

        Directory.Exists(ws.DeliverablesDir).ShouldBeTrue();
    }

    private sealed class TestEnv : IDisposable
    {
        public required string Root { get; init; }
        public required IHostEnvironment Host { get; init; }

        public static TestEnv Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "sra-deliverables-tests", Guid.NewGuid().ToString("N"));
            var systemRoot = Path.Combine(root, "Aevatar.VibeResearching");
            var contentRoot = Path.Combine(systemRoot, "src", "VibeResearching.Api");
            Directory.CreateDirectory(contentRoot);
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
}


