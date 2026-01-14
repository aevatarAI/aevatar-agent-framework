using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Core.Utils;
using Aevatar.Agents.AI.Tool.Abstractions;
using ScientificResearchAssistant.Streaming;

namespace ScientificResearchAssistant.Vibe;

// ============================================================
//  VibeAgentBase
//
//  What:
//  - Shared base for "vibe researching" multi-agent roles.
//
//  Why:
//  - Inject materials context (NotebookLM-style sources) into system prompt.
//  - Reuse ResearchToolManager wrapper to emit tool progress events via AsyncLocal.
// ============================================================

public abstract class VibeAgentBase : AIGAgentBase
{
    public const string MaterialsContextKey = "materials_context";

    // ------------------------------------------------------------
    //  YAML-driven tool allowlist (baseline policy)
    //
    //  WHY:
    //  - We want roles to be configurable via ~/.aevatar/agents/{role}.yaml.
    //  - AIGAgentBase already supports per-request tool allowlist via AIGAgentKeys.ToolAllowlist.
    //
    //  NOTE:
    //  - This is a "baseline" allowlist injected into every LLM request.
    //  - In-request allowlists (e.g. skills_load returning allowedTools) can still override it.
    // ------------------------------------------------------------

    protected VibeAgentBase()
    {
        // Keep agent state bounded so reconnect snapshots stay fast.
        EnableChatHistoryInState = true;
        EnableChatHistoryCompaction = true;
        ChatHistoryMaxMessages = 24;
        ChatHistorySummaryMaxChars = 6000;
    }

    protected override IAevatarToolManager CreateToolManager()
    {
        // Emit tool start/end events into the current stream sink (best-effort).
        return new ResearchToolManager(base.CreateToolManager());
    }

    protected override AevatarLLMRequest BuildLLMRequest(ChatRequest request)
    {
        var llm = base.BuildLLMRequest(request);

        // Materials grounding (MVP): append into system prompt.
        if (request?.Context != null &&
            request.Context.TryGetValue(MaterialsContextKey, out var raw) &&
            raw is string context &&
            !string.IsNullOrWhiteSpace(context))
        {
            llm.SystemPrompt = $"{llm.SystemPrompt}\n\nMaterials context:\n{context.Trim()}\n";
        }

        return llm;
    }
}


