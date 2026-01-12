using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Learning.Context;
using Aevatar.Learning.Notebooks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Learning.Reports;

// ============================================================
//  LearningReportService (MVP)
//
//  目标：
//  - 基于 notebook sources 构建 context
//  - 调用可配置 LLMProviders 生成结构化报告
//  - 版本化写入 notebook 目录：reports/{reportId}/v{n}.md + meta.json
// ============================================================
public sealed class LearningReportService
{
    private const int MaxReportChars = 200_000; // hard cap for safety (best-effort)
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly LearningContextBuilder _contextBuilder;
    private readonly ILLMProviderFactory _providers;
    private readonly IOptions<LLMProvidersConfig> _config;
    private readonly ILogger<LearningReportService> _logger;

    public LearningReportService(
        LearningContextBuilder contextBuilder,
        ILLMProviderFactory providers,
        IOptions<LLMProvidersConfig> config,
        ILogger<LearningReportService> logger)
    {
        _contextBuilder = contextBuilder ?? throw new ArgumentNullException(nameof(contextBuilder));
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<LearningReportResult> GenerateAsync(
        NotebookWorkspace workspace,
        string topic,
        string? providerName = null,
        IReadOnlyList<string>? selectedSourceIds = null,
        LearningContextBudget? budget = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        topic = (topic ?? string.Empty).Trim();
        if (topic.Length == 0)
            throw new ArgumentException("topic is required.", nameof(topic));

        workspace.EnsureDirectories();
        Directory.CreateDirectory(workspace.ReportsDir);

        var ctx = await _contextBuilder.BuildAsync(
            workspace,
            query: topic,
            selectedSourceIds: selectedSourceIds,
            budget: budget,
            ct: ct);

        var effectiveProvider = ResolveProviderName(providerName);
        var llm = _providers.GetProvider(effectiveProvider);

        var req = new AevatarLLMRequest
        {
            SystemPrompt = BuildSystemPrompt(ctx.Text),
            UserPrompt = $"Generate a learning report about: {topic}",
            Messages = new List<Aevatar.Agents.AI.AevatarChatMessage>(),
            Settings = new AevatarLLMSettings()
        };

        _logger.LogInformation(
            "[LearningReport] Generate: provider={Provider}, notebook={NotebookId}, topic={Topic}, sources={Sources}",
            effectiveProvider,
            workspace.NotebookId,
            topic,
            ctx.Slices.Count);

        var resp = await llm.GenerateAsync(req, ct);
        var content = (resp.Content ?? string.Empty).Trim();
        if (content.Length == 0)
            content = "(empty report)";
        if (content.Length > MaxReportChars)
            content = content[..MaxReportChars] + "\n\n...(truncated)";

        var reportId = BuildReportId(topic);
        var reportDir = Path.Combine(workspace.ReportsDir, reportId);
        Directory.CreateDirectory(reportDir);

        var metaPath = Path.Combine(reportDir, "meta.json");
        var meta = await TryReadMetaAsync(metaPath, ct) ?? new LearningReportMeta { ReportId = reportId, Topic = topic };

        var nextVersion = meta.CurrentVersion <= 0 ? 1 : meta.CurrentVersion + 1;
        var now = DateTimeOffset.UtcNow;

        meta.ReportId = reportId;
        meta.Topic = topic;
        meta.CurrentVersion = nextVersion;
        meta.UpdatedAt = now;
        meta.CreatedAt = meta.CreatedAt == default ? now : meta.CreatedAt;
        meta.SourceIds = ctx.Slices.Select(s => s.SourceId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var fileName = $"v{nextVersion}.md";
        var filePath = Path.Combine(reportDir, fileName);

        await File.WriteAllTextAsync(filePath, content, new UTF8Encoding(false), ct);
        await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(meta, Json), new UTF8Encoding(false), ct);

        return new LearningReportResult(meta, content, filePath);
    }

    private string ResolveProviderName(string? overrideName)
    {
        var name = (overrideName ?? string.Empty).Trim();
        if (name.Length > 0)
            return name;

        name = (_config.Value.Default ?? string.Empty).Trim();
        if (name.Length == 0)
            throw new InvalidOperationException("LLMProviders:Default is not configured.");

        return name;
    }

    private static string BuildSystemPrompt(string contextText)
    {
        var basePrompt =
            """
            You are an assistant generating a structured learning report.

            Rules:
            - Ground the report in the provided "Notebook context" when present.
            - If context is insufficient, explicitly list missing info and provide a best-effort outline.
            - Use clear headings and bullet points.
            - When citing facts, annotate with source ids like: [SOURCE <id>].
            """;

        contextText = (contextText ?? string.Empty).Trim();
        if (contextText.Length == 0)
            return basePrompt;

        return $"{basePrompt}\n\nNotebook context:\n{contextText}\n";
    }

    private static string BuildReportId(string topic)
    {
        var slug = Slugify(topic);
        var sha8 = Sha256Hex(topic)[..8];
        return $"{slug}-{sha8}";
    }

    private static string Sha256Hex(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Slugify(string input)
    {
        input = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (input.Length == 0) return "report";

        var sb = new StringBuilder(capacity: Math.Min(48, input.Length));
        var lastDash = false;

        foreach (var ch in input)
        {
            var isAlnum = (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9');
            if (isAlnum)
            {
                sb.Append(ch);
                lastDash = false;
                continue;
            }

            if (char.IsWhiteSpace(ch) || ch == '_' || ch == '-')
            {
                if (!lastDash && sb.Length > 0)
                {
                    sb.Append('-');
                    lastDash = true;
                }
            }
        }

        var slug = sb.ToString().Trim('-');
        if (slug.Length == 0) slug = "report";
        if (slug.Length > 32) slug = slug[..32].Trim('-');
        return slug.Length == 0 ? "report" : slug;
    }

    private static async Task<LearningReportMeta?> TryReadMetaAsync(string metaPath, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(metaPath))
                return null;

            var json = await File.ReadAllTextAsync(metaPath, ct);
            return JsonSerializer.Deserialize<LearningReportMeta>(json, Json);
        }
        catch
        {
            return null;
        }
    }
}

public sealed class LearningReportMeta
{
    public string ReportId { get; set; } = "";
    public string Topic { get; set; } = "";
    public int CurrentVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<string> SourceIds { get; set; } = new();
}

public sealed record LearningReportResult(
    LearningReportMeta Meta,
    string Content,
    string FilePath);


