using Aevatar.Agents.AI.Core.AgenticRag;
using Aevatar.Agents.AI.Core.Messages;

namespace AgenticRagDemo;

// ============================================================
//  DemoAgent
//
//  中文 + ASCII:
//  - 这个示例不依赖真实 LLM/网络：通过自定义 Retriever 返回可引用的证据。
//  - 如果你想接入真实检索/LLM：覆盖 CreatePlanner/CreateSynthesizer/CreateCritic 并在其中调用 LLM。
// ============================================================

public sealed class DemoAgent : AgenticRagGAgent
{
    protected override IAgenticRagRetriever CreateRetriever()
        => new DemoRetriever();

    // Keep the demo minimal: disable tool registration (tool loop not needed for this demo).
    protected override Task RegisterToolsAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    private sealed class DemoRetriever : IAgenticRagRetriever
    {
        public string Name => "demo_in_memory";

        public Task<IReadOnlyList<RagEvidenceSummary>> RetrieveAsync(
            RagRetrieveRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Two tiny evidence items with URI citations.
            var e1 = new RagEvidenceSummary
            {
                EvidenceId = "demo:1",
                Snippet = "Aevatar is an actor-model distributed agent framework; events are the communication primitive.",
                Citation = new RagCitation
                {
                    Uri = new UriCitation
                    {
                        Uri = "aevatar://demo/guide",
                        Start = 0,
                        End = 0,
                        Fragment = "overview"
                    }
                },
                Score = 1.0
            };
            e1.Tags["source"] = "demo";

            var e2 = new RagEvidenceSummary
            {
                EvidenceId = "demo:2",
                Snippet = "Cross-boundary state/event/config must be Protobuf; keep evidence snippets bounded and cite sources.",
                Citation = new RagCitation
                {
                    Uri = new UriCitation
                    {
                        Uri = "aevatar://demo/contracts",
                        Start = 0,
                        End = 0,
                        Fragment = "protobuf-first"
                    }
                },
                Score = 0.9
            };
            e2.Tags["source"] = "demo";

            return Task.FromResult<IReadOnlyList<RagEvidenceSummary>>([e1, e2]);
        }
    }
}


