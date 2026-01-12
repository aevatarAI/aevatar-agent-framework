using System.Text.Json;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Core.Secrets;
using Microsoft.AspNetCore.Http.Json;

// ============================================================
//  Aevatar.Secrets.Api
//
//  A tiny local web app to configure encrypted user secrets.
//
//  Default URLs:
//   - http://localhost:6667 (requested, but Chrome blocks as ERR_UNSAFE_PORT)
//   - http://localhost:6677 (browser-friendly fallback)
//   - repo policy: never use :5000 as default/example
//
//  User secrets path:
//   - Default: ~/.aevatar/secrets.json
//   - Override: AEVATAR_SECRETS_PATH / AEVATAR_SECRETS_DIR
// ============================================================

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

// Encrypted user secrets store (write).
builder.Services.AddAevatarUserSecretsStore();

// Ports
// If no URLs are configured (ASPNETCORE_URLS / --urls), listen on:
// - 6667: requested default (note: Chrome blocks some "unsafe" ports, including 6667)
// - 6677: safe fallback for browsers
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
{
    builder.WebHost.UseUrls("http://localhost:6667", "http://localhost:6677");
}

var app = builder.Build();

// UI: serve from wwwroot/ instead of embedding huge HTML/JS in Program.cs
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Text("ok"));

// ------------------------------------------------------------
// Providers (base types) + instances (configured) for UI
// ------------------------------------------------------------
app.MapGet("/api/llm/providers", (IAevatarUserSecretsStore secrets, HttpContext http) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

    var providers = ProviderCatalog.BuildProviderTypes(secrets);
    return Results.Json(new { ok = true, providers });
});

app.MapGet("/api/llm/instances", (IAevatarUserSecretsStore secrets, HttpContext http) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

    var instances = ProviderCatalog.BuildInstances(secrets);
    return Results.Json(new { ok = true, instances });
});

// ------------------------------------------------------------
// SkillsMP (Agent Skills marketplace)
// Stored at: SkillsMP:ApiKey  (+ optional SkillsMP:BaseUrl)
// ------------------------------------------------------------
app.MapGet("/api/skillsmp/status", (IAevatarUserSecretsStore secrets, HttpContext http) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

    var keyPath = "SkillsMP:ApiKey";
    var baseUrlKey = "SkillsMP:BaseUrl";

    var configured = secrets.TryGet(keyPath, out var raw) && !string.IsNullOrWhiteSpace(raw);
    var masked = configured ? SecretMask.MaskMiddle((raw ?? string.Empty).Trim()) : string.Empty;
    var baseUrl = secrets.TryGet(baseUrlKey, out var bu) ? (bu ?? string.Empty).Trim() : string.Empty;

    return Results.Json(new
    {
        ok = true,
        configured,
        masked,
        keyPath,
        baseUrl
    });
});

app.MapGet("/api/skillsmp/api-key", (
    bool? reveal,
    IAevatarUserSecretsStore secrets,
    HttpContext http) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

    const string keyPath = "SkillsMP:ApiKey";

    if (!secrets.TryGet(keyPath, out var value) || string.IsNullOrWhiteSpace(value))
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

app.MapPost("/api/skillsmp", (
    UpsertSkillsMpRequest req,
    IAevatarUserSecretsStore secrets,
    HttpContext http) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

    void SetOrRemove(string key, string? raw)
    {
        if (raw == null) return; // not provided
        var v = raw.Trim();
        if (string.IsNullOrWhiteSpace(v)) secrets.Remove(key);
        else secrets.Set(key, v);
    }

    // ApiKey: only write when provided (UI should prevent saving masked values).
    SetOrRemove("SkillsMP:ApiKey", req.ApiKey);
    // BaseUrl: optional (non-secret)
    SetOrRemove("SkillsMP:BaseUrl", req.BaseUrl);

    return Results.Json(new { ok = true });
});

app.MapDelete("/api/skillsmp", (
    IAevatarUserSecretsStore secrets,
    HttpContext http) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

    var removed = new Dictionary<string, bool>
    {
        ["SkillsMP:ApiKey"] = secrets.Remove("SkillsMP:ApiKey"),
        ["SkillsMP:BaseUrl"] = secrets.Remove("SkillsMP:BaseUrl")
    };

    return Results.Json(new { ok = true, removed });
});

// ------------------------------------------------------------
// Embeddings (global fallback) - Aliyun/DashScope (OpenAI-compatible)
// Stored at: LLMProviders:Embeddings:*
// ------------------------------------------------------------
app.MapGet("/api/embeddings", (IAevatarUserSecretsStore secrets, HttpContext http) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

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
        return Results.Forbid();

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
        return Results.Forbid();

    // Enabled: optional UI toggle (not required by core binding, but useful for humans).
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

    // ApiKey: only write when provided (UI should prevent saving masked values).
    SetOrRemove("LLMProviders:Embeddings:ApiKey", req.ApiKey);

    return Results.Json(new { ok = true });
});

app.MapDelete("/api/embeddings", (
    IAevatarUserSecretsStore secrets,
    HttpContext http) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

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
app.MapGet("/api/llm/default", (IAevatarUserSecretsStore secrets, HttpContext http) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

    EnsureDefaultProviderKeyBestEffort(secrets, preferredProvider: null);
    var effective = ResolveEffectiveDefaultProviderName(secrets);

    return Results.Json(new
    {
        ok = true,
        providerName = effective
    });
});

app.MapPost("/api/llm/default", (
    SetLlmDefaultRequest req,
    IAevatarUserSecretsStore secrets,
    HttpContext http) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

    var name = (req.ProviderName ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(name))
        return Results.BadRequest(new { ok = false, error = "providerName is required" });

    if (!IsProviderRunnable(secrets, name))
        return Results.BadRequest(new { ok = false, error = "providerName has no configured apiKey" });

    secrets.Set("LLMProviders:Default", name);
    return Results.Json(new { ok = true, providerName = name });
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

// Test connection (best-effort): verifies API key + endpoint by calling list-models endpoint.
app.MapGet("/api/llm/test/{providerName}", async (
    string providerName,
    IAevatarUserSecretsStore secrets,
    HttpContext http,
    CancellationToken ct) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

    var resolved = LlmProviderResolver.Resolve(secrets, providerName);
    var result = await LlmProbe.TestAsync(resolved, ct);
    return Results.Json(result);
});

// Fetch models (best-effort): returns model ids/names (never returns secret values).
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
    var result = await LlmProbe.FetchModelsAsync(resolved, limit ?? 200, ct);
    return Results.Json(result);
});

// Probe endpoints (no persistence):
// - Used by UI when user typed a new key but hasn't saved an instance yet.
app.MapPost("/api/llm/probe/test", async (
    ProbeLlmRequest req,
    HttpContext http,
    CancellationToken ct) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

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

    return Results.Json(await LlmProbe.TestAsync(provider, ct));
});

app.MapPost("/api/llm/probe/models", async (
    ProbeLlmRequest req,
    int? limit,
    HttpContext http,
    CancellationToken ct) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

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

    return Results.Json(await LlmProbe.FetchModelsAsync(provider, limit ?? 200, ct));
});

// API key status/mask/reveal (localhost-only)
// - Default: returns masked (middle hidden)
// - reveal=true: returns full value (for local UI only)
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

    var masked = SecretMask.MaskMiddle(value.Trim());

    if (reveal == true)
    {
        return Results.Json(new
        {
            ok = true,
            providerName = name,
            configured = true,
            masked,
            value = value.Trim()
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

app.MapPost("/api/llm/api-key", (
    SetLlmApiKeyRequest req,
    IAevatarUserSecretsStore secrets,
    HttpContext http) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

    var providerName = (req.ProviderName ?? "").Trim();
    if (string.IsNullOrWhiteSpace(providerName))
        return Results.BadRequest(new { error = "providerName is required" });

    var apiKey = (req.ApiKey ?? "").Trim();
    if (string.IsNullOrWhiteSpace(apiKey))
        return Results.BadRequest(new { error = "apiKey is required" });

    var keyPath = $"LLMProviders:Providers:{providerName}:ApiKey";
    secrets.Set(keyPath, apiKey);
    EnsureDefaultProviderKeyBestEffort(secrets, providerName);

    return Results.Json(new { ok = true, providerName, keyPath });
});

// Upsert a provider instance (multi-model friendly).
// - Writes ProviderType/Model/Endpoint and ApiKey (direct or copied) into user secrets.
// - All values are stored under: LLMProviders:Providers:{name}:*
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
        return Results.Forbid();

    var name = (providerName ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(name))
        return Results.BadRequest(new { error = "providerName is required" });

    var keyPath = $"LLMProviders:Providers:{name}:ApiKey";
    var removed = secrets.Remove(keyPath);

    return Results.Json(new { ok = true, providerName = name, keyPath, removed });
});

// ============================================================
//  Trash bin (two-step delete for API keys)
//
//  Step 1: move API key to trash (disconnect)
//  Step 2: delete permanently from trash list (home page)
// ============================================================

const string TrashApiKeyPrefix = "Aevatar:Trash:ApiKeys:";

app.MapPost("/api/trash/api-key/{providerName}", (
    string providerName,
    IAevatarUserSecretsStore secrets,
    HttpContext http) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

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

    // Capture human-readable info before removal.
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

    var trashKey = $"{TrashApiKeyPrefix}{name}";
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
        return Results.Forbid();

    var all = secrets.GetAll();
    var list = new List<TrashedApiKeyListItem>(capacity: 16);

    foreach (var kv in all)
    {
        var k = kv.Key ?? string.Empty;
        if (!k.StartsWith(TrashApiKeyPrefix, StringComparison.OrdinalIgnoreCase))
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

app.MapDelete("/api/trash/api-key/{providerName}", (
    string providerName,
    IAevatarUserSecretsStore secrets,
    HttpContext http) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

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

    var trashKey = $"{TrashApiKeyPrefix}{name}";
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
        return Results.Forbid();

    var name = (providerName ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(name))
        return Results.BadRequest(new { ok = false, error = "providerName is required" });

    var trashKey = $"{TrashApiKeyPrefix}{name}";
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

app.MapPost("/api/secrets/set", (
    SetSecretRequest req,
    IAevatarUserSecretsStore secrets,
    HttpContext http) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

    var key = (req.Key ?? "").Trim();
    if (string.IsNullOrWhiteSpace(key))
        return Results.BadRequest(new { error = "key is required" });

    var value = (req.Value ?? "").Trim();
    if (string.IsNullOrWhiteSpace(value))
        return Results.BadRequest(new { error = "value is required" });

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
        return Results.BadRequest(new { error = "key is required" });

    var removed = secrets.Remove(key);
    return Results.Json(new { ok = true, key, removed });
});

// Fallback for non-file routes (optional, keeps UX consistent if linked with extra path).
app.MapFallbackToFile("index.html");

app.Run();

static bool IsLocal(HttpContext ctx)
{
    var ip = ctx.Connection.RemoteIpAddress;
    return ip == null || System.Net.IPAddress.IsLoopback(ip);
}

static bool IsProviderRunnable(IAevatarUserSecretsStore secrets, string providerName)
{
    var name = (providerName ?? string.Empty).Trim();
    if (name.Length == 0)
        return false;

    var keyPath = $"LLMProviders:Providers:{name}:ApiKey";
    return secrets.TryGet(keyPath, out var v) && !string.IsNullOrWhiteSpace(v);
}

static string ResolveEffectiveDefaultProviderName(IAevatarUserSecretsStore secrets)
{
    if (secrets.TryGet("LLMProviders:Default", out var raw) && !string.IsNullOrWhiteSpace(raw))
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

static void EnsureDefaultProviderKeyBestEffort(IAevatarUserSecretsStore secrets, string? preferredProvider)
{
    // If explicit default exists AND still runnable, keep it.
    var current = secrets.TryGet("LLMProviders:Default", out var raw) ? (raw ?? string.Empty).Trim() : string.Empty;
    var currentIsPlaceholder =
        string.IsNullOrWhiteSpace(current) ||
        (string.Equals(current, "default", StringComparison.OrdinalIgnoreCase) && !IsProviderRunnable(secrets, "default"));

    if (!currentIsPlaceholder && IsProviderRunnable(secrets, current))
        return;

    var preferred = (preferredProvider ?? string.Empty).Trim();
    if (!string.IsNullOrWhiteSpace(preferred) && IsProviderRunnable(secrets, preferred))
    {
        secrets.Set("LLMProviders:Default", preferred);
        return;
    }

    var next = ResolveEffectiveDefaultProviderName(secrets);
    if (!string.IsNullOrWhiteSpace(next) && IsProviderRunnable(secrets, next))
    {
        secrets.Set("LLMProviders:Default", next);
        return;
    }

    // Nothing runnable: remove stale value if any.
    if (!string.IsNullOrWhiteSpace(current))
        secrets.Remove("LLMProviders:Default");
}