namespace ScientificResearchAssistant.Vibe;

// ============================================================
//  VibeDagBuilderAgent
//
//  Role:
//  - Extract a candidate DAG mutation (nodes/edges) from the round context.
//  - This is a *candidate* only; final acceptance is gated by DAG consensus (default: verifier-quorum).
//
//  Output:
//  - STRICT JSON ONLY (no markdown, no code fences).
//  - Schema:
//    {
//      "mutationId": "string",
//      "authorAgent": "dag_builder",
//      "nodes": [
//        { "id":"string", "type":"axiom|theorem|assumption|hypothesis|unknown", "label":"string", "proof":"string", "tags": { "k":"v" } }
//      ],
//      "edges": [
//        { "from":"string", "to":"string", "type":"depends_on" }
//      ]
//    }
//
//  Notes:
//  - Keep label/proof bounded; do not invent large proofs.
//  - Prefer stable ids (slug-like) and reuse existing ids if provided.
// ============================================================

public sealed class VibeDagBuilderAgent : VibeAgentBase
{
    private readonly ScientificResearchAssistant.Vibe.Tools.IVibeDagAccess? _dag;

    public VibeDagBuilderAgent(ScientificResearchAssistant.Vibe.Tools.IVibeDagAccess? dag = null)
    {
        _dag = dag;

        SystemPrompt =
            """
            You are a DAG builder for a research derivation graph.

            Task:
            - Propose a minimal, consistent DAG mutation that captures new derivations from this round.
            - If nothing is ready, output an EMPTY mutation with nodes=[] and edges=[].
            - If the librarian provides "trusted axioms" with citations, you SHOULD include them as AXIOM nodes.
              Treat them as axioms (no verifier step required) but keep citations in tags.

            Helpful tools (optional):
            - You MAY call dag_get_snapshot to see existing node ids/types and reuse them.
            - You MAY call dag_explain(nodeId) to understand dependencies and avoid cycles.

            Rules:
            - Output STRICT JSON ONLY (no markdown, no code fences).
            - Node.id should be stable and short (e.g. "thm_pythagoras_v1").
            - Edge semantics: dependency -> dependent (from -> to).
            - Use edge.type "depends_on" unless a stronger type is clearly justified.
            - Keep label <= 200 chars; proof <= 1200 chars.
            - For axioms from papers, put citation/source in tags, e.g.:
              tags: { "sourcePath": "...", "citation": "...", "trusted": "paper" }.
            """;
    }

    protected override async Task RegisterToolsAsync(CancellationToken cancellationToken = default)
    {
        await base.RegisterToolsAsync(cancellationToken);

        // Read-only DAG tools for higher-quality, stable mutations.
        if (_dag != null)
        {
            await RegisterToolAsync(new ScientificResearchAssistant.Vibe.Tools.DagGetSnapshotTool(_dag), cancellationToken: cancellationToken);
            await RegisterToolAsync(new ScientificResearchAssistant.Vibe.Tools.DagExplainTool(_dag), cancellationToken: cancellationToken);
        }
    }
}


