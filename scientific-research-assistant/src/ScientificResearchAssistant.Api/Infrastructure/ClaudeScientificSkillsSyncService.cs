using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ScientificResearchAssistant.Api.Infrastructure;

/// <summary>
/// Best-effort Git sync for K-Dense "Claude Scientific Skills" repo.
/// <para/>
/// WHY:
/// - Keep a local on-disk skills pack (SKILL.md + scripts/references/assets) in sync with upstream.
/// - Enable our Agent Skills layer to load and execute bundled scripts at runtime.
/// </summary>
public sealed class ClaudeScientificSkillsSyncService
{
    private readonly IOptions<ClaudeScientificSkillsSyncOptions> _options;
    private readonly ILogger<ClaudeScientificSkillsSyncService> _logger;

    private readonly SemaphoreSlim _lock = new(1, 1);

    public ClaudeScientificSkillsSyncService(
        IOptions<ClaudeScientificSkillsSyncOptions> options,
        ILogger<ClaudeScientificSkillsSyncService> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<(bool Ok, string? SkillsRoot, string? Error)> TryEnsureSyncedAsync(CancellationToken ct)
    {
        var opt = _options.Value ?? new ClaudeScientificSkillsSyncOptions();

        if (!opt.Enabled)
            return (false, null, "disabled");

        var repoUrl = (opt.RepoUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(repoUrl))
            return (false, null, "RepoUrl is empty");

        var @ref = (opt.Ref ?? "main").Trim();
        if (string.IsNullOrWhiteSpace(@ref))
            @ref = "main";

        var repoDir = ResolveRepoDir(opt);
        var skillsRoot = Path.GetFullPath(Path.Combine(repoDir, opt.SkillsSubDir ?? "scientific-skills"));

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(Math.Clamp(opt.UpdateTimeoutMs, 1000, 10 * 60_000)));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        await _lock.WaitAsync(linked.Token);
        try
        {
            if (!await IsGitAvailableAsync(linked.Token))
            {
                _logger.LogWarning("[SkillsSync] git not found; skipping sync. RepoUrl={RepoUrl}", repoUrl);
                // Still return skillsRoot so callers can point AgentSkills there if user mounted it manually.
                return (false, skillsRoot, "git not found");
            }

            // Clone / update
            if (!Directory.Exists(repoDir) || !Directory.Exists(Path.Combine(repoDir, ".git")))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(repoDir)!);
                _logger.LogInformation("[SkillsSync] Cloning skills repo -> {Dir}", repoDir);

                var args = new List<string> { "clone" };
                if (opt.ShallowClone)
                {
                    args.Add("--depth");
                    args.Add(Math.Clamp(opt.ShallowDepth, 1, 50).ToString());
                }

                args.Add("--branch");
                args.Add(@ref);
                args.Add(repoUrl);
                args.Add(repoDir);

                var rc = await RunGitAsync(args, workingDir: null, linked.Token);
                if (rc != 0)
                {
                    return (false, skillsRoot, $"git clone failed (exitCode={rc})");
                }
            }
            else
            {
                _logger.LogInformation("[SkillsSync] Updating skills repo in {Dir}", repoDir);

                // fetch -> hard reset to origin/ref
                var fetchArgs = new List<string> { "-C", repoDir, "fetch", "origin", @ref, "--prune" };
                if (opt.ShallowClone)
                {
                    fetchArgs.Add("--depth");
                    fetchArgs.Add(Math.Clamp(opt.ShallowDepth, 1, 50).ToString());
                }

                var fetchRc = await RunGitAsync(fetchArgs, workingDir: null, linked.Token);
                if (fetchRc != 0)
                {
                    return (false, skillsRoot, $"git fetch failed (exitCode={fetchRc})");
                }

                var resetRc = await RunGitAsync(new List<string> { "-C", repoDir, "reset", "--hard", "FETCH_HEAD" }, null, linked.Token);
                if (resetRc != 0)
                {
                    return (false, skillsRoot, $"git reset failed (exitCode={resetRc})");
                }

                // Clean untracked files (avoid stale scripts)
                _ = await RunGitAsync(new List<string> { "-C", repoDir, "clean", "-fd" }, null, linked.Token);
            }

            if (!Directory.Exists(skillsRoot))
            {
                _logger.LogWarning(
                    "[SkillsSync] Repo synced but skills root not found: {SkillsRoot}. Check SkillsSubDir config.",
                    skillsRoot);
                return (false, skillsRoot, "skills root not found");
            }

            if (opt.SetAgentSkillsEnv)
            {
                AppendAgentSkillsDirs(skillsRoot);
            }

            _logger.LogInformation("[SkillsSync] Ready. SkillsRoot={SkillsRoot}", skillsRoot);
            return (true, skillsRoot, null);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _logger.LogWarning("[SkillsSync] Timed out while syncing skills repo (UpdateTimeoutMs={Ms}).", opt.UpdateTimeoutMs);
            return (false, skillsRoot, "timeout");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SkillsSync] Sync failed (best-effort): {Message}", ex.Message);
            return (false, skillsRoot, ex.Message);
        }
        finally
        {
            _lock.Release();
        }
    }

    private string ResolveRepoDir(ClaudeScientificSkillsSyncOptions opt)
    {
        var raw = (opt.InstallDir ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(raw))
        {
            raw = ExpandHome(raw);
            return Path.GetFullPath(raw);
        }

        // Default: ~/.aevatar/skillpacks/claude-scientific-skills
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Path.GetTempPath();
        }

        return Path.Combine(home, ".aevatar", "skillpacks", "claude-scientific-skills");
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
            _logger.LogDebug("[SkillsSync][git] {Stdout}", stdout.Trim());
        if (!string.IsNullOrWhiteSpace(stderr))
            _logger.LogDebug("[SkillsSync][git:stderr] {Stderr}", stderr.Trim());

        return proc.ExitCode;
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


