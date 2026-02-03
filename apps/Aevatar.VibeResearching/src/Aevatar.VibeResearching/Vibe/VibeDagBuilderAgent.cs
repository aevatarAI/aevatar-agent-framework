using VibeResearching.Vibe.Tools;

namespace VibeResearching.Vibe;

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
            
            CRITICAL - Extract knowledge from verifier output:
            - The verifier output contains verified knowledge items (axioms, theorems, definitions) that have been checked.
            - You MUST extract and include these verified items as DAG nodes:
              * AXIOM nodes: Extract from verifier output when it mentions verified axioms or foundational statements.
              * THEOREM nodes: Extract from verifier output when it mentions verified theorems or proven statements.
              * ASSUMPTION/DEFINITION nodes: Extract from verifier output when it mentions verified definitions or assumptions.
            - For each extracted knowledge item:
              * Use the exact statement from verifier output as the "label".
              * Set "type" to "axiom", "theorem", "assumption", or "definition" based on verifier's classification.
              * If verifier marked it as "VERIFIED", include tag: { "verification_status": "verified", "verified_by": "verifier" }.
              * If verifier marked it as "NOT VERIFIED" or "INCONCLUSIVE", you may still include it but mark appropriately in tags.
              * CRITICAL - Extract proof or verification method:
                - First, try to extract proof from verifier output (verification method, check steps, reasoning).
                - If verifier output doesn't contain proof, extract from reasoner output (derivation steps, logical reasoning, key insights).
                - If neither contains proof, extract from planner output (key steps, methodology).
                - The "proof" field should contain a concise summary of how the knowledge item was derived or verified (max 1200 chars).
                - For axioms, proof can be empty or contain citation/source information.
                - For theorems, proof should contain key derivation steps or verification method.
            - Priority: Items marked as "VERIFIED" by verifier should be included first.
            - Cross-reference with reasoner output: If reasoner listed axioms/theorems/definitions and verifier verified them, 
              combine the information (use reasoner's complete statement, verifier's verification status).
            - Proof extraction priority: verifier verification method > reasoner derivation steps > planner methodology > empty.

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
            - CRITICAL: Use the EXACT plan node ID from the "Plan:" section in the context (e.g., "plan_abc_123_ms_r1").
              Do NOT construct the ID yourself - copy it exactly as shown in the plan context.
            - Look at the current round context to determine which milestone/plan node is being executed.
            
            CRITICAL - Page range tracking:
            - If the milestone goal or plan context mentions specific page ranges (e.g., "pages 41-63", "第41-63页", "sections 10-12"),
              you MUST extract and add this information to the node's tags as "pageRange" or "sourcePages".
            - Format: Use the exact page range mentioned in the milestone goal (e.g., "41-63", "pages 1-20", "第21-40页").
            - If the knowledge item is extracted from a specific section, also add "section" tag (e.g., "section": "10-12").
            - This helps track which pages/sections have been covered and improves milestone completion evaluation.
            - Example tags: { "pageRange": "41-63", "section": "10-12", "sourcePages": "pages 41-63" }

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


