using Aevatar.Agents.Core.Secrets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ScientificResearchAssistant.Api.Infrastructure;

// ============================================================
//  LlmSecretsApi (Endpoints)
//
//  中文说明：
//  - SRA 内置一份 “Secrets API” 来替代单独跑 Aevatar.Secrets.Api
//  - 所有写入接口严格 loopback-only（防止远程误用）
// ============================================================

public static partial class LlmSecretsApi
{
    public static void MapLlmSecretsApi(this WebApplication app)
    {
        // Providers catalog (base types)
        app.MapGet("/api/llm/providers", (
            IAevatarUserSecretsStore secrets,
            HttpContext http) =>
        {
            if (!IsLocal(http))
                return Results.Forbid();

            var providers = ProviderCatalog.BuildProviderTypes(secrets);
            return Results.Json(new { ok = true, providers });
        });

        // Configured provider instances (multi-model)
        app.MapGet("/api/llm/instances", (
            IAevatarUserSecretsStore secrets,
            HttpContext http) =>
        {
            if (!IsLocal(http))
                return Results.Forbid();

            var instances = ProviderCatalog.BuildInstances(secrets);
            return Results.Json(new { ok = true, instances });
        });

        // Provider details (never returns secret values)
        app.MapGet("/api/llm/provider/{providerName}", (
            string providerName,
            IAevatarUserSecretsStore secrets,
            HttpContext http) =>
        {
            if (!IsLocal(http))
                return Results.Forbid();

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
                return Results.Forbid();

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
                return Results.Forbid();

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
                return Results.Forbid();

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
        app.MapPost("/api/llm/api-key", (
            SetLlmApiKeyRequest req,
            IAevatarUserSecretsStore secrets,
            HttpContext http) =>
        {
            if (!IsLocal(http))
                return Results.Forbid();

            var providerName = (req.ProviderName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(providerName))
                return Results.BadRequest(new { ok = false, error = "providerName is required" });

            var apiKey = (req.ApiKey ?? "").Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
                return Results.BadRequest(new { ok = false, error = "apiKey is required" });

            var keyPath = $"LLMProviders:Providers:{providerName}:ApiKey";
            secrets.Set(keyPath, apiKey);

            return Results.Json(new { ok = true, providerName, keyPath });
        });

        // Upsert instance (multi-model friendly).
        app.MapPost("/api/llm/instance", (
            UpsertLlmInstanceRequest req,
            IAevatarUserSecretsStore secrets,
            HttpContext http) =>
        {
            if (!IsLocal(http))
                return Results.Forbid();

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
                return Results.Forbid();

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
                return Results.Forbid();

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
                return Results.Forbid();

            var key = (req.Key ?? "").Trim();
            if (string.IsNullOrWhiteSpace(key))
                return Results.BadRequest(new { ok = false, error = "key is required" });

            var removed = secrets.Remove(key);
            return Results.Json(new { ok = true, key, removed });
        });
    }

    private static bool IsLocal(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress;
        return ip == null || System.Net.IPAddress.IsLoopback(ip);
    }
}


