namespace Aevatar.PaperReview.Prompty;

// ============================================================
//  PROMPTY FILE MODEL
//  职责：表示 .prompty 文件的结构化内容
//  
//  Prompty 格式 = YAML Front Matter + Mustache/Jinja2 模板
//  这是微软的 LLM prompt 标准化格式，我们自己解析以避免
//  引入重量级的 Semantic Kernel 依赖。
// ============================================================

/// <summary>
/// Prompty 文件的结构化表示。
/// </summary>
public sealed class PromptyFile
{
    /// <summary>Prompt 名称</summary>
    public string Name { get; init; } = "";
    
    /// <summary>Prompt 描述</summary>
    public string Description { get; init; } = "";
    
    /// <summary>模型配置</summary>
    public PromptyModelConfig Model { get; init; } = new();
    
    /// <summary>输入参数定义</summary>
    public Dictionary<string, PromptyInput> Inputs { get; init; } = new();
    
    /// <summary>System prompt 模板</summary>
    public string? SystemTemplate { get; init; }
    
    /// <summary>User prompt 模板</summary>
    public string? UserTemplate { get; init; }
    
    /// <summary>原始模板（未分离 system/user 的情况）</summary>
    public string RawTemplate { get; init; } = "";
}

/// <summary>
/// 模型配置。
/// </summary>
public sealed class PromptyModelConfig
{
    /// <summary>API 类型: chat, completion</summary>
    public string Api { get; init; } = "chat";
    
    /// <summary>模型参数</summary>
    public PromptyModelParameters Parameters { get; init; } = new();
}

/// <summary>
/// 模型参数。
/// </summary>
public sealed class PromptyModelParameters
{
    public int MaxTokens { get; init; } = 4000;
    public double Temperature { get; init; } = 0.7;
    public double TopP { get; init; } = 1.0;
}

/// <summary>
/// 输入参数定义。
/// </summary>
public sealed class PromptyInput
{
    public string Type { get; init; } = "string";
    public string? Description { get; init; }
    public object? Default { get; init; }
}
