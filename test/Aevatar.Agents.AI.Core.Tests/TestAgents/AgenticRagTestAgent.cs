using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Core.AgenticRag;

namespace Aevatar.Agents.AI.Core.Tests.TestAgents;

// ============================================================
//  Test Agent for Agentic RAG loop (deterministic, no LLM/network)
// ============================================================

public sealed class AgenticRagTestAgent : AgenticRagGAgent
{
    private readonly IAgenticRagPlanner _planner;
    private readonly IAgenticRagRetriever _retriever;
    private readonly IAgenticRagSynthesizer _synthesizer;
    private readonly IAgenticRagCritic _critic;

    public AgenticRagTestAgent(
        IAgenticRagPlanner planner,
        IAgenticRagRetriever retriever,
        IAgenticRagSynthesizer synthesizer,
        IAgenticRagCritic critic)
    {
        _planner = planner;
        _retriever = retriever;
        _synthesizer = synthesizer;
        _critic = critic;

        // Avoid needing Actor layer in unit tests.
        EventPublisher = NullEventPublisher.Instance;
        _isInitialized = true;
    }

    protected override IAgenticRagRetriever CreateRetriever() => _retriever;
    protected override IAgenticRagPlanner CreatePlanner() => _planner;
    protected override IAgenticRagSynthesizer CreateSynthesizer() => _synthesizer;
    protected override IAgenticRagCritic CreateCritic() => _critic;
}


