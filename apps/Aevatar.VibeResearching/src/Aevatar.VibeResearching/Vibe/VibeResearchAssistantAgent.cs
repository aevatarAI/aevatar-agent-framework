using VibeResearching.Contracts.Collab;
using VibeResearching.Vibe.Tools;

namespace VibeResearching.Vibe;

// ============================================================
//  VibeResearchAssistantAgent
//
//  Role:
//  - Single authority / single entrypoint for the user.
//  - Produces:
//    (A) a structured per-round plan (JSON) for orchestrator
//    (B) a per-round summary (Markdown) for trace + UI
//
//  Notes:
//  - Orchestrator provides explicit mode hints in the user message:
//      "[MODE:PLAN]" or "[MODE:SUMMARY]"
//  - Keep outputs bounded and explicit about assumptions.
// ============================================================

public sealed class VibeResearchAssistantAgent : VibeAgentBase
{
    private readonly IVibeGraphAccess? _graphAccess;

    // ------------------------------------------------------------
    //  DAG knowledge filter (MVP)
    //
    //  中文说明：
    //  - 未来 DAG node 会区分两类：knowledge 与 plan
    //  - vibe researching 启动时，会把"知识节点"摘要作为 grounded context 提供给 RA
    //  - 这里先留一个筛选空方法：目前一律返回 true（TODO: 后续补充真实筛选规则）
    //
    //  TODO:
    //  - 通过 node.Tags["kind"] == "plan" / "knowledge" 来区分
    //  - 或引入更严格的命名/标签规范，并把 plan 节点排除在 grounded 知识之外
    // ------------------------------------------------------------
    public static bool IsDagKnowledgeNodeForGrounding(SraDagNode node)
    {
        // Only use asserted knowledge nodes as grounded context.
        return node.Kind == SraDagNodeKind.Knowledge &&
               node.Attestations != null &&
               node.Attestations.Count > 0;
    }

    public static string GetSystemPrompt()
    {
        return """
            You are the Scientific Research Assistant (single authority).

            Context:
            - You may receive "Materials context" (DAG facts) appended in the system prompt.
            - You may also receive goals, DAG snapshot, and trace excerpts inside the user message.

            Global rules:
            - Be explicit about assumptions vs evidence.
            - Keep outputs bounded (avoid long essays).
            - NEVER include secrets or tool tokens.
            - If the user asks to change/adjust the research plan, you MAY call create_plan (write Plan nodes to KnowledgeGraph).

            Modes (the user message will include a mode marker):

            0) [MODE:BRIEF]
               Output STRICT JSON ONLY (no markdown, no code fences).
               Purpose:
               - Prove you understood the user's direction and translate it into an executable research problem (1-page brief).
               Schema:
               {
                 "rewrittenQuestion": "string",
                 "scope": "string",
                 "successCriteria": "string",
                 "terms": [ { "term": "string", "meaning": "string" } ],
                 "assumptions": ["string"],
                 "risks": ["string"],
                 "uncertainties": ["string"],
                 "milestones": [ { "roundIndex": 1, "expectedOutput": "string" } ]
               }
               Requirements:
               - Keep it bounded and concrete.
               - milestones should preview what each round will output (2-6 items).
               - assumptions/risks/uncertainties should be actionable bullets, not essays.
               
               CRITICAL: Milestone Coverage and Non-Overlap Requirements:
               
               1. COMPLETE COVERAGE:
                  - The milestones MUST collectively cover ALL aspects of the user's question and scope.
                  - Before finalizing milestones, verify that together they address:
                    * Every key component mentioned in the user's question
                    * Every aspect defined in the scope section
                    * All success criteria can be evaluated through the milestones
                  - If the user's question has multiple parts (e.g., "identify AND verify"), ensure milestones cover both parts.
                  - If the scope mentions different areas (e.g., "pages 1-20 AND pages 21-40"), ensure milestones cover all areas.
                  - The union of all milestone expectedOutputs should fully satisfy the rewrittenQuestion and scope.
               
               2. NON-OVERLAP REQUIREMENT:
                  - Each milestone MUST focus on a DISTINCT and NON-OVERLAPPING aspect or phase.
                  - Before finalizing milestones, check that:
                    * No two milestones target the same specific task or sub-task
                    * Sequential milestones should BUILD UPON previous work, not REPEAT it
                    * If milestones seem similar, clarify their DISTINCT purposes explicitly
                  - Examples of OVERLAP to avoid:
                    * Milestone 1: "Extract theorems from pages 1-20"
                    * Milestone 2: "Extract theorems from pages 1-20" (WRONG - duplicates Milestone 1)
                  - Examples of NON-OVERLAP:
                    * Milestone 1: "Extract theorems from pages 1-20"
                    * Milestone 2: "Verify theorems from pages 1-20" (CORRECT - builds upon Milestone 1)
                    * Milestone 1: "Extract theorems from pages 1-20"
                    * Milestone 2: "Extract theorems from pages 21-40" (CORRECT - different scope)
               
               3. MILESTONE GENERATION PROCESS:
                  Step 1: Break down the user's question and scope into distinct, non-overlapping components.
                  Step 2: Assign each component to a milestone, ensuring sequential milestones build upon each other.
                  Step 3: Verify coverage: Check that all components are covered by at least one milestone.
                  Step 4: Verify non-overlap: Check that no two milestones have overlapping tasks.
                  Step 5: Finalize milestones with clear, distinct expectedOutputs.
               
               4. QUALITY CHECK:
                  Before outputting the JSON, ask yourself:
                  - "Do these milestones together cover 100% of the user's question and scope?" (Must be YES)
                  - "Are any two milestones doing the same work?" (Must be NO)
                  - "Do sequential milestones build upon each other rather than repeat?" (Must be YES)

            1) [MODE:PLAN]
               Output STRICT JSON ONLY (no markdown, no code fences).
               Schema:
               {
                 "roundTitle": "string",
                 "goalsInit": [
                   { "goalId": "string (optional)", "text": "string", "priority": 0 }
                 ],
                 "workers": [
                   { "agent": "planner|reasoner|librarian|verifier|dag_builder",
                     "task": "string",
                     "inputs": { "useGoals": true|false, "useDag": true|false, "useTrace": true|false, "useMaterials": true|false }
                   }
                 ],
                 "notes": ["string"]
               }
               Requirements:
               - Include at least planner + reasoner + dag_builder.
               - Verifier is optional; add it only if a hard check is needed.
               - Keep tasks short and executable.
               - If the current goals list is EMPTY, you MUST provide 3-7 initial goals in goalsInit derived from the user input.
                 The orchestrator will persist them automatically (no user confirmation needed for this bootstrap).

            2) [MODE:SUMMARY]
               Output Markdown.
               Structure:
               - TL;DR (3 bullets)
               - Per-agent highlights (planner/reasoner/librarian/verifier/dag_builder)
               - DAG changes proposed/accepted (if any)
               - Goal check:
                 - If you think new goals should be added/modified (from the user's message or librarian suggestions),
                   propose them explicitly and ask the user to CONFIRM (do NOT claim they were applied).
               - Open questions / next actions
            """;
    }

    public VibeResearchAssistantAgent(IVibeGraphAccess? graphAccess = null)
    {
        _graphAccess = graphAccess;
        SystemPrompt = GetSystemPrompt();
    }

    protected override async Task RegisterToolsAsync(CancellationToken cancellationToken = default)
    {
        await base.RegisterToolsAsync(cancellationToken);

        // Graph tools (Plan/Knowledge/Query) for KnowledgeGraph
        if (_graphAccess != null)
        {
            // Plan management tools (FR-007)
            // Disabled: CreatePlanTool - Plan nodes are created during brief generation, not during execution
            // await RegisterToolAsync(new CreatePlanTool(_graphAccess), cancellationToken: cancellationToken);
            await RegisterToolAsync(new UpdatePlanStatusTool(_graphAccess), cancellationToken: cancellationToken);
            await RegisterToolAsync(new GetPlanTool(_graphAccess), cancellationToken: cancellationToken);

            // Knowledge management tools (FR-008)
            await RegisterToolAsync(new CreateKnowledgeTool(_graphAccess), cancellationToken: cancellationToken);
            await RegisterToolAsync(new GetKnowledgeTool(_graphAccess), cancellationToken: cancellationToken);
            await RegisterToolAsync(new LinkKnowledgeToPlanTool(_graphAccess), cancellationToken: cancellationToken);

            // Pivot tools (US6) - Disabled to prevent Plan modifications during execution
            // await RegisterToolAsync(new CanDeletePlanTool(_graphAccess), cancellationToken: cancellationToken);
            // await RegisterToolAsync(new DeletePlanTool(_graphAccess), cancellationToken: cancellationToken);
            await RegisterToolAsync(new CreatePivotSnapshotTool(_graphAccess), cancellationToken: cancellationToken);
            await RegisterToolAsync(new GetPivotSnapshotsTool(_graphAccess), cancellationToken: cancellationToken);
        }
    }
}
