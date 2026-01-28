namespace Aevatar.VibeResearching.HttpApi.Host.MinimalApis;

public static partial class LlmSecretsApi
{
    private static class ProviderProfiles
    {
        private static readonly IReadOnlyList<ProviderProfile> Profiles = new[]
        {
            // Popular
            new ProviderProfile("openai", "OpenAI", "popular", "Connect with API key", LlmProviderKind.OpenAiCompatible, "https://api.openai.com", "gpt-4o-mini", Recommended: true),
            new ProviderProfile("anthropic", "Anthropic", "popular", "Connect with Claude API key", LlmProviderKind.Anthropic, "https://api.anthropic.com", "claude-3-5-sonnet-latest"),
            // Alias: some users think in terms of "Claude" rather than "Anthropic"
            new ProviderProfile("claude", "Claude (Anthropic)", "popular", "Connect with Claude API key", LlmProviderKind.Anthropic, "https://api.anthropic.com", "claude-3-5-sonnet-latest"),
            new ProviderProfile("google", "Google", "popular", "Connect with Gemini API key", LlmProviderKind.Google, "https://generativelanguage.googleapis.com", "models/gemini-1.5-flash"),
            // Alias: some users think in terms of "Gemini" rather than "Google"
            new ProviderProfile("gemini", "Gemini (Google)", "popular", "Connect with Gemini API key", LlmProviderKind.Google, "https://generativelanguage.googleapis.com", "models/gemini-1.5-flash"),
            new ProviderProfile("openrouter", "OpenRouter", "popular", "Bring your own key (OpenAI compatible)", LlmProviderKind.OpenAiCompatible, "https://openrouter.ai/api/v1", "openai/gpt-4o-mini"),

            // Other (common in Aevatar demos)
            new ProviderProfile("deepseek", "DeepSeek", "other", "OpenAI-compatible API key", LlmProviderKind.OpenAiCompatible, "https://api.deepseek.com", "deepseek-chat"),
            new ProviderProfile("dashscope", "DashScope", "other", "Alibaba Qwen API key", LlmProviderKind.OpenAiCompatible, "https://dashscope.aliyuncs.com/compatible-mode", "qwen-plus"),
            new ProviderProfile("groq", "Groq", "other", "OpenAI-compatible API key", LlmProviderKind.OpenAiCompatible, "https://api.groq.com/openai", "llama-3.1-8b-instant"),
            new ProviderProfile("mistral", "Mistral", "other", "API key", LlmProviderKind.OpenAiCompatible, "https://api.mistral.ai", "mistral-small-latest"),
            new ProviderProfile("together", "Together", "other", "API key", LlmProviderKind.OpenAiCompatible, "https://api.together.xyz", "meta-llama/Meta-Llama-3.1-8B-Instruct-Turbo"),

            // Azure OpenAI is supported by Aevatar runtime but probing it is not stable without api-version/deployment info.
            new ProviderProfile("azureopenai", "Azure OpenAI", "other", "Azure key (requires endpoint in appsettings)", LlmProviderKind.OpenAiCompatible, "", "", Recommended: false),
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

            // Unknown provider name: treat as OpenAI-compatible with no default endpoint.
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
}


