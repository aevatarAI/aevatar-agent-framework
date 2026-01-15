using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using VibeResearching.Api.Vibe.Trace;
using VibeResearching.Api.Workspace;
using VibeResearching.Contracts.Collab;
using Shouldly;
using Google.Protobuf.WellKnownTypes;

namespace VibeResearching.Tests;

public sealed class TraceStoreTests
{
    [Fact]
    public async Task AppendAsync_ShouldWriteTraceJsonl_AndSummaryMd()
    {
        var root = CreateTempSystemRoot();
        try
        {
            var sessionId = "test001";
            var runId = "run001";

            var env = new TestHostEnvironment(Path.Combine(root, "src", "VibeResearching.Api"));
            var ws = new WorkspaceService(env, NullLogger<WorkspaceService>.Instance);
            var store = new TraceStore(ws, NullLogger<TraceStore>.Instance);

            var summary = new SraRoundSummary
            {
                SessionId = sessionId,
                RunId = runId,
                RoundIndex = 0,
                TriggerKind = "user_message",
                TriggerRef = "r1",
                UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            };
            summary.PerAgent.Add(new SraRoundAgentSummary { Agent = "planner", Highlights = { "h1" } });

            await store.AppendAsync(sessionId, summary, "# Summary\n\nok", CancellationToken.None);

            var list = await store.LoadLatestAsync(sessionId, 10, CancellationToken.None);
            list.Count.ShouldBe(1);
            list[0].RunId.ShouldBe(runId);

            var paths = ws.EnsureSessionWorkspace(sessionId);
            var mdPath = Path.Combine(paths.RunsDir, runId, "summary.md");
            File.Exists(mdPath).ShouldBeTrue();
            (await File.ReadAllTextAsync(mdPath)).ShouldContain("# Summary");

            var jsonlPath = store.GetTracePath(sessionId);
            File.Exists(jsonlPath).ShouldBeTrue();
            (await File.ReadAllTextAsync(jsonlPath)).ShouldContain("\"runId\"");
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public async Task LoadLatestAsync_ShouldReturnLatestN_InChronologicalOrder()
    {
        var root = CreateTempSystemRoot();
        try
        {
            var sessionId = "test002";

            var env = new TestHostEnvironment(Path.Combine(root, "src", "VibeResearching.Api"));
            var ws = new WorkspaceService(env, NullLogger<WorkspaceService>.Instance);
            var store = new TraceStore(ws, NullLogger<TraceStore>.Instance);

            for (var i = 0; i < 4; i++)
            {
                var s = new SraRoundSummary
                {
                    SessionId = sessionId,
                    RunId = $"run{i}",
                    RoundIndex = i,
                    TriggerKind = "user_message",
                    TriggerRef = $"r{i}",
                    UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
                };
                await store.AppendAsync(sessionId, s, $"# run{i}", CancellationToken.None);
            }

            var latest2 = await store.LoadLatestAsync(sessionId, 2, CancellationToken.None);
            latest2.Count.ShouldBe(2);
            latest2[0].RunId.ShouldBe("run2");
            latest2[1].RunId.ShouldBe("run3");
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    private static string CreateTempSystemRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sra_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "src", "VibeResearching.Api"));
        return root;
    }

    private static void TryDeleteDir(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
            ContentRootFileProvider = new PhysicalFileProvider(contentRootPath);
        }

        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "VibeResearching.Tests";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
    }
}


