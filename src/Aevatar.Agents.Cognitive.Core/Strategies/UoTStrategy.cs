using Aevatar.Agents.Abstractions;
using Aevatar.Agents.CreativeReasoning.Core;
using Aevatar.Agents.CreativeReasoning.Execution;
using Aevatar.Agents.Cognitive.Core;
using Microsoft.Extensions.Logging;
using UoTCandidateSolution = Aevatar.Agents.CreativeReasoning.Messages.CandidateSolution;
using MeshCandidateSolution = Aevatar.Agents.Cognitive.Core.CandidateSolution;

namespace Aevatar.Agents.Cognitive.Core.Strategies;

// ============================================================
//  UOT STRATEGY
//  UoT 组合式策略适配器 - 类比检索 + 思维合成
// ============================================================

/// <summary>
/// UoT 组合式策略适配器。
/// 将 Aevatar.Agents.CreativeReasoning 封装为 IReasoningStrategy 接口。
/// </summary>
public sealed class UoTStrategy : IReasoningStrategy
{
    private readonly IUoTExecutor _executor;
    private readonly ILogger<UoTStrategy> _logger;

    public UoTStrategy(IUoTExecutor executor, ILogger<UoTStrategy> logger)
    {
        _executor = executor;
        _logger = logger;
    }

    /// <inheritdoc />
    public StrategyKind Kind => StrategyKind.UotCombinational;

    /// <inheritdoc />
    public string DisplayName => "UoT Combinational";

    /// <inheritdoc />
    public string Description => "类比检索 + 思维合成，创意问题求解。从相似领域提取思维单元，重组生成创新方案。";

    /// <inheritdoc />
    public async Task<ReasoningResult> ExecuteAsync(
        string problem,
        ReasoningOptions options,
        IProgress<ReasoningProgress>? progress = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("UoT executing: {Problem}", problem[..Math.Min(50, problem.Length)]);

        // 构建 UoT 选项
        var uotOptions = new UoTOptions
        {
            ProviderName = options.ProviderName ?? AevatarAgentsConstants.DefaultProviderName,
            DomainHint = options.UotDomainHint,
            MaxAnalogies = options.UotMaxAnalogies,
            MaxCandidates = options.UotMaxCandidates,
            FeasibilityThreshold = options.UotFeasibilityThreshold,
            UtilityWeight = options.UotUtilityWeight,
            NoveltyWeight = options.UotNoveltyWeight,
            OnProgress = p => progress?.Report(MapProgress(p))
        };

        try
        {
            var result = await _executor.ExecuteAsync(problem, uotOptions, ct);

            return new ReasoningResult
            {
                Success = result.Success,
                Content = result.BestSolution?.Content,
                Error = result.Error,
                Duration = result.Trace.Duration,
                TotalLlmCalls = result.Trace.TotalLLMCalls,
                PromptTokens = 0, // UoT 不分开统计
                CompletionTokens = 0,
                UotCandidates = result.AllCandidates.Select(MapCandidate).ToList(),
                UotBestSolution = result.BestSolution == null ? null : MapCandidate(result.BestSolution)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UoT execution failed");
            return ReasoningResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc />
    public ValidationResult ValidateOptions(ReasoningOptions options)
    {
        var errors = new List<ValidationError>();

        if (options.UotMaxAnalogies < 1)
            errors.Add(new ValidationError("UotMaxAnalogies", "Max analogies must be at least 1"));

        if (options.UotMaxCandidates < 1)
            errors.Add(new ValidationError("UotMaxCandidates", "Max candidates must be at least 1"));

        if (options.UotFeasibilityThreshold < 0 || options.UotFeasibilityThreshold > 1)
            errors.Add(new ValidationError("UotFeasibilityThreshold", "Feasibility threshold must be between 0 and 1"));

        if (Math.Abs(options.UotUtilityWeight + options.UotNoveltyWeight - 1.0f) > 0.01f)
        {
            // 警告而非错误
            return ValidationResult.WithWarnings(
                new ValidationWarning("Weights", "Utility + Novelty weights should sum to 1.0"));
        }

        return errors.Count > 0
            ? ValidationResult.Failed(errors.ToArray())
            : ValidationResult.Success();
    }

    private static ReasoningProgress MapProgress(UoTProgress p) => new()
    {
        Phase = p.Phase.ToString(),
        ProgressPercent = p.ProgressPercent,
        Message = p.Message,
        TaskId = null,
        Timestamp = DateTimeOffset.UtcNow,
        AnalogiesFound = p.AnalogiesFound > 0 ? p.AnalogiesFound : null,
        ThoughtsExtracted = p.ThoughtsExtracted > 0 ? p.ThoughtsExtracted : null,
        CandidatesGenerated = p.CandidatesGenerated > 0 ? p.CandidatesGenerated : null,
        CandidatesPassed = p.CandidatesPassed > 0 ? p.CandidatesPassed : null
    };

    private static MeshCandidateSolution MapCandidate(UoTCandidateSolution c) => new()
    {
        Id = c.Id,
        Content = c.Content,
        Score = c.Score == null ? null : new SolutionScore
        {
            Feasibility = c.Score.Feasibility,
            Utility = c.Score.Utility,
            Novelty = c.Score.Novelty,
            Composite = c.Score.Composite
        },
        SourceAnalogies = null // UoT 不直接暴露这个字段
    };
}
