using System.Text.Json;
using Aevatar.Agents.Core.Secrets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Aevatar.VibeResearching.HttpApi.Host.MinimalApis;

// ============================================================
//  LlmSecretsApi (Endpoints)
//
//  中文说明：
//  - SRA 内置一份 “Secrets API” 来替代单独跑 Aevatar.Config
//  - 所有写入接口严格 loopback-only（防止远程误用）
// ============================================================

public static partial class LlmSecretsApi
{
    private const string LlmDefaultProviderKey = "LLMProviders:Default";

    public static void MapLlmSecretsApi(this WebApplication app)
    {
        // Providers catalog (base types)
        app.MapGet("/api/llm/providers", (
            IAevatarUserSecretsStore secrets,
            HttpContext http) =>
        {
            if (!IsLocal(http))
                return Results.Json(new { ok = false, error = "Forbidden: local access only" }, statusCode: 403);

            var providers = ProviderCatalog.BuildProviderTypes(secrets);
            return Results.Json(new { ok = true, providers });
        });

        // Configured provider instances (multi-model)
        app.MapGet("/api/llm/instances", (
            IAevatarUserSecretsStore secrets,
            HttpContext http) =>
        {
            if (!IsLocal(http))
                return Results.Json(new { ok = false, error = "Forbidden: local access only" }, statusCode: 403);

            var instances = ProviderCatalog.BuildInstances(secrets);
            return Results.Json(new { ok = true, instances });
        });

        // ------------------------------------------------------------
        // Embeddings (global fallback) - Aliyun/DashScope (OpenAI-compatible)
        // Stored at: LLMProviders:Embeddings:*
        // ------------------------------------------------------------
        app.MapGet("/api/embeddings", (
            IAevatarUserSecretsStore secrets,
            HttpContext http) =>
        {
            if (!IsLocal(http))
                return Results.Json(new { ok = false, error = "Forbidden: local access only" }, statusCode: 403);

            secrets.TryGet("LLMProviders:Embeddings:ProviderType", out var providerType);
            secrets.TryGet("LLMProviders:Embeddings:Model", out var model);
            secrets.TryGet("LLMProviders:Embeddings:Endpoint", out var endpoint);
            var hasKey = secrets.TryGet("LLMProviders:Embeddings:ApiKey", out var apiKey) && !string.IsNullOrWhiteSpace(apiKey);

            bool? enabled = null;
            if (secrets.TryGet("LLMProviders:Embeddings:Enabled", out var enabledRaw) && !string.IsNullOrWhiteSpace(enabledRaw))
            {
                if (bool.TryParse(enabledRaw.Trim(), out var b))
                    enabled = b;
            }

            return Results.Json(new
            {
                ok = true,
                embeddings = new
                {
                    enabled,
                    providerType = (providerType ?? string.Empty).Trim(),
                    model = (model ?? string.Empty).Trim(),
                    endpoint = (endpoint ?? string.Empty).Trim(),
                    configured = hasKey,
                    masked = hasKey ? SecretMask.MaskMiddle((apiKey ?? string.Empty).Trim()) : string.Empty
                }
            });
        });

        app.MapGet("/api/embeddings/api-key", (
            bool? reveal,
            IAevatarUserSecretsStore secrets,
            HttpContext http) =>
        {
            if (!IsLocal(http))
                return Results.Json(new { ok = false, error = "Forbidden: local access only" }, statusCode: 403);

            if (!secrets.TryGet("LLMProviders:Embeddings:ApiKey", out var value) || string.IsNullOrWhiteSpace(value))
            {
                return Results.Json(new
                {
                    ok = true,
                    configured = false,
                    masked = ""
                });
            }

            var trimmed = value.Trim();
            var masked = SecretMask.MaskMiddle(trimmed);

            if (reveal == true)
            {
                return Results.Json(new
                {
                    ok = true,
                    configured = true,
                    masked,
                    value = trimmed
                });
            }

            return Results.Json(new
            {
                ok = true,
                configured = true,
                masked
            });
        });

        app.MapPost("/api/embeddings", (
            UpsertEmbeddingsRequest req,
            IAevatarUserSecretsStore secrets,
            HttpContext http) =>
        {
            if (!IsLocal(http))
                return Results.Json(new { ok = false, error = "Forbidden: local access only" }, statusCode: 403);

            if (req.Enabled.HasValue)
            {
                secrets.Set("LLMProviders:Embeddings:Enabled", req.Enabled.Value ? "true" : "false");
            }

            void SetOrRemove(string key, string? raw)
            {
                if (raw == null) return; // not provided
                var v = raw.Trim();
                if (string.IsNullOrWhiteSpace(v)) secrets.Remove(key);
                else secrets.Set(key, v);
            }

            SetOrRemove("LLMProviders:Embeddings:ProviderType", req.ProviderType);
            SetOrRemove("LLMProviders:Embeddings:Model", req.Model);
            SetOrRemove("LLMProviders:Embeddings:Endpoint", req.Endpoint);
            SetOrRemove("LLMProviders:Embeddings:ApiKey", req.ApiKey);

            return Results.Json(new { ok = true });
        });

        app.MapDelete("/api/embeddings", (
            IAevatarUserSecretsStore secrets,
            HttpContext http) =>
        {
            if (!IsLocal(http))
                return Results.Json(new { ok = false, error = "Forbidden: local access only" }, statusCode: 403);

            var removed = new Dictionary<string, bool>
            {
                ["LLMProviders:Embeddings:Enabled"] = secrets.Remove("LLMProviders:Embeddings:Enabled"),
                ["LLMProviders:Embeddings:ProviderType"] = secrets.Remove("LLMProviders:Embeddings:ProviderType"),
                ["LLMProviders:Embeddings:Model"] = secrets.Remove("LLMProviders:Embeddings:Model"),
                ["LLMProviders:Embeddings:Endpoint"] = secrets.Remove("LLMProviders:Embeddings:Endpoint"),
                ["LLMProviders:Embeddings:ApiKey"] = secrets.Remove("LLMProviders:Embeddings:ApiKey")
            };

            return Results.Json(new { ok = true, removed });
        });

        // Default provider (stored at LLMProviders:Default)
        app.MapGet("/api/llm/default", (
            IAevatarUserSecretsStore secrets,
            HttpContext http) =>
        {
            if (!IsLocal(http))
                return Results.Json(new { ok = false, error = "Forbidden: local access only" }, statusCode: 403);

            EnsureDefaultProviderKeyBestEffort(secrets, preferredProvider: null);
            var effective = ResolveEffectiveDefaultProviderName(secrets);
            return Results.Json(new { ok = true, providerName = effective });
        });

        app.MapPost("/api/llm/default", (
            SetLlmDefaultRequest req,
            IAevatarUserSecretsStore secrets,
            HttpContext http) =>
        {
            if (!IsLocal(http))
                return Results.Json(new { ok = false, error = "Forbidden: local access only" }, statusCode: 403);

            var name = (req.ProviderName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                return Results.BadRequest(new { ok = false, error = "providerName is required" });

            if (!IsProviderRunnable(secrets, name))
                return Results.BadRequest(new { ok = false, error = "providerName has no configured apiKey" });

            secrets.Set(LlmDefaultProviderKey, name);
            return Results.Json(new { ok = true, providerName = name });
        });

        // Provider details (never returns secret values)
        app.MapGet("/api/llm/provider/{providerName}", (
            string providerName,
            IAevatarUserSecretsStore secrets,
            HttpContext http) =>
        {
            if (!IsLocal(http))
                return Results.Json(new { ok = false, error = "Forbidden: local access only" }, statusCode: 403);

            var resolved = LlmProviderResolver.Resolve(secrets, providerName);
            return Results.Json(new { ok = true, provider = resolved.Public });
        });

        // Test connection (best-effort)
        app.MapGet("/api/llm/test/{providerName}", async (
            string providerName,
            IAevatarUserSecretsStore secrets,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!IsLocal(http))
                return Results.Json(new { ok = false, error = "Forbidden: local access only" }, statusCode: 403);

            var resolved = LlmProviderResolver.Resolve(secrets, providerName);
            var result = await LlmSecretsProbe.TestAsync(resolved, ct);
            return Results.Json(result);
        });

        // Fetch models (best-effort)
        app.MapGet("/api/llm/models/{providerName}", async (
            string providerName,
            IAevatarUserSecretsStore secrets,
            HttpContext http,
            int? limit,
            CancellationToken ct) =>
        {
            if (!IsLocal(http))
                return Results.Json(new { ok = false, error = "Forbidden: local access only" }, statusCode: 403);

            var resolved = LlmProviderResolver.Resolve(secrets, providerName);
            var result = await LlmSecretsProbe.FetchModelsAsync(resolved, limit ?? 200, ct);
            return Results.Json(result);
        });

        // API key status/mask/reveal (localhost-only)
        app.MapGet("/api/llm/api-key/{providerName}", (
            string providerName,
            bool? reveal,
            IAevatarUserSecretsStore secrets,
            HttpContext http) =>
        {
            if (!IsLocal(http))
                return Results.Json(new { ok = false, error = "Forbidden: local access only" }, statusCode: 403);

            var name = (providerName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                return Results.BadRequest(new { ok = false, error = "providerName is required" });

            var keyPath = $"LLMProviders:Providers:{name}:ApiKey";
            if (!secrets.TryGet(keyPath, out var value) || string.IsNullOrWhiteSpace(value))
            {
                return Results.Json(new
                {
                    ok = true,
                    providerName = name,
                    configured = false,
                    masked = ""
                });
            }

            var trimmed = value.Trim();
            var masked = SecretMask.MaskMiddle(trimmed);

            if (reveal == true)
            {
                return Results.Json(new
                {
                    ok = true,
                    providerName = name,
                    configured = true,
                    masked,
                    value = trimmed
                });
            }

            return Results.Json(new
            {
                ok = true,
                providerName = name,
                configured = true,
                masked
            });
        });

        // Legacy: set API key for a specific instance name
        // Also ensures ProviderType, Endpoint, and Model are set from profile defaults if missing.
        app.MapPost("/api/llm/api-key", (
            SetLlmApiKeyRequest req,
            IAevatarUserSecretsStore secrets,
            HttpContext http) =>
        {
            if (!IsLocal(http))
                return Results.Json(new { ok = false, error = "Forbidden: local access only" }, statusCode: 403);

            var providerName = (req.ProviderName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(providerName))
                return Results.BadRequest(new { ok = false, error = "providerName is required" });

            var apiKey = (req.ApiKey ?? "").Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
                return Results.BadRequest(new { ok = false, error = "apiKey is required" });

            var keyPath = $"LLMProviders:Providers:{providerName}:ApiKey";
            secrets.Set(keyPath, apiKey);

            // Ensure ProviderType, Endpoint, and Model are set from profile defaults if not already configured.
            // This fixes the issue where only API key is saved but endpoint/model use wrong defaults.
            var providerType = providerName;
            if (ProviderProfiles.TryInferProviderTypeFromInstanceName(providerName, out var inferred))
                providerType = inferred;

            var profile = ProviderProfiles.Get(providerType);

            var providerTypePath = $"LLMProviders:Providers:{providerName}:ProviderType";
            if (!secrets.TryGet(providerTypePath, out var existingPt) || string.IsNullOrWhiteSpace(existingPt))
                secrets.Set(providerTypePath, providerType);

            var endpointPath = $"LLMProviders:Providers:{providerName}:Endpoint";
            if (!secrets.TryGet(endpointPath, out var existingEp) || string.IsNullOrWhiteSpace(existingEp))
            {
                if (!string.IsNullOrWhiteSpace(profile.DefaultEndpoint))
                    secrets.Set(endpointPath, profile.DefaultEndpoint);
            }

            var modelPath = $"LLMProviders:Providers:{providerName}:Model";
            if (!secrets.TryGet(modelPath, out var existingModel) || string.IsNullOrWhiteSpace(existingModel))
            {
                if (!string.IsNullOrWhiteSpace(profile.DefaultModel))
                    secrets.Set(modelPath, profile.DefaultModel);
            }

            EnsureDefaultProviderKeyBestEffort(secrets, providerName);

            return Results.Json(new { ok = true, providerName, keyPath });
        });

        // Upsert instance (multi-model friendly).
        app.MapPost("/api/llm/instance", (
            UpsertLlmInstanceRequest req,
            IAevatarUserSecretsStore secrets,
            HttpContext http) =>
        {
            if (!IsLocal(http))
                return Results.Json(new { ok = false, error = "Forbidden: local access only" }, statusCode: 403);

            var name = (req.ProviderName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                return Results.BadRequest(new { ok = false, error = "providerName is required" });

            var providerType = (req.ProviderType ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(providerType))
                return Results.BadRequest(new { ok = false, error = "providerType is required" });

            var model = (req.Model ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(model))
                return Results.BadRequest(new { ok = false, error = "model is required" });

            // ProviderType/Model are always explicit for instances.
            var providerTypePath = $"LLMProviders:Providers:{name}:ProviderType";
            var modelPath = $"LLMProviders:Providers:{name}:Model";
            secrets.Set(providerTypePath, providerType);
            secrets.Set(modelPath, model);

            // Endpoint is optional: if empty -> remove override (fall back to profile default).
            var endpointPath = $"LLMProviders:Providers:{name}:Endpoint";
            var endpoint = (req.Endpoint ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                secrets.Remove(endpointPath);
            }
            else
            {
                secrets.Set(endpointPath, endpoint);
            }

            // ApiKey: allow direct set or server-side copy (never echo).
            var apiKeyPath = $"LLMProviders:Providers:{name}:ApiKey";
            var apiKey = (req.ApiKey ?? string.Empty).Trim();
            var copyFrom = (req.CopyApiKeyFrom ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                secrets.Set(apiKeyPath, apiKey);
            }
            else if (!string.IsNullOrWhiteSpace(copyFrom))
            {
                var fromPath = $"LLMProviders:Providers:{copyFrom}:ApiKey";
                if (!secrets.TryGet(fromPath, out var fromKey) || string.IsNullOrWhiteSpace(fromKey))
                    return Results.BadRequest(new { ok = false, error = "copyApiKeyFrom has no configured apiKey" });
                secrets.Set(apiKeyPath, fromKey.Trim());
            }

            EnsureDefaultProviderKeyBestEffort(secrets, name);

            var resolved = LlmProviderResolver.Resolve(secrets, name);
            return Results.Json(new
            {
                ok = true,
                providerName = name,
                providerType,
                keyPaths = new[] { providerTypePath, modelPath, endpointPath, apiKeyPath },
                provider = resolved.Public
            });
        });

        app.MapDelete("/api/llm/api-key/{providerName}", (
            string providerName,
            IAevatarUserSecretsStore secrets,
            HttpContext http) =>
        {
            if (!IsLocal(http))
                return Results.Json(new { ok = false, error = "Forbidden: local access only" }, statusCode: 403);

            var name = (providerName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                return Results.BadRequest(new { ok = false, error = "providerName is required" });

            var keyPath = $"LLMProviders:Providers:{name}:ApiKey";
            var removed = secrets.Remove(keyPath);

            return Results.Json(new { ok = true, providerName = name, keyPath, removed });
        });

        // Generic secrets set/remove (Advanced + endpoint override).
        app.MapPost("/api/secrets/set", (
            SetSecretRequest req,
            IAevatarUserSecretsStore secrets,
            HttpContext http) =>
        {
            if (!IsLocal(http))
                return Results.Json(new { ok = false, error = "Forbidden: local access only" }, statusCode: 403);

            var key = (req.Key ?? "").Trim();
            if (string.IsNullOrWhiteSpace(key))
                return Results.BadRequest(new { ok = false, error = "key is required" });

            var value = (req.Value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value))
                return Results.BadRequest(new { ok = false, error = "value is required" });

            secrets.Set(key, value);
            return Results.Json(new { ok = true, key });
        });

        app.MapPost("/api/secrets/remove", (
            RemoveSecretRequest req,
            IAevatarUserSecretsStore secrets,
            HttpContext http) =>
        {
            if (!IsLocal(http))
                return Results.Json(new { ok = false, error = "Forbidden: local access only" }, statusCode: 403);

            var key = (req.Key ?? "").Trim();
            if (string.IsNullOrWhiteSpace(key))
                return Results.BadRequest(new { ok = false, error = "key is required" });

            var removed = secrets.Remove(key);
            return Results.Json(new { ok = true, key, removed });
        });

        // ============================================================
        //  Probe endpoints (no persistence)
        //  - Used by UI when user typed a new key but hasn't saved an instance yet.
        // ============================================================

        app.MapPost("/api/llm/probe/test", async (
            ProbeLlmRequest req,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!IsLocal(http))
                return Results.Json(new { ok = false, error = "Forbidden: local access only" }, statusCode: 403);

            var providerType = (req.ProviderType ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(providerType))
                return Results.BadRequest(new { ok = false, error = "providerType is required" });

            var apiKey = (req.ApiKey ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
                return Results.BadRequest(new { ok = false, error = "apiKey is required" });

            var profile = ProviderProfiles.Get(providerType);
            var endpoint = string.IsNullOrWhiteSpace(req.Endpoint) ? (profile.DefaultEndpoint ?? "") : req.Endpoint.Trim();
            if (string.IsNullOrWhiteSpace(endpoint))
                return Results.BadRequest(new { ok = false, error = "endpoint is required (or set a provider default endpoint)" });

            var provider = new ResolvedProvider(
                ProviderName: $"probe:{providerType}",
                ProviderType: providerType,
                ProviderTypeSource: "probe",
                DisplayName: profile.DisplayName,
                Kind: profile.Kind,
                Endpoint: endpoint,
                EndpointSource: "probe",
                Model: string.Empty,
                ModelSource: "probe",
                ApiKeyConfigured: true,
                ApiKey: apiKey,
                Public: new ResolvedProviderPublic(
                    ProviderName: $"probe:{providerType}",
                    ProviderType: providerType,
                    ProviderTypeSource: "probe",
                    DisplayName: profile.DisplayName,
                    Kind: profile.Kind.ToString(),
                    ApiKeyConfigured: true,
                    Endpoint: endpoint,
                    EndpointSource: "probe",
                    Model: string.Empty,
                    ModelSource: "probe"));

            return Results.Json(await LlmSecretsProbe.TestAsync(provider, ct));
        });

        app.MapPost("/api/llm/probe/models", async (
            ProbeLlmRequest req,
            int? limit,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!IsLocal(http))
                return Results.Json(new { ok = false, error = "Forbidden: local access only" }, statusCode: 403);

            var providerType = (req.ProviderType ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(providerType))
                return Results.BadRequest(new { ok = false, error = "providerType is required" });

            var apiKey = (req.ApiKey ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
                return Results.BadRequest(new { ok = false, error = "apiKey is required" });

            var profile = ProviderProfiles.Get(providerType);
            var endpoint = string.IsNullOrWhiteSpace(req.Endpoint) ? (profile.DefaultEndpoint ?? "") : req.Endpoint.Trim();
            if (string.IsNullOrWhiteSpace(endpoint))
                return Results.BadRequest(new { ok = false, error = "endpoint is required (or set a provider default endpoint)" });

            var provider = new ResolvedProvider(
                ProviderName: $"probe:{providerType}",
                ProviderType: providerType,
                ProviderTypeSource: "probe",
                DisplayName: profile.DisplayName,
                Kind: profile.Kind,
                Endpoint: endpoint,
                EndpointSource: "probe",
                Model: string.Empty,
                ModelSource: "probe",
                ApiKeyConfigured: true,
                ApiKey: apiKey,
                Public: new ResolvedProviderPublic(
                    ProviderName: $"probe:{providerType}",
                    ProviderType: providerType,
                    ProviderTypeSource: "probe",
                    DisplayName: profile.DisplayName,
                    Kind: profile.Kind.ToString(),
                    ApiKeyConfigured: true,
                    Endpoint: endpoint,
                    EndpointSource: "probe",
                    Model: string.Empty,
                    ModelSource: "probe"));

            return Results.Json(await LlmSecretsProbe.FetchModelsAsync(provider, limit ?? 200, ct));
        });

        // ============================================================
        //  Trash bin (two-step delete for API keys)
        //
        //  Step 1: move API key to trash (Delete in UI)
        //  Step 2: delete permanently from trash list (home page)
        // ============================================================

        const string trashApiKeyPrefix = "Aevatar:Trash:ApiKeys:";

        app.MapPost("/api/trash/api-key/{providerName}", (
            string providerName,
            IAevatarUserSecretsStore secrets,
            HttpContext http) =>
        {
            if (!IsLocal(http))
                return Results.Json(new { ok = false, error = "Forbidden: local access only" }, statusCode: 403);

            var name = (providerName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                return Results.BadRequest(new { ok = false, error = "providerName is required" });

            var providerPrefix = $"LLMProviders:Providers:{name}:";
            var keyPath = $"{providerPrefix}ApiKey";

            // Snapshot all provider keys (so restore can bring them back).
            var all = secrets.GetAll();
            var providerKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in all)
            {
                if (!kv.Key.StartsWith(providerPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                providerKeys[kv.Key] = kv.Value ?? string.Empty;
            }

            if (providerKeys.Count == 0)
            {
                return Results.BadRequest(new { ok = false, error = "provider has no secrets/config to delete" });
            }

            var resolved = LlmProviderResolver.Resolve(secrets, name);
            var trashedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            providerKeys.TryGetValue(keyPath, out var apiKey);

            var entry = new TrashedApiKeyEntry(
                ProviderName: name,
                ProviderType: resolved.ProviderType,
                Model: resolved.Model,
                Endpoint: resolved.Endpoint,
                OriginalKeyPath: keyPath,
                TrashedAtUnixMs: trashedAt,
                ApiKey: (apiKey ?? string.Empty).Trim(),
                ProviderKeys: providerKeys);

            var trashKey = $"{trashApiKeyPrefix}{name}";
            secrets.Set(trashKey, JsonSerializer.Serialize(entry));

            // Delete the entire provider subtree so it truly disappears from all config sources.
            foreach (var k in providerKeys.Keys)
            {
                secrets.Remove(k);
            }
            EnsureDefaultProviderKeyBestEffort(secrets, preferredProvider: null);

            return Results.Json(new
            {
                ok = true,
                providerName = name,
                trashedAtUnixMs = trashedAt
            });
        });

        app.MapGet("/api/trash/api-keys", (IAevatarUserSecretsStore secrets, HttpContext http) =>
        {
            if (!IsLocal(http))
                return Results.Json(new { ok = false, error = "Forbidden: local access only" }, statusCode: 403);

            var all = secrets.GetAll();
            var list = new List<TrashedApiKeyListItem>(capacity: 16);

            foreach (var kv in all)
            {
                var k = kv.Key ?? string.Empty;
                if (!k.StartsWith(trashApiKeyPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var raw = kv.Value ?? string.Empty;
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                TrashedApiKeyEntry? entry = null;
                try
                {
                    entry = JsonSerializer.Deserialize<TrashedApiKeyEntry>(raw);
                }
                catch
                {
                    // ignore malformed entry
                }
                if (entry == null || string.IsNullOrWhiteSpace(entry.ProviderName))
                    continue;

                // If older entries didn't populate ApiKey, fall back to ProviderKeys.
                var rawKey = entry.ApiKey;
                if (string.IsNullOrWhiteSpace(rawKey) && entry.ProviderKeys != null)
                {
                    entry.ProviderKeys.TryGetValue(entry.OriginalKeyPath ?? string.Empty, out rawKey);
                }

                list.Add(new TrashedApiKeyListItem(
                    ProviderName: entry.ProviderName,
                    ProviderType: entry.ProviderType,
                    Model: entry.Model,
                    Endpoint: entry.Endpoint,
                    TrashedAtUnixMs: entry.TrashedAtUnixMs,
                    Masked: SecretMask.MaskMiddle(rawKey ?? string.Empty)));
            }

            return Results.Json(new
            {
                ok = true,
                items = list
                    .OrderByDescending(x => x.TrashedAtUnixMs)
                    .ToList()
            });
        });

        // Permanently delete a trashed API key (also ensures live key is removed).
        app.MapDelete("/api/trash/api-key/{providerName}", (
            string providerName,
            IAevatarUserSecretsStore secrets,
            HttpContext http) =>
        {
            if (!IsLocal(http))
                return Results.Json(new { ok = false, error = "Forbidden: local access only" }, statusCode: 403);

            var name = (providerName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                return Results.BadRequest(new { ok = false, error = "providerName is required" });

            var providerPrefix = $"LLMProviders:Providers:{name}:";
            var all = secrets.GetAll();
            foreach (var k in all.Keys)
            {
                if (k.StartsWith(providerPrefix, StringComparison.OrdinalIgnoreCase))
                    secrets.Remove(k);
            }

            var trashKey = $"{trashApiKeyPrefix}{name}";
            var removed = secrets.Remove(trashKey);
            EnsureDefaultProviderKeyBestEffort(secrets, preferredProvider: null);

            return Results.Json(new { ok = true, providerName = name, removed });
        });

        app.MapPost("/api/trash/api-key/{providerName}/restore", (
            string providerName,
            IAevatarUserSecretsStore secrets,
            HttpContext http) =>
        {
            if (!IsLocal(http))
                return Results.Json(new { ok = false, error = "Forbidden: local access only" }, statusCode: 403);

            var name = (providerName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                return Results.BadRequest(new { ok = false, error = "providerName is required" });

            var trashKey = $"{trashApiKeyPrefix}{name}";
            if (!secrets.TryGet(trashKey, out var raw) || string.IsNullOrWhiteSpace(raw))
                return Results.BadRequest(new { ok = false, error = "trash entry not found" });

            TrashedApiKeyEntry? entry = null;
            try
            {
                entry = JsonSerializer.Deserialize<TrashedApiKeyEntry>(raw);
            }
            catch
            {
                // ignore
            }
            if (entry == null || string.IsNullOrWhiteSpace(entry.ApiKey))
                return Results.BadRequest(new { ok = false, error = "trash entry is malformed" });

            var providerPrefix = $"LLMProviders:Providers:{name}:";
            var all = secrets.GetAll();
            if (all.Keys.Any(k => k.StartsWith(providerPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                return Results.BadRequest(new { ok = false, error = "provider already exists; delete it before restore" });
            }

            if (entry.ProviderKeys != null && entry.ProviderKeys.Count > 0)
            {
                foreach (var kv in entry.ProviderKeys)
                {
                    secrets.Set(kv.Key, kv.Value ?? string.Empty);
                }
            }
            else
            {
                // Back-compat: older trash entries only stored ApiKey.
                var keyPath = $"{providerPrefix}ApiKey";
                secrets.Set(keyPath, entry.ApiKey.Trim());
            }

            secrets.Remove(trashKey);
            EnsureDefaultProviderKeyBestEffort(secrets, preferredProvider: name);

            return Results.Json(new { ok = true, providerName = name, restored = true });
        });
    }

    private static bool IsLocal(HttpContext ctx)
    {
        // Allow disabling local check for trusted Docker environments
        var cfg = ctx.RequestServices.GetService<IConfiguration>();
        var allowRemote = cfg?["Security:AllowRemoteApi"]
                          ?? Environment.GetEnvironmentVariable("ALLOW_REMOTE_LLM_API");
        if (string.Equals(allowRemote, "true", StringComparison.OrdinalIgnoreCase))
            return true;

        var ip = ctx.Connection.RemoteIpAddress;
        return ip == null || System.Net.IPAddress.IsLoopback(ip);
    }

    private static bool IsProviderRunnable(IAevatarUserSecretsStore secrets, string providerName)
    {
        var name = (providerName ?? string.Empty).Trim();
        if (name.Length == 0)
            return false;

        var keyPath = $"LLMProviders:Providers:{name}:ApiKey";
        return secrets.TryGet(keyPath, out var v) && !string.IsNullOrWhiteSpace(v);
    }

    private static string ResolveEffectiveDefaultProviderName(IAevatarUserSecretsStore secrets)
    {
        if (secrets.TryGet(LlmDefaultProviderKey, out var raw) && !string.IsNullOrWhiteSpace(raw))
        {
            var v = raw.Trim();
            // Only treat literal "default" as a placeholder when there is no runnable "default" instance.
            if (!string.Equals(v, "default", StringComparison.OrdinalIgnoreCase) || IsProviderRunnable(secrets, "default"))
                return v;
        }

        // Fallback: first runnable provider instance.
        const string prefix = "LLMProviders:Providers:";
        const string suffix = ":ApiKey";
        var all = secrets.GetAll();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in all)
        {
            var k = kv.Key ?? string.Empty;
            if (!k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!k.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(kv.Value))
                continue;

            var mid = k.Substring(prefix.Length, k.Length - prefix.Length - suffix.Length).Trim();
            if (mid.Length == 0)
                continue;
            names.Add(mid);
        }

        return names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).FirstOrDefault() ?? "default";
    }

    private static void EnsureDefaultProviderKeyBestEffort(IAevatarUserSecretsStore secrets, string? preferredProvider)
    {
        var current = secrets.TryGet(LlmDefaultProviderKey, out var raw) ? (raw ?? string.Empty).Trim() : string.Empty;
        var currentIsPlaceholder =
            string.IsNullOrWhiteSpace(current) ||
            (string.Equals(current, "default", StringComparison.OrdinalIgnoreCase) && !IsProviderRunnable(secrets, "default"));

        if (!currentIsPlaceholder && IsProviderRunnable(secrets, current))
            return;

        var preferred = (preferredProvider ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(preferred) && IsProviderRunnable(secrets, preferred))
        {
            secrets.Set(LlmDefaultProviderKey, preferred);
            return;
        }

        var next = ResolveEffectiveDefaultProviderName(secrets);
        if (!string.IsNullOrWhiteSpace(next) && IsProviderRunnable(secrets, next))
        {
            secrets.Set(LlmDefaultProviderKey, next);
            return;
        }

        // Nothing runnable: remove stale value if any.
        if (!string.IsNullOrWhiteSpace(current))
            secrets.Remove(LlmDefaultProviderKey);
    }
}


