using System.Diagnostics;
using System.Text;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Core.AgentSkills;
using Aevatar.Agents.AI.Core.Embeddings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.VibeResearching.Infrastructure.MongoDB.SkillPacks;

// SkillPackSyncMode, SkillPacksSyncResult, and SkillPackSyncEntry are now imported from Infrastructure.Domain

/// <summary>
/// Best-effort Git sync for multiple "Agent Skills" packs.
/// <para/>
/// Primary use case: keep large upstream packs (e.g. K-Dense Claude Scientific Skills) updated automatically.
/// </summary>
public sealed class SkillPacksSyncService : Aevatar.VibeResearching.Infrastructure.ISkillPacksSyncService
{
    private readonly IOptionsMonitor<SkillPacksOptions> _packs;
    private readonly IOptions<LLMProvidersConfig> _llm;
    private readonly IAIAgentEmbeddingFactory? _embeddingFactory;
    private readonly IHostEnvironment _env;
    private readonly ILogger<SkillPacksSyncService> _logger;
    private readonly SkillPacksSyncProgress _progress;

    private readonly SemaphoreSlim _lock = new(1, 1);

    // ============================================================
    //  Sync status (best-effort)
    //
    //  WHY:
    //  - Startup sync can fail (no network/git). We want to retry later.
    //  - Callers (e.g. per-session runtime) need a cheap "should retry?" signal.
    // ============================================================
    private volatile SkillPacksSyncResult? _lastResult;
    private DateTimeOffset _lastAttemptUtc = DateTimeOffset.MinValue;

    public SkillPacksSyncService(
        IOptionsMonitor<SkillPacksOptions> packs,
        IOptions<LLMProvidersConfig> llm,
        IAIAgentEmbeddingFactory? embeddingFactory,
        IHostEnvironment env,
        ILogger<SkillPacksSyncService> logger,
        SkillPacksSyncProgress progress)
    {
        _packs = packs;
        _llm = llm;
        _embeddingFactory = embeddingFactory;
        _env = env;
        _logger = logger;
        _progress = progress;
    }

    public bool HasEnabledPacks =>
        _packs.CurrentValue?.Packs?.Any(p => p.Enabled && !string.IsNullOrWhiteSpace(p.RepoUrl)) == true;

    public bool LastSyncOk => _lastResult?.Ok == true;

    public DateTimeOffset LastAttemptUtc => _lastAttemptUtc;

    public SkillPacksSyncResult? LastResult => _lastResult;

    public async Task<SkillPacksSyncResult> TryEnsureSyncedAsync(SkillPackSyncMode mode, CancellationToken ct)
    {
        var specs = GetEnabledSpecs(mode);
        if (specs.Count == 0)
        {
            var none = new SkillPacksSyncResult { Ok = false, Error = "no enabled skill packs configured" };
            _lastResult = none;
            return none;
        }

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(10 * 60_000));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        await _lock.WaitAsync(linked.Token);
        try
        {
            _lastAttemptUtc = DateTimeOffset.UtcNow;
            _progress.StartRun(mode, specs.Count);

            if (!await IsGitAvailableAsync(linked.Token))
            {
                _logger.LogWarning("[SkillPacksSync] git not found; skipping sync.");
                var missingGit = new SkillPacksSyncResult
                {
                    Ok = false,
                    Error = "git not found"
                };
                _lastResult = missingGit;
                _progress.Step("git_missing", "git not found; skipping sync.");
                _progress.FinishRun(ok: false, error: missingGit.Error);
                return missingGit;
            }

            var results = new List<SkillPackSyncEntry>();
            var allOk = true;

            for (var i = 0; i < specs.Count; i++)
            {
                linked.Token.ThrowIfCancellationRequested();
                var spec = specs[i];

                // Publish "current pack" info for the UI.
                var repoDir = ResolveRepoDir(spec);
                var skillsRoot = Path.GetFullPath(Path.Combine(repoDir, spec.SkillsSubDir ?? "skills"));
                _progress.BeginPack(i, spec, repoDir, skillsRoot);

                var r = await TrySyncOneAsync(spec, linked.Token);
                results.Add(r);
                _progress.PackResult(r);
                if (!r.Ok) allOk = false;
            }

            var result = new SkillPacksSyncResult
            {
                Ok = allOk,
                Packs = results,
                Error = allOk ? null : "one or more packs failed"
            };
            _lastResult = result;
            _progress.FinishRun(allOk, result.Error);
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    private List<SkillPackSpec> GetEnabledSpecs(SkillPackSyncMode mode)
    {
        var list = new List<SkillPackSpec>();

        var configured = _packs.CurrentValue?.Packs ?? new List<SkillPackSpec>();
        if (configured.Count == 0)
        {
            return list;
        }

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

    private async Task<SkillPackSyncEntry> TrySyncOneAsync(SkillPackSpec spec, CancellationToken ct)
    {
        var repoUrl = (spec.RepoUrl ?? string.Empty).Trim();
        var @ref = string.IsNullOrWhiteSpace(spec.Ref) ? "main" : spec.Ref.Trim();

        var repoDir = ResolveRepoDir(spec);
        var skillsRoot = Path.GetFullPath(Path.Combine(repoDir, spec.SkillsSubDir ?? "skills"));

        using var timeoutCts =
            new CancellationTokenSource(TimeSpan.FromMilliseconds(Math.Clamp(spec.UpdateTimeoutMs, 1000, 10 * 60_000)));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            // Clone / update
            if (!Directory.Exists(repoDir) || !Directory.Exists(Path.Combine(repoDir, ".git")))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(repoDir)!);
                _logger.LogInformation("[SkillPacksSync] Cloning {Name} -> {Dir} (ref={Ref} url={Url})",
                    spec.Name, repoDir, @ref, repoUrl);
                _progress.Step("git_clone", $"git clone {repoUrl} (ref={@ref})");

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
                _logger.LogInformation("[SkillPacksSync] Updating {Name} in {Dir} (ref={Ref} url={Url})",
                    spec.Name, repoDir, @ref, repoUrl);
                _progress.Step("git_fetch", $"git fetch {repoUrl} (ref={@ref})");

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

                var resetRc = await RunGitAsync(new List<string> { "-C", repoDir, "reset", "--hard", "FETCH_HEAD" },
                    null, linked.Token);
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
                _progress.Step("git_clean", "git clean -fd");
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

            // Build embeddings index best-effort (for semantic find_helpful_skills).
            _progress.Step("index", "building embeddings index (best-effort)");
            var (idxOk, idxFile, idxErr) = await TryBuildEmbeddingsIndexBestEffortAsync(skillsRoot, linked.Token);

            return new SkillPackSyncEntry
            {
                Name = spec.Name,
                RepoUrl = repoUrl,
                Ref = @ref,
                RepoDir = repoDir,
                SkillsRoot = skillsRoot,
                Commit = commit,
                EmbeddingsIndexOk = idxOk,
                EmbeddingsIndexFile = idxFile,
                EmbeddingsIndexError = idxErr,
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
            _logger.LogWarning(ex, "[SkillPacksSync] Sync failed for {Name} (best-effort): {Message}", spec.Name,
                ex.Message);
            _progress.Step("error", $"sync failed: {ex.Message}");
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
        // ContentRoot is ".../apps/Aevatar.VibeResearching/src/VibeResearching.Api"
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

    private string GetSkillIndexDir()
    {
        var dir = Path.Combine(GetAssistantRoot(), ".skillpacks", ".index");
        Directory.CreateDirectory(dir);

        // Make sure AIGAgentBase uses the same index dir.
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AgentSkillsEmbeddingsIndex.IndexDirEnv)))
        {
            Environment.SetEnvironmentVariable(AgentSkillsEmbeddingsIndex.IndexDirEnv, dir);
        }

        return dir;
    }

    private async Task<(bool? Ok, string? IndexFile, string? Error)> TryBuildEmbeddingsIndexBestEffortAsync(
        string skillsRoot,
        CancellationToken ct)
    {
        try
        {
            // If embeddings are not configured, don't treat as failure.
            var providerCfg = TryGetDefaultProviderConfig();
            if (providerCfg == null || providerCfg.Embeddings == null)
                return (null, null, "embeddings_not_configured");

            if (_embeddingFactory == null)
                return (null, null, "embedding_factory_not_available");

            var generator = await _embeddingFactory.CreateAsync(providerCfg, ct);
            if (generator == null)
                return (null, null, "embedding_generator_not_available");

            var options = BuildEmbeddingOptions(providerCfg);
            var indexDir = GetSkillIndexDir();
            var indexFile = AgentSkillsEmbeddingsIndex.GetIndexFilePathForRoot(skillsRoot, indexDir);

            // Discover SKILL.md docs under this root (bounded depth).
            async Task<IReadOnlyList<AgentSkillsEmbeddingDocument>> Discover(CancellationToken token)
            {
                await Task.CompletedTask;
                return DiscoverSkillDocsForEmbedding(skillsRoot, token);
            }

            _ = await AgentSkillsEmbeddingsIndex.EnsureIndexAsync(
                skillsRoot,
                Discover,
                generator,
                options,
                indexDir,
                _logger,
                progress: null,
                cancellationToken: ct);

            return (true, indexFile, null);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[SkillPacksSync] Embeddings index build failed (best-effort): {Message}", ex.Message);
            return (false, null, ex.Message);
        }
    }

    private LLMProviderConfig? TryGetDefaultProviderConfig()
    {
        var llm = _llm.Value;
        if (llm == null || llm.Providers.Count == 0)
            return null;

        var name = string.IsNullOrWhiteSpace(llm.Default) ? "default" : llm.Default.Trim();
        if (llm.Providers.TryGetValue(name, out var cfg))
            return cfg;

        // Fallback: first provider
        return llm.Providers.Values.FirstOrDefault();
    }

    private static EmbeddingGenerationOptions BuildEmbeddingOptions(LLMProviderConfig providerCfg)
    {
        var options = new EmbeddingGenerationOptions();

        if (providerCfg.Embeddings != null)
        {
            if (!string.IsNullOrWhiteSpace(providerCfg.Embeddings.Model))
            {
                options.ModelId = providerCfg.Embeddings.Model;
            }
            else if (!string.IsNullOrWhiteSpace(providerCfg.Model))
            {
                options.ModelId = providerCfg.Model;
            }

            if (providerCfg.Embeddings.Dimensions.HasValue)
            {
                options.Dimensions = providerCfg.Embeddings.Dimensions;
            }
        }
        else if (!string.IsNullOrWhiteSpace(providerCfg.Model))
        {
            options.ModelId = providerCfg.Model;
        }

        return options;
    }

    private IReadOnlyList<AgentSkillsEmbeddingDocument> DiscoverSkillDocsForEmbedding(string skillsRoot,
        CancellationToken ct)
    {
        // This is intentionally simple and best-effort:
        // - find directories containing SKILL.md up to max depth 3
        // - parse front matter name/description minimally (fallback to folder name)
        const int maxDepth = 3;
        const int maxSkills = 2000;

        var root = Path.GetFullPath(skillsRoot);
        var results = new List<AgentSkillsEmbeddingDocument>();

        if (!Directory.Exists(root))
            return results;

        var queue = new Queue<(string Dir, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var (dir, depth) = queue.Dequeue();
            if (depth > maxDepth)
                continue;

            var skillFile = Path.Combine(dir, "SKILL.md");
            if (File.Exists(skillFile))
            {
                var folderName =
                    Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var (name, desc) = TryParseNameAndDescription(skillFile, folderName);
                long ticks;
                try
                {
                    ticks = File.GetLastWriteTimeUtc(skillFile).Ticks;
                }
                catch
                {
                    ticks = 0;
                }

                results.Add(new AgentSkillsEmbeddingDocument
                {
                    Name = name,
                    FolderName = folderName,
                    DirectoryPath = dir,
                    SkillFilePath = skillFile,
                    SkillFileLastWriteUtcTicks = ticks,
                    TextToEmbed = $"{name}\n{desc}"
                });

                if (results.Count >= maxSkills)
                    break;
            }

            if (depth == maxDepth)
                continue;

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(dir);
            }
            catch
            {
                continue;
            }

            foreach (var child in children.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                if (ShouldSkipDirForDiscovery(child))
                    continue;
                queue.Enqueue((child, depth + 1));
            }
        }

        return results;
    }

    private static bool ShouldSkipDirForDiscovery(string dirPath)
    {
        var name = Path.GetFileName(dirPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(name))
            return true;

        if (name.StartsWith(".", StringComparison.Ordinal))
            return true;

        return string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "obj", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "node_modules", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "scripts", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "references", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "assets", StringComparison.OrdinalIgnoreCase);
    }

    private static (string Name, string Description) TryParseNameAndDescription(string skillFilePath,
        string fallbackName)
    {
        try
        {
            // Read a small head; SKILL.md front matter is expected at top.
            using var fs = new FileStream(skillFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var max = (int)Math.Min(32 * 1024, fs.Length);
            if (max <= 0) return (fallbackName, $"Agent skill at '{fallbackName}'");

            var buf = new byte[max];
            var read = fs.Read(buf, 0, max);
            if (read <= 0) return (fallbackName, $"Agent skill at '{fallbackName}'");

            var head = Encoding.UTF8.GetString(buf, 0, read);
            if (!head.StartsWith("---", StringComparison.Ordinal))
                return (fallbackName, $"Agent skill at '{fallbackName}'");

            // front matter ends at second '---' line.
            var idx = head.IndexOf("\n---", StringComparison.Ordinal);
            if (idx < 0) return (fallbackName, $"Agent skill at '{fallbackName}'");

            var yaml = head.Substring(0, idx).Replace("\r\n", "\n");
            var lines = yaml.Split('\n');
            var name = fallbackName;
            var desc = $"Agent skill at '{fallbackName}'";

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
                {
                    name = line.Substring("name:".Length).Trim();
                }
                else if (line.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
                {
                    desc = line.Substring("description:".Length).Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(name))
                name = fallbackName;

            if (string.IsNullOrWhiteSpace(desc))
                desc = $"Agent skill at '{fallbackName}'";

            return (name, desc);
        }
        catch
        {
            return (fallbackName, $"Agent skill at '{fallbackName}'");
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
        try
        {
            return await t.ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
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
