using ScientificResearchAssistant.Vibe.Tools;

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
    private readonly IVibeGraphAccess? _graphAccess;

    public VibeDagBuilderAgent(IVibeGraphAccess? graphAccess = null)
    {
        _graphAccess = graphAccess;

        SystemPrompt =
            """
            You are a DAG builder for a research derivation graph.

            Task:
            - Propose a minimal, consistent DAG mutation that captures new derivations from this round.
            - If nothing is ready, output an EMPTY mutation with nodes=[] and edges=[].
            - If the librarian provides "trusted axioms" with citations, you SHOULD include them as AXIOM nodes.
              Treat them as axioms (no verifier step required) but keep citations in tags.

            Helpful tools (optional):
            - You MAY call graph_get_snapshot to see existing node ids/types and reuse them.
            - You MAY call graph_explain_node(nodeId) to understand dependencies and avoid cycles.

            Rules:
            - Output STRICT JSON ONLY (no markdown, no code fences).
            - Node.id should be stable and short (e.g. "thm_pythagoras_v1").
            - Edge semantics: dependency -> dependent (from -> to).
            - Use edge.type "depends_on" unless a stronger type is clearly justified.
            - Keep label <= 200 chars; proof <= 1200 chars.
            - For axioms from papers, put citation/source in tags, e.g.:
              tags: { "sourcePath": "...", "citation": "...", "trusted": "paper" }.

            IMPORTANT - Provenance tracking:
            - Each knowledge node MUST specify "motivatedByPlanNodeId" to link it to the plan step that motivated its creation.
            - The plan node IDs follow the pattern: plan_{sessionId}_ms_r{roundIndex} (e.g., plan_abc123_ms_r1 for Round 1).
            - Look at the current round context to determine which milestone/plan node is being executed.

            Schema (updated):
            {
              "mutationId": "string",
              "authorAgent": "dag_builder",
              "nodes": [
                {
                  "id": "string",
                  "type": "axiom|theorem|assumption|hypothesis|unknown",
                  "kind": "knowledge",
                  "label": "string",
                  "proof": "string",
                  "motivatedByPlanNodeId": "string (required for knowledge nodes)",
                  "tags": { "k": "v" }
                }
              ],
              "edges": [
                { "from": "string", "to": "string", "type": "depends_on" }
              ]
            }
            """;
    }

    protected override async Task RegisterToolsAsync(CancellationToken cancellationToken = default)
    {
        await base.RegisterToolsAsync(cancellationToken);

        // Read-only graph tools for higher-quality, stable mutations.
        if (_graphAccess != null)
        {
            await RegisterToolAsync(new GraphGetSnapshotTool(_graphAccess), cancellationToken: cancellationToken);
            await RegisterToolAsync(new GraphExplainNodeTool(_graphAccess), cancellationToken: cancellationToken);
        }
    }
}


