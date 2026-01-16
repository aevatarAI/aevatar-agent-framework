using System.Collections.Concurrent;
using Aevatar.PaperReview.Prompty;
using Scriban;
using Scriban.Runtime;

namespace Aevatar.PaperReview.Services;

// ============================================================
//  REVIEW PROMPT PROVIDER
//  职责：加载和渲染提示词模板
//  
//  混合方案：
//  - LLM Prompt → Prompty 格式（.prompty）
//  - 报告生成 → Scriban 格式（.scriban）
// ============================================================

/// <summary>
/// 评审提示词提供者 - 混合 Prompty/Scriban 方案。
/// </summary>
public sealed class ReviewPromptProvider
{
    private readonly string _promptsDir;
    private readonly ConcurrentDictionary<string, Template> _scribanCache = new();
    private readonly PromptyLoader _promptyLoader;
    private readonly ILogger<ReviewPromptProvider> _logger;

    public ReviewPromptProvider(
        PromptyLoader promptyLoader,
        ILogger<ReviewPromptProvider> logger)
    {
        _promptyLoader = promptyLoader;
        _logger = logger;
        _promptsDir = Path.Combine(AppContext.BaseDirectory, "prompts");
        
        // 回退到源码目录（开发时）
        if (!Directory.Exists(_promptsDir))
        {
            _promptsDir = Path.Combine(Directory.GetCurrentDirectory(), "prompts");
        }
    }

    // ─────────────────────────────────────────────────────────
    //  LLM Prompt - 使用 Prompty
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 渲染评审任务提示词（Prompty 格式）。
    /// </summary>
    public PromptyRenderResult RenderReviewTask(ReviewTaskContext ctx)
    {
        return _promptyLoader.Render("review-task", new Dictionary<string, object>
        {
            ["venue_type"] = ctx.VenueType,
            ["scrutiny_level"] = ctx.ScrutinyLevel,
            ["dim_count"] = ctx.DimensionCount,
            ["focus_areas"] = ctx.FocusAreas,
            ["title"] = ctx.Title,
            ["authors"] = ctx.Authors,
            ["paper_content"] = ctx.PaperContent
        });
    }

    /// <summary>
    /// 渲染评审任务提示词为纯文本（兼容旧 API）。
    /// </summary>
    public string RenderReviewTaskAsText(ReviewTaskContext ctx)
    {
        return _promptyLoader.RenderAsText("review-task", new Dictionary<string, object>
        {
            ["venue_type"] = ctx.VenueType,
            ["scrutiny_level"] = ctx.ScrutinyLevel,
            ["dim_count"] = ctx.DimensionCount,
            ["focus_areas"] = ctx.FocusAreas,
            ["title"] = ctx.Title,
            ["authors"] = ctx.Authors,
            ["paper_content"] = ctx.PaperContent
        });
    }

    /// <summary>
    /// 获取评审任务的模型配置。
    /// </summary>
    public PromptyModelConfig GetReviewTaskModelConfig()
    {
        return _promptyLoader.Load("review-task").Model;
    }

    // ─────────────────────────────────────────────────────────
    //  报告生成 - 继续使用 Scriban
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 渲染评审报告（Scriban 格式）。
    /// </summary>
    public string RenderReport(ReviewReportContext ctx)
    {
        var template = LoadScribanTemplate("review-report.scriban");
        return RenderScriban(template, new ScriptObject
        {
            ["title"] = ctx.Title,
            ["authors"] = ctx.Authors,
            ["venue_type"] = ctx.VenueType,
            ["review_type"] = ctx.ReviewType,
            ["session_id"] = ctx.SessionId,
            ["success"] = ctx.Success,
            ["duration"] = ctx.DurationSeconds,
            ["llm_calls"] = ctx.LlmCalls,
            ["total_tokens"] = ctx.TotalTokens,
            ["content"] = ctx.Content
        });
    }

    // ─────────────────────────────────────────────────────────
    //  Scriban 模板加载与渲染
    // ─────────────────────────────────────────────────────────

    private Template LoadScribanTemplate(string name)
    {
        return _scribanCache.GetOrAdd(name, n =>
        {
            var path = Path.Combine(_promptsDir, n);
            if (!File.Exists(path))
            {
                _logger.LogWarning("Scriban template not found: {Path}", path);
                return Template.Parse($"{{{{ error: 'Template {n} not found' }}}}");
            }

            var content = File.ReadAllText(path);
            var template = Template.Parse(content);
            
            if (template.HasErrors)
            {
                _logger.LogError("Template parse errors in {Name}: {Errors}", 
                    n, string.Join("; ", template.Messages));
            }
            
            return template;
        });
    }

    private static string RenderScriban(Template template, ScriptObject model)
    {
        var ctx = new TemplateContext();
        ctx.PushGlobal(model);
        return template.Render(ctx);
    }
}

// ============================================================
//  上下文模型
// ============================================================

/// <summary>
/// 评审任务上下文。
/// </summary>
public sealed record ReviewTaskContext
{
    public required string VenueType { get; init; }
    public required string ScrutinyLevel { get; init; }
    public required int DimensionCount { get; init; }
    public required string FocusAreas { get; init; }
    public required string Title { get; init; }
    public required string Authors { get; init; }
    public required string PaperContent { get; init; }
}

/// <summary>
/// 评审报告上下文。
/// </summary>
public sealed record ReviewReportContext
{
    public required string Title { get; init; }
    public required string Authors { get; init; }
    public required string VenueType { get; init; }
    public required string ReviewType { get; init; }
    public required string SessionId { get; init; }
    public required bool Success { get; init; }
    public required double DurationSeconds { get; init; }
    public required int LlmCalls { get; init; }
    public required long TotalTokens { get; init; }
    public required string Content { get; init; }
}
