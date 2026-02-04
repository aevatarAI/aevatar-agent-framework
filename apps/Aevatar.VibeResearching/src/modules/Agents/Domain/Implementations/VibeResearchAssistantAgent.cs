using Microsoft.Extensions.Logging;
using Aevatar.VibeResearching.Agents.Contracts.Collab;
using Aevatar.VibeResearching.Agents.Contracts.Sessions;
using Aevatar.VibeResearching.Agents.Tools;

namespace Aevatar.VibeResearching.Agents;

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

    public VibeResearchAssistantAgent(IVibeGraphAccess? graphAccess = null)
    {
        _graphAccess = graphAccess;

        SystemPrompt =
            """
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
