using System.Text.Json.Nodes;
using Aevatar.Agents.Core.Secrets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace VibeResearching.Api.Infrastructure;

// ============================================================
//  SkillsMpApi (local-only endpoints)
//
//  中文说明：
//  - 这些接口会“代用户”携带 SkillsMP API Key 去访问 skillsmp.com
//  - 为防止远程滥用，统一 loopback-only
// ============================================================
public static class SkillsMpApi
{
    private const string ApiKeyPath = SkillsMpClient.ApiKeyConfigKey; // "SkillsMP:ApiKey"
    private const string BaseUrlPath = SkillsMpClient.BaseUrlConfigKey; // "SkillsMP:BaseUrl"

    public static void MapSkillsMpApi(this WebApplication app)
    {
        app.MapGet("/api/skillsmp/status", (
            IAevatarUserSecretsStore secrets,
            IConfiguration cfg,
            HttpContext http) =>
        {
            if (!IsLocal(http))
                return Results.Forbid();

            var raw =
                (secrets.TryGet(ApiKeyPath, out var s) ? s : null) ??
                cfg[ApiKeyPath] ??
                (Environment.GetEnvironmentVariable(SkillsMpClient.ApiKeyEnv) ?? string.Empty);

            raw = (raw ?? string.Empty).Trim();
            var configured = !string.IsNullOrWhiteSpace(raw);
            var masked = configured ? MaskMiddle(raw) : string.Empty;
            var baseUrl = (cfg[BaseUrlPath] ?? string.Empty).Trim();

            return Results.Json(new
            {
                ok = true,
                configured,
                masked,
                keyPath = ApiKeyPath,
                baseUrl
            });
        });

        // Mask/reveal API key (localhost-only)
        app.MapGet("/api/skillsmp/api-key", (
            bool? reveal,
            IAevatarUserSecretsStore secrets,
            IConfiguration cfg,
            HttpContext http) =>
        {
            if (!IsLocal(http))
                return Results.Forbid();

            var raw =
                (secrets.TryGet(ApiKeyPath, out var s) ? s : null) ??
                cfg[ApiKeyPath] ??
                (Environment.GetEnvironmentVariable(SkillsMpClient.ApiKeyEnv) ?? string.Empty);

            raw = (raw ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Results.Json(new { ok = true, configured = false, masked = "" });
            }

            var masked = MaskMiddle(raw);
            if (reveal == true)
            {
                return Results.Json(new { ok = true, configured = true, masked, value = raw });
            }

            return Results.Json(new { ok = true, configured = true, masked });
        });

        // Upsert settings into user secrets (preferred)
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

            SetOrRemove(ApiKeyPath, req.ApiKey);
            SetOrRemove(BaseUrlPath, req.BaseUrl);

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
                [ApiKeyPath] = secrets.Remove(ApiKeyPath),
                [BaseUrlPath] = secrets.Remove(BaseUrlPath)
            };

            return Results.Json(new { ok = true, removed });
        });

        app.MapGet("/api/skillsmp/search", async (
            string? q,
            int? page,
            int? limit,
            string? sortBy,
            SkillsMpClient client,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!IsLocal(http))
                return Results.Forbid();

            var query = (q ?? string.Empty).Trim();
            if (query.Length == 0)
                return Results.BadRequest(new { ok = false, error = "q is required" });

            var p = page ?? 1;
            var l = limit ?? 20;

            var r = await client.SearchAsync(query, p, l, sortBy, ct);
            if (!r.Ok)
            {
                var code = r.StatusCode is >= 100 and <= 599 ? r.StatusCode.Value : 400;
                return Results.Json(new { ok = false, error = r.Error ?? "search failed", statusCode = r.StatusCode ?? 0 }, statusCode: code);
            }

            return Results.Json(new
            {
                ok = true,
                query = new { q = query, page = p, limit = l, sortBy = (sortBy ?? "").Trim() },
                items = r.Items,
                raw = r.Raw
            });
        });

        app.MapGet("/api/skillsmp/ai-search", async (
            string? q,
            SkillsMpClient client,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!IsLocal(http))
                return Results.Forbid();

            var query = (q ?? string.Empty).Trim();
            if (query.Length == 0)
                return Results.BadRequest(new { ok = false, error = "q is required" });

            var r = await client.AiSearchAsync(query, ct);
            if (!r.Ok)
            {
                var code = r.StatusCode is >= 100 and <= 599 ? r.StatusCode.Value : 400;
                return Results.Json(new { ok = false, error = r.Error ?? "ai-search failed", statusCode = r.StatusCode ?? 0 }, statusCode: code);
            }

            return Results.Json(new
            {
                ok = true,
                query = new { q = query },
                items = r.Items,
                raw = r.Raw
            });
        });

        app.MapPost("/api/skillsmp/install", async (
            InstallSkillPackRequest req,
            SkillPacksConfigFileStore store,
            SkillPacksSyncService sync,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!IsLocal(http))
                return Results.Forbid();

            var repoUrl = (req.RepoUrl ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(repoUrl))
                return Results.BadRequest(new { ok = false, error = "repoUrl is required" });

            var name = (req.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                name = DerivePackName(repoUrl);

            var @ref = string.IsNullOrWhiteSpace(req.Ref) ? "main" : req.Ref!.Trim();
            var skillsSubDir = string.IsNullOrWhiteSpace(req.SkillsSubDir) ? "skills" : req.SkillsSubDir!.Trim();

            var pack = new SkillPackSpec
            {
                Name = name,
                Enabled = true,
                AutoUpdateOnStartup = true,
                RepoUrl = repoUrl,
                Ref = @ref,
                SkillsSubDir = skillsSubDir,
                InstallDir = string.IsNullOrWhiteSpace(req.InstallDir) ? null : req.InstallDir!.Trim(),
                UpdateTimeoutMs = req.UpdateTimeoutMs ?? 60_000,
                ShallowClone = req.ShallowClone ?? true,
                ShallowDepth = req.ShallowDepth ?? 1,
                SetAgentSkillsEnv = true
            };

            var saved = await store.UpsertPackAsync(pack, ct);

            // Best-effort: config reload is async; give it a tiny window before syncing.
            var doSync = req.Sync is not false;
            object? syncResult = null;
            if (doSync)
            {
                try { await Task.Delay(200, ct); } catch { /* ignore */ }
                syncResult = await sync.TryEnsureSyncedAsync(SkillPackSyncMode.Manual, ct);
            }

            return Results.Json(new
            {
                ok = true,
                pack = new
                {
                    name = saved.Name,
                    repoUrl = saved.RepoUrl,
                    @ref = saved.Ref,
                    skillsSubDir = saved.SkillsSubDir,
                    installDir = saved.InstallDir ?? ""
                },
                synced = doSync,
                sync = syncResult
            });
        });
    }

    private static bool IsLocal(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress;
        return ip == null || System.Net.IPAddress.IsLoopback(ip);
    }

    private static string DerivePackName(string repoUrl)
    {
        try
        {
            var u = new Uri(repoUrl);
            var seg = u.Segments.LastOrDefault() ?? "skill-pack";
            var name = seg.Trim('/').Trim();
            if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);
            return string.IsNullOrWhiteSpace(name) ? "skill-pack" : name;
        }
        catch
        {
            var s = repoUrl.Trim().TrimEnd('/');
            var last = s.Split('/').LastOrDefault() ?? "skill-pack";
            if (last.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                last = last.Substring(0, last.Length - 4);
            return string.IsNullOrWhiteSpace(last) ? "skill-pack" : last;
        }
    }

    private static string MaskMiddle(string raw, int prefix = 4, int suffix = 4)
    {
        var s = (raw ?? string.Empty).Trim();
        if (s.Length == 0) return string.Empty;

        prefix = Math.Clamp(prefix, 0, 16);
        suffix = Math.Clamp(suffix, 0, 16);
        if (s.Length <= prefix + suffix || s.Length < 8)
            return new string('*', s.Length);

        var mid = s.Length - prefix - suffix;
        if (mid <= 0)
            return new string('*', s.Length);

        return s.Substring(0, prefix) + new string('*', mid) + s.Substring(s.Length - suffix, suffix);
    }

    private sealed record InstallSkillPackRequest(
        string? Name,
        string? RepoUrl,
        string? Ref,
        string? SkillsSubDir,
        string? InstallDir,
        int? UpdateTimeoutMs,
        bool? ShallowClone,
        int? ShallowDepth,
        bool? Sync);

    private sealed record UpsertSkillsMpRequest(
        string? ApiKey,
        string? BaseUrl);
}


