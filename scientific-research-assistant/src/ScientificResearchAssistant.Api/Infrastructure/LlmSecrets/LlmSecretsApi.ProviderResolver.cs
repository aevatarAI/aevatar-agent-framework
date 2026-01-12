using Aevatar.Agents.Core.Secrets;

namespace ScientificResearchAssistant.Api.Infrastructure;

public static partial class LlmSecretsApi
{
    private static class LlmProviderResolver
    {
        public static ResolvedProvider Resolve(IAevatarUserSecretsStore secrets, string providerName)
        {
            var name = (providerName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name) || string.Equals(name, "default", StringComparison.OrdinalIgnoreCase))
            {
                if (secrets.TryGet(LlmDefaultProviderKey, out var def) && !string.IsNullOrWhiteSpace(def))
                    name = def.Trim();
                else
                    name = "default";
            }

            // ProviderType resolution:
            // - Prefer explicit ProviderType in secrets (supports multi-instance names like openai-gpt-4o-mini).
            // - Fallback: infer from "<provider>-<model>" naming convention.
            // - Final fallback: treat providerName as providerType.
            var providerTypeSource = "missing";
            var providerType = string.Empty;
            var providerTypePath = $"LLMProviders:Providers:{name}:ProviderType";
            if (secrets.TryGet(providerTypePath, out var ptFromSecrets) && !string.IsNullOrWhiteSpace(ptFromSecrets))
            {
                providerTypeSource = "secret";
                providerType = ptFromSecrets.Trim();
            }
            else if (ProviderProfiles.TryInferProviderTypeFromInstanceName(name, out var inferred))
            {
                providerTypeSource = "inferred";
                providerType = inferred;
            }
            else
            {
                providerType = name;
            }

            var profile = ProviderProfiles.Get(providerType);

            var apiKeyPath = $"LLMProviders:Providers:{name}:ApiKey";
            var endpointPath = $"LLMProviders:Providers:{name}:Endpoint";
            var modelPath = $"LLMProviders:Providers:{name}:Model";

            var apiKeyConfigured = secrets.TryGet(apiKeyPath, out var apiKey) && !string.IsNullOrWhiteSpace(apiKey);
            apiKey = apiKeyConfigured ? apiKey : string.Empty;

            var endpointSource = "missing";
            var endpoint = string.Empty;
            if (secrets.TryGet(endpointPath, out var endpointFromSecrets) && !string.IsNullOrWhiteSpace(endpointFromSecrets))
            {
                endpointSource = "secret";
                endpoint = endpointFromSecrets.Trim();
            }
            else if (!string.IsNullOrWhiteSpace(profile.DefaultEndpoint))
            {
                endpointSource = "default";
                endpoint = profile.DefaultEndpoint.Trim();
            }

            var modelSource = "missing";
            var model = string.Empty;
            if (secrets.TryGet(modelPath, out var modelFromSecrets) && !string.IsNullOrWhiteSpace(modelFromSecrets))
            {
                modelSource = "secret";
                model = modelFromSecrets.Trim();
            }
            else if (!string.IsNullOrWhiteSpace(profile.DefaultModel))
            {
                modelSource = "default";
                model = profile.DefaultModel.Trim();
            }

            var pub = new ResolvedProviderPublic(
                ProviderName: name,
                ProviderType: providerType,
                ProviderTypeSource: providerTypeSource,
                DisplayName: profile.DisplayName,
                Kind: profile.Kind.ToString(),
                ApiKeyConfigured: apiKeyConfigured,
                Endpoint: endpoint,
                EndpointSource: endpointSource,
                Model: model,
                ModelSource: modelSource);

            return new ResolvedProvider(
                ProviderName: name,
                ProviderType: providerType,
                ProviderTypeSource: providerTypeSource,
                DisplayName: profile.DisplayName,
                Kind: profile.Kind,
                Endpoint: endpoint,
                EndpointSource: endpointSource,
                Model: model,
                ModelSource: modelSource,
                ApiKeyConfigured: apiKeyConfigured,
                ApiKey: apiKeyConfigured ? apiKey!.Trim() : string.Empty,
                Public: pub);
        }
    }
}


