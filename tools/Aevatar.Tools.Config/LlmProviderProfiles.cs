// ============================================================
//  ProviderProfiles
//
//  LLMTornado 支持的所有 Provider 类型
//  参考: https://github.com/lofcz/LlmTornado
// ============================================================

static class ProviderProfiles
{
    private static readonly IReadOnlyList<ProviderProfile> Profiles = new[]
    {
        // ─────────────────────────────────────────────────────────────────
        // TIER 1: Major Cloud Providers (高流量、高可用)
        // ─────────────────────────────────────────────────────────────────
        new ProviderProfile("openai", "OpenAI", "tier1", "GPT-4o, o1, o3 series", LlmProviderKind.OpenAi, "https://api.openai.com", "gpt-4o-mini", Recommended: true),
        new ProviderProfile("anthropic", "Anthropic", "tier1", "Claude 3.5 Sonnet, Opus, Haiku", LlmProviderKind.Anthropic, "https://api.anthropic.com", "claude-sonnet-4-20250514", Recommended: true),
        new ProviderProfile("google", "Google", "tier1", "Gemini 2.0, 1.5 Pro/Flash", LlmProviderKind.Google, "https://generativelanguage.googleapis.com", "gemini-2.0-flash"),
        new ProviderProfile("azure", "Azure OpenAI", "tier1", "Enterprise OpenAI (requires deployment)", LlmProviderKind.Azure, "", ""),

        // ─────────────────────────────────────────────────────────────────
        // TIER 2: Popular Alternatives (性价比、特色功能)
        // ─────────────────────────────────────────────────────────────────
        new ProviderProfile("deepseek", "DeepSeek", "tier2", "DeepSeek V3, Coder, Reasoner", LlmProviderKind.DeepSeek, "https://api.deepseek.com", "deepseek-chat", Recommended: true),
        new ProviderProfile("mistral", "Mistral", "tier2", "Mistral Large, Small, Codestral", LlmProviderKind.Mistral, "https://api.mistral.ai", "mistral-small-latest"),
        new ProviderProfile("groq", "Groq", "tier2", "Ultra-fast inference (Llama, Mixtral)", LlmProviderKind.Groq, "https://api.groq.com/openai", "llama-3.3-70b-versatile"),
        new ProviderProfile("xai", "xAI", "tier2", "Grok-2, Grok-2 Vision", LlmProviderKind.XAi, "https://api.x.ai", "grok-2-latest"),
        new ProviderProfile("cohere", "Cohere", "tier2", "Command R+, Embed, Rerank", LlmProviderKind.Cohere, "https://api.cohere.com", "command-r-plus"),
        new ProviderProfile("perplexity", "Perplexity", "tier2", "Sonar Pro (search-augmented)", LlmProviderKind.Perplexity, "https://api.perplexity.ai", "sonar-pro"),

        // ─────────────────────────────────────────────────────────────────
        // TIER 3: Aggregators & Routers (聚合多个后端)
        // ─────────────────────────────────────────────────────────────────
        new ProviderProfile("openrouter", "OpenRouter", "aggregator", "200+ models, unified API", LlmProviderKind.OpenRouter, "https://openrouter.ai/api/v1", "openai/gpt-4o-mini", Recommended: true),
        new ProviderProfile("deepinfra", "DeepInfra", "aggregator", "Open-source models, GPU inference", LlmProviderKind.DeepInfra, "https://api.deepinfra.com/v1/openai", "meta-llama/Llama-3.3-70B-Instruct-Turbo"),
        new ProviderProfile("requesty", "Requesty", "aggregator", "Smart routing, cost optimization", LlmProviderKind.OpenAiCompatible, "https://router.requesty.ai/v1", ""),
        new ProviderProfile("together", "Together AI", "aggregator", "Open-source models at scale", LlmProviderKind.Together, "https://api.together.xyz", "meta-llama/Llama-3.3-70B-Instruct-Turbo"),

        // ─────────────────────────────────────────────────────────────────
        // TIER 4: Regional / Specialized (区域特色、垂直领域)
        // ─────────────────────────────────────────────────────────────────
        new ProviderProfile("alibaba", "Alibaba (Qwen)", "regional", "Qwen series via DashScope", LlmProviderKind.OpenAiCompatible, "https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen-plus"),
        new ProviderProfile("moonshot", "Moonshot AI", "regional", "Kimi series (China)", LlmProviderKind.OpenAiCompatible, "https://api.moonshot.cn/v1", "moonshot-v1-8k"),
        new ProviderProfile("zhipu", "Zhipu AI", "regional", "GLM-4 series (China)", LlmProviderKind.OpenAiCompatible, "https://open.bigmodel.cn/api/paas/v4", "glm-4-flash"),
        new ProviderProfile("upstage", "Upstage", "regional", "Solar series (Korea)", LlmProviderKind.OpenAiCompatible, "https://api.upstage.ai/v1/solar", "solar-pro"),
        new ProviderProfile("voyage", "Voyage AI", "embedding", "Specialized embeddings", LlmProviderKind.OpenAiCompatible, "https://api.voyageai.com/v1", "voyage-3"),
        
        // ─────────────────────────────────────────────────────────────────
        // TIER 5: Research / Experimental (学术、实验性)
        // ─────────────────────────────────────────────────────────────────
        new ProviderProfile("blablador", "Blablador", "experimental", "EU research infrastructure", LlmProviderKind.OpenAiCompatible, "https://api.blablador.org/v1", ""),
        new ProviderProfile("zai", "Z.ai", "experimental", "Specialized AI services", LlmProviderKind.OpenAiCompatible, "", ""),

        // ─────────────────────────────────────────────────────────────────
        // LOCAL: Self-hosted (本地部署)
        // ─────────────────────────────────────────────────────────────────
        new ProviderProfile("ollama", "Ollama", "local", "Local models (llama3, qwen, etc.)", LlmProviderKind.OpenAiCompatible, "http://localhost:11434/v1", "llama3.2"),
        new ProviderProfile("lmstudio", "LM Studio", "local", "Local GUI with OpenAI API", LlmProviderKind.OpenAiCompatible, "http://localhost:1234/v1", ""),
        new ProviderProfile("vllm", "vLLM", "local", "High-throughput local serving", LlmProviderKind.OpenAiCompatible, "http://localhost:8000/v1", ""),
        new ProviderProfile("llamacpp", "llama.cpp", "local", "Lightweight local inference", LlmProviderKind.OpenAiCompatible, "http://localhost:8080/v1", ""),
    };

    public static IReadOnlyList<ProviderProfile> All => Profiles;

    public static ProviderProfile Get(string providerName)
    {
        var name = (providerName ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(name))
        {
            var match = Profiles.FirstOrDefault(p => string.Equals(p.Id, name, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return match;
        }

        return new ProviderProfile(name, name, "configured", "Configured via user secrets", LlmProviderKind.OpenAiCompatible, "", "");
    }

    public static bool TryInferProviderTypeFromInstanceName(string instanceName, out string providerType)
    {
        providerType = string.Empty;
        var name = (instanceName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (Profiles.Any(p => string.Equals(p.Id, name, StringComparison.OrdinalIgnoreCase)))
        {
            providerType = name;
            return true;
        }

        var idx = name.IndexOf('-', StringComparison.Ordinal);
        if (idx <= 0)
            return false;

        var head = name.Substring(0, idx).Trim();
        if (string.IsNullOrWhiteSpace(head))
            return false;

        if (Profiles.Any(p => string.Equals(p.Id, head, StringComparison.OrdinalIgnoreCase)))
        {
            providerType = head;
            return true;
        }

        return false;
    }
}
