using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ScientificResearchAssistant.Api.Infrastructure;

public enum SkillPackSyncMode
{
    Startup,
    Manual
}

public sealed class SkillPacksSyncResult
{
    public bool Ok { get; init; }
    public List<SkillPackSyncEntry> Packs { get; init; } = new();
    public string? Error { get; init; }
}

public sealed class SkillPackSyncEntry
{
    public required string Name { get; init; }
    public required string RepoUrl { get; init; }
    public required string Ref { get; init; }
    public required string RepoDir { get; init; }
    public required string SkillsRoot { get; init; }
    public string? Commit { get; init; }
    public bool Ok { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Best-effort Git sync for multiple "Agent Skills" packs.
/// <para/>
/// Primary use case: keep large upstream packs (e.g. K-Dense Claude Scientific Skills) updated automatically.
/// </summary>
public sealed class SkillPacksSyncService
{
    private readonly IOptions<SkillPacksOptions> _packs;
    private readonly IOptions<ClaudeScientificSkillsSyncOptions> _legacy;
    private readonly IHostEnvironment _env;
    private readonly ILogger<SkillPacksSyncService> _logger;

    private readonly SemaphoreSlim _lock = new(1, 1);

    public SkillPacksSyncService(
        IOptions<SkillPacksOptions> packs,
        IOptions<ClaudeScientificSkillsSyncOptions> legacy,
        IHostEnvironment env,
        ILogger<SkillPacksSyncService> logger)
    {
        _packs = packs;
        _legacy = legacy;
        _env = env;
        _logger = logger;
    }

    public async Task<SkillPacksSyncResult> TryEnsureSyncedAsync(SkillPackSyncMode mode, CancellationToken ct)
    {
        var specs = GetEnabledSpecs(mode);
        if (specs.Count == 0)
        {
            return new SkillPacksSyncResult { Ok = false, Error = "no enabled skill packs configured" };
        }

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(10 * 60_000));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        await _lock.WaitAsync(linked.Token);
        try
        {
            if (!await IsGitAvailableAsync(linked.Token))
            {
                _logger.LogWarning("[SkillPacksSync] git not found; skipping sync.");
                return new SkillPacksSyncResult
                {
                    Ok = false,
                    Error = "git not found"
                };
            }

            var results = new List<SkillPackSyncEntry>();
            var allOk = true;

            foreach (var spec in specs)
            {
                linked.Token.ThrowIfCancellationRequested();
                var r = await TrySyncOneAsync(spec, linked.Token);
                results.Add(r);
                if (!r.Ok) allOk = false;
            }

            return new SkillPacksSyncResult
            {
                Ok = allOk,
                Packs = results,
                Error = allOk ? null : "one or more packs failed"
            };
        }
        finally
        {
            _lock.Release();
        }
    }

    private List<SkillPackSpec> GetEnabledSpecs(SkillPackSyncMode mode)
    {
        var list = new List<SkillPackSpec>();

        // Prefer new config (SkillPacks:Packs) when present.
        var configured = _packs.Value?.Packs ?? new List<SkillPackSpec>();
        if (configured.Count > 0)
        {
            foreach (var p in configured)
            {
                if (p == null) continue;
                if (!p.Enabled) continue;
                if (mode == SkillPackSyncMode.Startup && !p.AutoUpdateOnStartup) continue;
                if (string.IsNullOrWhiteSpace(p.RepoUrl)) continue;
                list.Add(p);
            }

            return list;
        }

        // Fallback: legacy single-pack section.
        var legacy = _legacy.Value;
        if (legacy != null && legacy.Enabled && (mode != SkillPackSyncMode.Startup || legacy.AutoUpdateOnStartup))
        {
            list.Add(new SkillPackSpec
            {
                Name = "claude-scientific-skills",
                Enabled = legacy.Enabled,
                AutoUpdateOnStartup = legacy.AutoUpdateOnStartup,
                RepoUrl = legacy.RepoUrl,
                Ref = legacy.Ref,
                SkillsSubDir = legacy.SkillsSubDir,
                InstallDir = legacy.InstallDir,
                UpdateTimeoutMs = legacy.UpdateTimeoutMs,
                ShallowClone = legacy.ShallowClone,
                ShallowDepth = legacy.ShallowDepth,
                SetAgentSkillsEnv = legacy.SetAgentSkillsEnv
            });
        }

        return list;
    }

    private async Task<SkillPackSyncEntry> TrySyncOneAsync(SkillPackSpec spec, CancellationToken ct)
    {
        var repoUrl = (spec.RepoUrl ?? string.Empty).Trim();
        var @ref = string.IsNullOrWhiteSpace(spec.Ref) ? "main" : spec.Ref.Trim();

        var repoDir = ResolveRepoDir(spec);
        var skillsRoot = Path.GetFullPath(Path.Combine(repoDir, spec.SkillsSubDir ?? "skills"));

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(Math.Clamp(spec.UpdateTimeoutMs, 1000, 10 * 60_000)));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            // Clone / update
            if (!Directory.Exists(repoDir) || !Directory.Exists(Path.Combine(repoDir, ".git")))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(repoDir)!);
                _logger.LogInformation("[SkillPacksSync] Cloning {Name} -> {Dir}", spec.Name, repoDir);

                var args = new List<string> { "clone" };
                if (spec.ShallowClone)
                {
                    args.Add("--depth");
                    args.Add(Math.Clamp(spec.ShallowDepth, 1, 50).ToString());
                }

                args.Add("--branch");
                args.Add(@ref);
                args.Add(repoUrl);
                args.Add(repoDir);

                var rc = await RunGitAsync(args, workingDir: null, linked.Token);
                if (rc != 0)
                {
                    return new SkillPackSyncEntry
                    {
                        Name = spec.Name,
                        RepoUrl = repoUrl,
                        Ref = @ref,
                        RepoDir = repoDir,
                        SkillsRoot = skillsRoot,
                        Ok = false,
                        Error = $"git clone failed (exitCode={rc})"
                    };
                }
            }
            else
            {
                _logger.LogInformation("[SkillPacksSync] Updating {Name} in {Dir}", spec.Name, repoDir);

                var fetchArgs = new List<string> { "-C", repoDir, "fetch", "origin", @ref, "--prune" };
                if (spec.ShallowClone)
                {
                    fetchArgs.Add("--depth");
                    fetchArgs.Add(Math.Clamp(spec.ShallowDepth, 1, 50).ToString());
                }

                var fetchRc = await RunGitAsync(fetchArgs, workingDir: null, linked.Token);
                if (fetchRc != 0)
                {
                    return new SkillPackSyncEntry
                    {
                        Name = spec.Name,
                        RepoUrl = repoUrl,
                        Ref = @ref,
                        RepoDir = repoDir,
                        SkillsRoot = skillsRoot,
                        Ok = false,
                        Error = $"git fetch failed (exitCode={fetchRc})"
                    };
                }

                var resetRc = await RunGitAsync(new List<string> { "-C", repoDir, "reset", "--hard", "FETCH_HEAD" }, null, linked.Token);
                if (resetRc != 0)
                {
                    return new SkillPackSyncEntry
                    {
                        Name = spec.Name,
                        RepoUrl = repoUrl,
                        Ref = @ref,
                        RepoDir = repoDir,
                        SkillsRoot = skillsRoot,
                        Ok = false,
                        Error = $"git reset failed (exitCode={resetRc})"
                    };
                }

                // Clean untracked files (avoid stale scripts)
                _ = await RunGitAsync(new List<string> { "-C", repoDir, "clean", "-fd" }, null, linked.Token);
            }

            if (!Directory.Exists(skillsRoot))
            {
                return new SkillPackSyncEntry
                {
                    Name = spec.Name,
                    RepoUrl = repoUrl,
                    Ref = @ref,
                    RepoDir = repoDir,
                    SkillsRoot = skillsRoot,
                    Ok = false,
                    Error = $"skills root not found (SkillsSubDir={spec.SkillsSubDir})"
                };
            }

            var commit = await TryGetHeadCommitAsync(repoDir, linked.Token);

            if (spec.SetAgentSkillsEnv)
            {
                AppendAgentSkillsDirs(skillsRoot);
            }

            return new SkillPackSyncEntry
            {
                Name = spec.Name,
                RepoUrl = repoUrl,
                Ref = @ref,
                RepoDir = repoDir,
                SkillsRoot = skillsRoot,
                Commit = commit,
                Ok = true
            };
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return new SkillPackSyncEntry
            {
                Name = spec.Name,
                RepoUrl = repoUrl,
                Ref = @ref,
                RepoDir = repoDir,
                SkillsRoot = skillsRoot,
                Ok = false,
                Error = "timeout"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SkillPacksSync] Sync failed for {Name} (best-effort): {Message}", spec.Name, ex.Message);
            return new SkillPackSyncEntry
            {
                Name = spec.Name,
                RepoUrl = repoUrl,
                Ref = @ref,
                RepoDir = repoDir,
                SkillsRoot = skillsRoot,
                Ok = false,
                Error = ex.Message
            };
        }
    }

    private string ResolveRepoDir(SkillPackSpec spec)
    {
        var raw = (spec.InstallDir ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(raw))
        {
            raw = ExpandHome(raw);
            if (!Path.IsPathRooted(raw))
            {
                // Treat relative InstallDir as relative to assistant root (not to current working dir).
                raw = Path.Combine(GetAssistantRoot(), raw);
            }

            return Path.GetFullPath(raw);
        }

        // Default: {assistantRoot}/.skillpacks/{Name}
        var safeName = SanitizeName(spec.Name);
        return Path.Combine(GetAssistantRoot(), ".skillpacks", safeName);
    }

    private string GetAssistantRoot()
    {
        // ContentRoot is ".../scientific-research-assistant/src/ScientificResearchAssistant.Api"
        // assistant root is two levels up.
        try
        {
            var dir = new DirectoryInfo(_env.ContentRootPath);
            var root = dir.Parent?.Parent?.FullName;
            return string.IsNullOrWhiteSpace(root) ? _env.ContentRootPath : root;
        }
        catch
        {
            return _env.ContentRootPath;
        }
    }

    private static string SanitizeName(string name)
    {
        var s = (name ?? string.Empty).Trim();
        if (s.Length == 0) return "skill-pack";

        var sb = new List<char>(s.Length);
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_' or '.')
            {
                sb.Add(char.ToLowerInvariant(c));
            }
            else if (char.IsWhiteSpace(c))
            {
                sb.Add('-');
            }
        }

        var result = new string(sb.ToArray()).Trim('-');
        return result.Length == 0 ? "skill-pack" : result;
    }

    private static string ExpandHome(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
                return path.Substring(2);
            return Path.Combine(home, path.Substring(2));
        }

        return path;
    }

    private async Task<bool> IsGitAvailableAsync(CancellationToken ct)
    {
        try
        {
            var rc = await RunGitAsync(new List<string> { "--version" }, workingDir: null, ct);
            return rc == 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<int> RunGitAsync(IReadOnlyList<string> args, string? workingDir, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(workingDir))
            psi.WorkingDirectory = workingDir!;

        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = false };
        proc.Start();

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);

        await proc.WaitForExitAsync(ct);

        var stdout = await Safe(stdoutTask);
        var stderr = await Safe(stderrTask);

        if (!string.IsNullOrWhiteSpace(stdout))
            _logger.LogDebug("[SkillPacksSync][git] {Stdout}", stdout.Trim());
        if (!string.IsNullOrWhiteSpace(stderr))
            _logger.LogDebug("[SkillPacksSync][git:stderr] {Stderr}", stderr.Trim());

        return proc.ExitCode;
    }

    private async Task<string?> TryGetHeadCommitAsync(string repoDir, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-C");
            psi.ArgumentList.Add(repoDir);
            psi.ArgumentList.Add("rev-parse");
            psi.ArgumentList.Add("HEAD");

            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = false };
            proc.Start();
            var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
            _ = await proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            var s = (stdout ?? string.Empty).Trim();
            return s.Length > 0 ? s : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string> Safe(Task<string> t)
    {
        try { return await t.ConfigureAwait(false); }
        catch { return string.Empty; }
    }

    private static void AppendAgentSkillsDirs(string skillsRoot)
    {
        var root = Path.GetFullPath(skillsRoot);
        var existing = (Environment.GetEnvironmentVariable("AEVATAR_AGENT_SKILLS_DIRS") ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(existing))
        {
            Environment.SetEnvironmentVariable("AEVATAR_AGENT_SKILLS_DIRS", root);
            return;
        }

        // Keep it simple: use ';' as separator (AIGAgentBase supports both ';' and ':').
        var parts = existing.Split([';', ':'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Any(p => string.Equals(Path.GetFullPath(p), root, StringComparison.OrdinalIgnoreCase)))
            return;

        Environment.SetEnvironmentVariable("AEVATAR_AGENT_SKILLS_DIRS", existing + ";" + root);
    }
}


