using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Scriban;
using Scriban.Runtime;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Aevatar.PaperReview.Prompty;

// ============================================================
//  PROMPTY LOADER
//  职责：加载和渲染 .prompty 文件
//  
//  设计决策：
//  - 自己解析 YAML front matter，避免 Semantic Kernel 依赖
//  - 使用 Scriban 渲染模板部分（复用现有能力）
//  - 支持 system/user 消息分离
// ============================================================

/// <summary>
/// Prompty 文件加载器 - 解析 YAML front matter 并渲染模板。
/// </summary>
public sealed partial class PromptyLoader
{
    private readonly string _promptsDir;
    private readonly ConcurrentDictionary<string, PromptyFile> _cache = new();
    private readonly ILogger<PromptyLoader> _logger;
    
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public PromptyLoader(ILogger<PromptyLoader> logger)
    {
        _logger = logger;
        _promptsDir = Path.Combine(AppContext.BaseDirectory, "prompts");
        
        // 回退到源码目录（开发时）
        if (!Directory.Exists(_promptsDir))
        {
            _promptsDir = Path.Combine(Directory.GetCurrentDirectory(), "prompts");
        }
    }

    // ─────────────────────────────────────────────────────────
    //  公开 API
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 加载 .prompty 文件。
    /// </summary>
    public PromptyFile Load(string name)
    {
        var fileName = name.EndsWith(".prompty") ? name : $"{name}.prompty";
        
        return _cache.GetOrAdd(fileName, n =>
        {
            var path = Path.Combine(_promptsDir, n);
            if (!File.Exists(path))
            {
                _logger.LogWarning("Prompty file not found: {Path}", path);
                return new PromptyFile { Name = n, RawTemplate = $"ERROR: {n} not found" };
            }
            
            var content = File.ReadAllText(path);
            return Parse(content, n);
        });
    }

    /// <summary>
    /// 渲染 prompty 模板。
    /// </summary>
    public PromptyRenderResult Render(string name, Dictionary<string, object> variables)
    {
        var prompty = Load(name);
        
        var systemPrompt = prompty.SystemTemplate != null 
            ? RenderTemplate(prompty.SystemTemplate, variables) 
            : null;
        
        var userPrompt = prompty.UserTemplate != null
            ? RenderTemplate(prompty.UserTemplate, variables)
            : RenderTemplate(prompty.RawTemplate, variables);
        
        return new PromptyRenderResult
        {
            SystemPrompt = systemPrompt,
            UserPrompt = userPrompt,
            Model = prompty.Model
        };
    }

    /// <summary>
    /// 渲染 prompty 为单一文本（兼容旧 API）。
    /// </summary>
    public string RenderAsText(string name, Dictionary<string, object> variables)
    {
        var result = Render(name, variables);
        
        if (result.SystemPrompt != null)
        {
            return $"{result.SystemPrompt}\n\n{result.UserPrompt}";
        }
        
        return result.UserPrompt;
    }

    // ─────────────────────────────────────────────────────────
    //  解析逻辑
    // ─────────────────────────────────────────────────────────

    private PromptyFile Parse(string content, string fileName)
    {
        // 分离 YAML front matter 和模板
        var (frontMatter, template) = SplitFrontMatter(content);
        
        if (string.IsNullOrWhiteSpace(frontMatter))
        {
            // 无 front matter，作为纯模板处理
            return new PromptyFile { Name = fileName, RawTemplate = template };
        }
        
        try
        {
            var meta = YamlDeserializer.Deserialize<PromptyMeta>(frontMatter);
            var (systemTemplate, userTemplate) = SplitMessages(template);
            
            return new PromptyFile
            {
                Name = meta.Name ?? fileName,
                Description = meta.Description ?? "",
                Model = ParseModelConfig(meta.Model),
                Inputs = ParseInputs(meta.Inputs),
                SystemTemplate = systemTemplate,
                UserTemplate = userTemplate,
                RawTemplate = template
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse prompty YAML: {File}", fileName);
            return new PromptyFile { Name = fileName, RawTemplate = template };
        }
    }

    private static (string FrontMatter, string Template) SplitFrontMatter(string content)
    {
        // Prompty 使用 --- 分隔 YAML front matter
        var match = FrontMatterRegex().Match(content);
        
        if (!match.Success)
        {
            return ("", content);
        }
        
        var frontMatter = match.Groups[1].Value.Trim();
        var template = content[(match.Index + match.Length)..].TrimStart('\r', '\n');
        
        return (frontMatter, template);
    }

    private static (string? System, string? User) SplitMessages(string template)
    {
        // 查找 system: 和 user: 标记
        var systemMatch = SystemBlockRegex().Match(template);
        var userMatch = UserBlockRegex().Match(template);
        
        if (!systemMatch.Success && !userMatch.Success)
        {
            // 无消息分离，返回整个模板作为 user
            return (null, template);
        }
        
        string? systemContent = null;
        string? userContent = null;
        
        if (systemMatch.Success)
        {
            var start = systemMatch.Index + systemMatch.Length;
            var end = userMatch.Success ? userMatch.Index : template.Length;
            systemContent = template[start..end].Trim();
        }
        
        if (userMatch.Success)
        {
            var start = userMatch.Index + userMatch.Length;
            userContent = template[start..].Trim();
        }
        
        return (systemContent, userContent);
    }

    private static PromptyModelConfig ParseModelConfig(PromptyModelMeta? meta)
    {
        if (meta == null) return new PromptyModelConfig();
        
        return new PromptyModelConfig
        {
            Api = meta.Api ?? "chat",
            Parameters = new PromptyModelParameters
            {
                MaxTokens = meta.Parameters?.MaxTokens ?? 4000,
                Temperature = meta.Parameters?.Temperature ?? 0.7,
                TopP = meta.Parameters?.TopP ?? 1.0
            }
        };
    }

    private static Dictionary<string, PromptyInput> ParseInputs(Dictionary<string, PromptyInputMeta>? inputs)
    {
        if (inputs == null) return new();
        
        return inputs.ToDictionary(
            kv => kv.Key,
            kv => new PromptyInput
            {
                Type = kv.Value.Type ?? "string",
                Description = kv.Value.Description,
                Default = kv.Value.Default
            }
        );
    }

    // ─────────────────────────────────────────────────────────
    //  模板渲染
    // ─────────────────────────────────────────────────────────

    private static string RenderTemplate(string template, Dictionary<string, object> variables)
    {
        // 预处理：Prompty 使用 {{var}} 语法，Scriban 需要 {{ var }}
        var processed = PreprocessTemplate(template);
        
        var scribanTemplate = Template.Parse(processed);
        if (scribanTemplate.HasErrors)
        {
            return $"Template error: {string.Join(", ", scribanTemplate.Messages)}";
        }
        
        var scriptObject = new ScriptObject();
        foreach (var (key, value) in variables)
        {
            scriptObject[key] = value;
        }
        
        var context = new TemplateContext();
        context.PushGlobal(scriptObject);
        
        return scribanTemplate.Render(context);
    }

    private static string PreprocessTemplate(string template)
    {
        // 保持简单：Prompty 和 Scriban 的 {{ }} 语法兼容
        // 只需要处理紧凑形式 {{var}} → {{ var }}
        return CompactVarRegex().Replace(template, "{{ $1 }}");
    }

    // ─────────────────────────────────────────────────────────
    //  正则表达式
    // ─────────────────────────────────────────────────────────

    [GeneratedRegex(@"^---\s*\n(.*?)\n---\s*\n", RegexOptions.Singleline)]
    private static partial Regex FrontMatterRegex();
    
    [GeneratedRegex(@"^system:\s*\n", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SystemBlockRegex();
    
    [GeneratedRegex(@"^user:\s*\n", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex UserBlockRegex();
    
    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex CompactVarRegex();
}

// ============================================================
//  YAML 元数据模型（内部用）
// ============================================================

internal sealed class PromptyMeta
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public PromptyModelMeta? Model { get; set; }
    public Dictionary<string, PromptyInputMeta>? Inputs { get; set; }
}

internal sealed class PromptyModelMeta
{
    public string? Api { get; set; }
    public PromptyParametersMeta? Parameters { get; set; }
}

internal sealed class PromptyParametersMeta
{
    public int MaxTokens { get; set; } = 4000;
    public double Temperature { get; set; } = 0.7;
    public double TopP { get; set; } = 1.0;
}

internal sealed class PromptyInputMeta
{
    public string? Type { get; set; }
    public string? Description { get; set; }
    public object? Default { get; set; }
}

// ============================================================
//  渲染结果
// ============================================================

/// <summary>
/// Prompty 渲染结果。
/// </summary>
public sealed class PromptyRenderResult
{
    /// <summary>System prompt（可选）</summary>
    public string? SystemPrompt { get; init; }
    
    /// <summary>User prompt</summary>
    public required string UserPrompt { get; init; }
    
    /// <summary>模型配置</summary>
    public PromptyModelConfig Model { get; init; } = new();
}
