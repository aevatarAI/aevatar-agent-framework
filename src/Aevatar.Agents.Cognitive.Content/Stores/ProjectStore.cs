using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Cognitive.Core;
using Aevatar.Agents.Cognitive.Content;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Aevatar.Agents.Cognitive.Content;

// ============================================================
//  PROJECT STORE
//  YAML 项目配置管理
// ============================================================

/// <summary>
/// 项目存储服务。
/// 从 YAML 文件加载和保存项目配置。
/// </summary>
public sealed class ProjectStore
{
    private readonly string _projectsPath;
    private readonly string _archivePath;
    private readonly ILogger<ProjectStore> _logger;
    private readonly IDeserializer _deserializer;
    private readonly ISerializer _serializer;

    public ProjectStore(ILogger<ProjectStore> logger)
    {
        _logger = logger;
        _projectsPath = Path.Combine(Directory.GetCurrentDirectory(), "projects");
        _archivePath = Path.Combine(Directory.GetCurrentDirectory(), "projects_archive");
        
        // 确保目录存在
        Directory.CreateDirectory(_projectsPath);
        Directory.CreateDirectory(_archivePath);

        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)  // snake_case: worker_count -> WorkerCount
            .IgnoreUnmatchedProperties()
            .Build();

        _serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)  // snake_case: WorkerCount -> worker_count
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();

        _logger.LogInformation("ProjectStore initialized. Projects path: {Path}", _projectsPath);
    }

    // ─────────────────────────────────────────────────────────
    //  项目读取（实时从文件系统加载）
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 获取所有项目（实时读取文件系统）。
    /// </summary>
    public IReadOnlyList<MeshProject> GetAllProjects()
    {
        var projects = new List<MeshProject>();
        
        try
        {
            var files = Directory.GetFiles(_projectsPath, "*.yaml")
                .Concat(Directory.GetFiles(_projectsPath, "*.yml"))
                .OrderBy(f => Path.GetFileName(f));

            foreach (var file in files)
            {
                try
                {
                    var project = LoadProjectFile(file);
                    if (project != null)
                    {
                        projects.Add(project);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load project file: {File}", file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read projects directory: {Path}", _projectsPath);
        }

        return projects;
    }

    /// <summary>
    /// 获取单个项目。
    /// </summary>
    public MeshProject? GetProject(string id)
    {
        var filePath = GetProjectFilePath(id);
        if (filePath == null) return null;
        return LoadProjectFile(filePath);
    }

    /// <summary>
    /// 添加新项目并保存为单独文件。
    /// </summary>
    public string AddProject(YamlProjectDefinition definition)
    {
        // 生成 ID（如果没有）
        if (string.IsNullOrEmpty(definition.Id))
        {
            definition.Id = $"p_{Guid.NewGuid():N}"[..12];
        }

        // 检查 ID 是否已存在
        if (GetProjectFilePath(definition.Id) != null)
        {
            throw new InvalidOperationException($"Project with ID '{definition.Id}' already exists");
        }

        // 保存到文件
        SaveProjectFile(definition);

        _logger.LogInformation("Added project: {Id} - {Name}", definition.Id, definition.Name);
        return definition.Id;
    }

    /// <summary>
    /// 更新项目。
    /// </summary>
    public bool UpdateProject(string id, YamlProjectDefinition definition)
    {
        var filePath = GetProjectFilePath(id);
        if (filePath == null) return false;

        definition.Id = id; // 确保 ID 不变
        definition.UpdatedAt = DateTime.UtcNow;
        SaveProjectFile(definition);

        _logger.LogInformation("Updated project: {Id}", id);
        return true;
    }

    /// <summary>
    /// 删除项目（归档到 projects_archive/，非真删）。
    /// </summary>
    public bool DeleteProject(string id)
    {
        var filePath = GetProjectFilePath(id);
        if (filePath == null) return false;

        try
        {
            // 读取项目内容
            var yaml = File.ReadAllText(filePath);
            var project = _deserializer.Deserialize<YamlProjectDefinition>(yaml);
            if (project == null) return false;

            // 归档（移动文件到 archive 目录并重命名）
            var safeName = SanitizeFileName(project.Name ?? id);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var archiveFileName = $"{safeName}_{timestamp}.yaml";
            var archivePath = Path.Combine(_archivePath, archiveFileName);

            // 添加归档头
            var header = $"""
                # ═══════════════════════════════════════════════════════════════════════════
                #  ARCHIVED PROJECT: {project.Name}
                #  Archived at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
                #  Original ID: {project.Id}
                #  Original File: {Path.GetFileName(filePath)}
                # ═══════════════════════════════════════════════════════════════════════════

                """;
            File.WriteAllText(archivePath, header + yaml);

            // 删除原文件
            File.Delete(filePath);

            _logger.LogInformation("Archived project: {Id} - {Name} → {Archive}", id, project.Name, archiveFileName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to archive project: {Id}", id);
            return false;
        }
    }

    /// <summary>
    /// 获取已归档项目列表。
    /// </summary>
    public IReadOnlyList<(string FileName, YamlProjectDefinition Project)> GetArchivedProjects()
    {
        var result = new List<(string, YamlProjectDefinition)>();
        
        try
        {
            var files = Directory.GetFiles(_archivePath, "*.yaml")
                .Concat(Directory.GetFiles(_archivePath, "*.yml"));
            foreach (var file in files.OrderByDescending(f => File.GetLastWriteTime(f)))
            {
                try
                {
                    var yaml = File.ReadAllText(file);
                    var project = _deserializer.Deserialize<YamlProjectDefinition>(yaml);
                    if (project != null)
                    {
                        result.Add((Path.GetFileName(file), project));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read archived project: {File}", file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list archived projects");
        }
        
        return result;
    }

    /// <summary>
    /// 恢复归档项目。
    /// </summary>
    public bool RestoreProject(string archiveFileName)
    {
        try
        {
            var archiveFilePath = Path.Combine(_archivePath, archiveFileName);
            if (!File.Exists(archiveFilePath)) return false;

            var yaml = File.ReadAllText(archiveFilePath);
            var project = _deserializer.Deserialize<YamlProjectDefinition>(yaml);
            if (project == null) return false;

            // 检查 ID 冲突，如果冲突则生成新 ID
            if (GetProjectFilePath(project.Id!) != null)
            {
                project.Id = $"restored_{Guid.NewGuid():N}"[..16];
            }

            project.UpdatedAt = DateTime.UtcNow;
            
            // 保存为新文件
            SaveProjectFile(project);

            // 删除归档文件
            File.Delete(archiveFilePath);

            _logger.LogInformation("Restored project: {Id} - {Name}", project.Id, project.Name);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore project: {File}", archiveFileName);
            return false;
        }
    }

    // ─────────────────────────────────────────────────────────
    //  文件操作辅助方法
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 根据项目 ID 查找文件路径。
    /// </summary>
    private string? GetProjectFilePath(string id)
    {
        // 优先查找 {id}.yaml
        var exactPath = Path.Combine(_projectsPath, $"{id}.yaml");
        if (File.Exists(exactPath)) return exactPath;

        var exactPathYml = Path.Combine(_projectsPath, $"{id}.yml");
        if (File.Exists(exactPathYml)) return exactPathYml;

        // 扫描所有文件查找匹配的 ID
        try
        {
            var files = Directory.GetFiles(_projectsPath, "*.yaml")
                .Concat(Directory.GetFiles(_projectsPath, "*.yml"));

            foreach (var file in files)
            {
                try
                {
                    var yaml = File.ReadAllText(file);
                    var project = _deserializer.Deserialize<YamlProjectDefinition>(yaml);
                    if (project?.Id == id)
                    {
                        return file;
                    }
                }
                catch
                {
                    // 忽略解析错误
                }
            }
        }
        catch
        {
            // 忽略目录读取错误
        }

        return null;
    }

    /// <summary>
    /// 加载项目文件。
    /// </summary>
    private MeshProject? LoadProjectFile(string filePath)
    {
        var yaml = File.ReadAllText(filePath);
        var def = _deserializer.Deserialize<YamlProjectDefinition>(yaml);
        return def != null ? ToMeshProject(def) : null;
    }

    /// <summary>
    /// 保存项目文件。
    /// </summary>
    private void SaveProjectFile(YamlProjectDefinition definition)
    {
        var safeName = SanitizeFileName(definition.Name ?? definition.Id ?? "project");
        var fileName = $"{safeName}.yaml";
        var filePath = Path.Combine(_projectsPath, fileName);

        // 如果同名文件存在但 ID 不同，添加 ID 后缀
        if (File.Exists(filePath))
        {
            try
            {
                var existing = _deserializer.Deserialize<YamlProjectDefinition>(File.ReadAllText(filePath));
                if (existing?.Id != definition.Id)
                {
                    fileName = $"{safeName}_{definition.Id}.yaml";
                    filePath = Path.Combine(_projectsPath, fileName);
                }
            }
            catch
            {
                // 文件解析失败，使用带 ID 的文件名
                fileName = $"{safeName}_{definition.Id}.yaml";
                filePath = Path.Combine(_projectsPath, fileName);
            }
        }

        var yaml = _serializer.Serialize(definition);
        var header = $"""
            # ═══════════════════════════════════════════════════════════════════════════
            #  PROJECT: {definition.Name}
            #  ID: {definition.Id}
            #  Strategy: {definition.Strategy}
            # ═══════════════════════════════════════════════════════════════════════════

            """;

        File.WriteAllText(filePath, header + yaml);
        _logger.LogDebug("Saved project to: {Path}", filePath);
    }

    private static string SanitizeFileName(string name)
    {
        // 移除非法字符
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        
        // 限制长度
        if (sanitized.Length > 50) sanitized = sanitized[..50];
        
        // 如果结果为空，用默认名
        return string.IsNullOrWhiteSpace(sanitized) ? "project" : sanitized.Trim();
    }

    // ─────────────────────────────────────────────────────────
    //  转换方法
    // ─────────────────────────────────────────────────────────

    private static MeshProject ToMeshProject(YamlProjectDefinition def)
    {
        var strategy = Enum.TryParse<StrategyKind>(def.Strategy, true, out var s) ? s : StrategyKind.Maker;
        var options = BuildOptions(def, strategy);

        return new MeshProject
        {
            Id = def.Id ?? throw new InvalidOperationException("Project ID is required"),
            Name = def.Name ?? "Unnamed Project",
            Description = def.Description ?? "",
            Icon = def.Icon ?? "🔬",
            Strategy = strategy,
            Task = def.Task?.Trim() ?? throw new InvalidOperationException("Task is required"),
            Options = options
        };
    }

    private static ReasoningOptions BuildOptions(YamlProjectDefinition def, StrategyKind strategy)
    {
        var opts = def.Options ?? new YamlProjectOptions();

        return strategy switch
        {
            // Direct: 最简单，几乎不需要配置
            StrategyKind.Direct => new ReasoningOptions
            {
                ProviderName = opts.ProviderName ?? AevatarAgentsConstants.DefaultProviderName,
                DirectSystemPrompt = opts.SystemPrompt,
                MaxLlmCalls = 1,
                MaxTokens = opts.MaxTokens ?? 100_000
            },
            // MAKER: 多 Agent 共识
            StrategyKind.Maker => new ReasoningOptions
            {
                ProviderName = opts.ProviderName ?? AevatarAgentsConstants.DefaultProviderName,
                MakerReliability = Enum.TryParse<MakerReliability>(opts.Reliability, true, out var r) ? r : MakerReliability.Medium,
                MaxLlmCalls = opts.MaxLlmCalls ?? 500,
                MaxTokens = opts.MaxTokens ?? 2_000_000,
                Context = opts.Context
            },
            // C-UoT: 组合式
            StrategyKind.UotCombinational => ReasoningOptions.ForUotCombinational(
                opts.DomainHint,
                opts.MaxAnalogies ?? 5,
                opts.MaxCandidates ?? 10,
                opts.ProviderName ?? AevatarAgentsConstants.DefaultProviderName),
            // E-UoT: 探索式
            StrategyKind.UotExploratory => ReasoningOptions.ForUotExploratory(
                opts.DomainHint,
                opts.MaxAnalogies ?? 5,
                opts.MaxCandidates ?? 10,
                opts.MaxOutsideThoughts ?? 10,
                opts.ExplorationDirections ?? 3,
                opts.ProviderName ?? AevatarAgentsConstants.DefaultProviderName),
            // T-UoT: 变革式
            StrategyKind.UotTransformative => ReasoningOptions.ForUotTransformative(
                opts.DomainHint,
                opts.MaxRuleSets ?? 3,
                opts.MutationsPerSet ?? 3,
                opts.MinRadicality ?? 0.5f,
                opts.ProviderName ?? AevatarAgentsConstants.DefaultProviderName),
            // Cognitive DSL: v2 工作流驱动
            StrategyKind.Cognitive => ReasoningOptions.ForCognitive(
                opts.Workflow ?? "direct",
                opts.WorkerCount ?? 5,
                opts.ConsensusK ?? 2,
                opts.MaxRounds ?? 10,
                opts.MaxDepth ?? 10,
                opts.SemanticSimilarity ?? 0.85f,
                opts.TimeoutMinutes ?? 30,
                opts.ProviderName ?? AevatarAgentsConstants.DefaultProviderName),
            // 其他默认
            _ => new ReasoningOptions { ProviderName = opts.ProviderName ?? AevatarAgentsConstants.DefaultProviderName }
        };
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  YAML Data Models
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// YAML 项目定义。
/// </summary>
public class YamlProjectDefinition
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public string? Strategy { get; set; }
    public string? Task { get; set; }
    public YamlProjectOptions? Options { get; set; }
    
    // 任务模板相关
    public string? TaskTemplate { get; set; }
    public string? CustomInstruction { get; set; }
    
    // 内容来源相关
    public YamlContentSource? Content { get; set; }
    
    // 元数据
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// YAML 项目选项。
/// </summary>
public class YamlProjectOptions
{
    // 通用
    public string? ProviderName { get; set; }
    public int? MaxLlmCalls { get; set; }
    public long? MaxTokens { get; set; }
    public Dictionary<string, string>? Context { get; set; }

    // DIRECT
    public string? SystemPrompt { get; set; }

    // MAKER
    public string? Reliability { get; set; }

    // UoT 通用
    public string? DomainHint { get; set; }
    public int? MaxAnalogies { get; set; }
    public int? MaxCandidates { get; set; }

    // E-UoT
    public int? MaxOutsideThoughts { get; set; }
    public int? ExplorationDirections { get; set; }

    // T-UoT
    public int? MaxRuleSets { get; set; }
    public int? MutationsPerSet { get; set; }
    public float? MinRadicality { get; set; }

    // COGNITIVE DSL
    public string? Workflow { get; set; }
    public int? WorkerCount { get; set; }
    public int? ConsensusK { get; set; }
    public int? MaxRounds { get; set; }
    public int? MaxDepth { get; set; }
    public float? SemanticSimilarity { get; set; }
    public int? TimeoutMinutes { get; set; }
}

/// <summary>
/// YAML 内容来源。
/// </summary>
public class YamlContentSource
{
    public string? FilePath { get; set; }
    public string? DirectoryPath { get; set; }
    public List<string>? FilePaths { get; set; }
    public string? UploadId { get; set; }
    public string? ContentDescription { get; set; }
    public List<string>? Extensions { get; set; }
    public bool Recursive { get; set; }
}

