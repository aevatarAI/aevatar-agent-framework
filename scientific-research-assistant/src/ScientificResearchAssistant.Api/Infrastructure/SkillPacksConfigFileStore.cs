using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ScientificResearchAssistant.Api.Infrastructure;

// ============================================================
//  SkillPacksConfigFileStore (local file, gitignored)
//
//  中文说明：
//  - 管理 `src/ScientificResearchAssistant.Api/skillpacks.json`
//  - 支持 UI 一键追加/更新 pack
//  - 原子写入：避免 reloadOnChange 读到半截文件
// ============================================================
public sealed class SkillPacksConfigFileStore
{
    private readonly IHostEnvironment _env;
    private readonly ILogger<SkillPacksConfigFileStore> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SkillPacksConfigFileStore(IHostEnvironment env, ILogger<SkillPacksConfigFileStore> logger)
    {
        _env = env;
        _logger = logger;
    }

    public sealed class SkillPacksConfigFile
    {
        public SkillPacksOptions SkillPacks { get; set; } = new();
    }

    public string GetSkillPacksJsonPath()
        => Path.Combine(_env.ContentRootPath, "skillpacks.json");

    public async Task<SkillPacksConfigFile> LoadAsync(CancellationToken ct)
    {
        var path = GetSkillPacksJsonPath();
        if (!File.Exists(path))
        {
            return new SkillPacksConfigFile
            {
                SkillPacks = new SkillPacksOptions { Packs = new List<SkillPackSpec>() }
            };
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            if (string.IsNullOrWhiteSpace(json))
                return new SkillPacksConfigFile { SkillPacks = new SkillPacksOptions { Packs = new List<SkillPackSpec>() } };

            var model = JsonSerializer.Deserialize<SkillPacksConfigFile>(json, JsonOptions);
            model ??= new SkillPacksConfigFile();
            model.SkillPacks ??= new SkillPacksOptions();
            model.SkillPacks.Packs ??= new List<SkillPackSpec>();
            return model;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[SkillPacksConfig] Failed to parse skillpacks.json (best-effort). Creating new.");
            return new SkillPacksConfigFile { SkillPacks = new SkillPacksOptions { Packs = new List<SkillPackSpec>() } };
        }
    }

    public async Task SaveAsync(SkillPacksConfigFile model, CancellationToken ct)
    {
        var path = GetSkillPacksJsonPath();
        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(dir))
            throw new InvalidOperationException("skillpacks.json path is invalid");

        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(model, JsonOptionsIndented);

        // Atomic-ish write: tmp then replace.
        var tmp = Path.Combine(dir, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tmp, json, ct);

        try
        {
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            TryDelete(tmp);
        }
    }

    public async Task<SkillPackSpec> UpsertPackAsync(SkillPackSpec pack, CancellationToken ct)
    {
        if (pack == null) throw new ArgumentNullException(nameof(pack));

        await _lock.WaitAsync(ct);
        try
        {
            var model = await LoadAsync(ct);
            model.SkillPacks ??= new SkillPacksOptions();
            model.SkillPacks.Packs ??= new List<SkillPackSpec>();

            var repoUrl = (pack.RepoUrl ?? string.Empty).Trim();
            var name = (pack.Name ?? string.Empty).Trim();

            SkillPackSpec? existing = null;
            if (!string.IsNullOrWhiteSpace(repoUrl))
            {
                existing = model.SkillPacks.Packs.FirstOrDefault(p =>
                    p != null &&
                    !string.IsNullOrWhiteSpace(p.RepoUrl) &&
                    string.Equals(p.RepoUrl.Trim(), repoUrl, StringComparison.OrdinalIgnoreCase));
            }

            if (existing == null && !string.IsNullOrWhiteSpace(name))
            {
                existing = model.SkillPacks.Packs.FirstOrDefault(p =>
                    p != null &&
                    !string.IsNullOrWhiteSpace(p.Name) &&
                    string.Equals(p.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
            }

            if (existing == null)
            {
                model.SkillPacks.Packs.Add(pack);
            }
            else
            {
                // Merge: keep it explicit and predictable.
                existing.Name = name.Length == 0 ? existing.Name : name;
                existing.RepoUrl = repoUrl.Length == 0 ? existing.RepoUrl : repoUrl;
                existing.Ref = string.IsNullOrWhiteSpace(pack.Ref) ? existing.Ref : pack.Ref;
                existing.SkillsSubDir = string.IsNullOrWhiteSpace(pack.SkillsSubDir) ? existing.SkillsSubDir : pack.SkillsSubDir;
                existing.Enabled = pack.Enabled;
                existing.AutoUpdateOnStartup = pack.AutoUpdateOnStartup;
                existing.InstallDir = string.IsNullOrWhiteSpace(pack.InstallDir) ? existing.InstallDir : pack.InstallDir;
                existing.UpdateTimeoutMs = pack.UpdateTimeoutMs;
                existing.ShallowClone = pack.ShallowClone;
                existing.ShallowDepth = pack.ShallowDepth;
                existing.SetAgentSkillsEnv = pack.SetAgentSkillsEnv;
                pack = existing;
            }

            // Stable ordering for diff readability.
            model.SkillPacks.Packs = model.SkillPacks.Packs
                .Where(p => p != null)
                .OrderBy(p => p!.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList()!;

            await SaveAsync(model, ct);
            return pack;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions JsonOptionsIndented = new()
    {
        WriteIndented = true
    };
}


