using AgenticRagDemo;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Core.AgenticRag;
using Aevatar.Agents.Runtime.Local;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// ============================================================
//  AgenticRagDemo (Local runtime)
//
//  Goal:
//  - Demonstrate AgenticRagGAgent end-to-end WITHOUT real LLM/network.
//  - Print answer + citations + diagnostics.
// ============================================================

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddLogging(b =>
        {
            b.AddConsole();
            b.SetMinimumLevel(LogLevel.Information);
        });

        // Local runtime
        services.AddAevatarLocalRuntime();

        // Demo-only Noop provider so AIGAgentBase.InitializeAsync() can succeed without secrets/network.
        services.AddSingleton<ILLMProviderFactory, NoopLLMProviderFactory>();
    })
    .Build();

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("AgenticRagDemo");
var actorFactory = host.Services.GetRequiredService<IGAgentActorFactory>();

logger.LogInformation("▶ Creating DemoAgent (Local runtime)...");
var actor = await actorFactory.CreateGAgentActorAsync<DemoAgent>();
var agent = (DemoAgent)actor.GetAgent();

logger.LogInformation("▶ Initializing agent with Noop LLM provider (no network)...");
await agent.InitializeAsync("noop");

logger.LogInformation("▶ Running Agentic RAG...");
var response = await agent.AnswerAsync(new AgenticRagRequest
{
    RequestId = Guid.NewGuid().ToString("N"),
    Query = "What is Aevatar Agent Framework? What are the key rules for Agentic RAG?",
    Budget = new RagBudget
    {
        MaxIterations = 2,
        MaxEvidenceItems = 4,
        MaxEvidenceChars = 200
    }
});

logger.LogInformation("\n=== Answer ===\n{Answer}\n", response.Answer);

logger.LogInformation("=== Evidence (with citations) ===");
foreach (var e in response.Evidence)
{
    var citation = e.Citation.RefCase switch
    {
        Aevatar.Agents.AI.Core.Messages.RagCitation.RefOneofCase.Uri =>
            $"uri={e.Citation.Uri.Uri} fragment={e.Citation.Uri.Fragment}",
        Aevatar.Agents.AI.Core.Messages.RagCitation.RefOneofCase.MemoryEntry =>
            $"memory_id={e.Citation.MemoryEntry.MemoryId} entry_id={e.Citation.MemoryEntry.EntryId}",
        _ => "none"
    };

    logger.LogInformation("- [{EvidenceId}] score={Score} {Citation}\n  {Snippet}",
        e.EvidenceId,
        e.Score,
        citation,
        e.Snippet);
}

logger.LogInformation("\n=== Diagnostics ===");
foreach (var (k, v) in response.Diagnostics.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
{
    logger.LogInformation("- {Key} = {Value}", k, v);
}

logger.LogInformation("\n✅ Demo complete.");


