using Aevatar.Agents.Abstractions;
using Aevatar.Agents.CreativeReasoning.Core;
using Aevatar.Agents.CreativeReasoning.Execution;
using Aevatar.Agents.Cognitive.Core;
using Microsoft.Extensions.Logging;
using TUoTSolution = Aevatar.Agents.CreativeReasoning.Messages.TransformativeSolution;
using MeshCandidateSolution = Aevatar.Agents.Cognitive.Core.CandidateSolution;

namespace Aevatar.Agents.Cognitive.Core.Strategies;

// ============================================================
//  T-UOT STRATEGY
//  变革式思维宇宙 - 挑战规则本身
//
//  最深层的创造性推理：
//  1. 暴露规则（包括隐藏假设）
//  2. 突变规则生成新规则集
//  3. 在新规则空间中探索解决方案
//
//  哲学：隐藏假设是看不见的牢笼
// ============================================================

/// <summary>
/// T-UoT 变革式策略适配器。
/// 将 Aevatar.Agents.CreativeReasoning T-UoT 封装为 IReasoningStrategy 接口。
/// </summary>
public sealed class TUoTStrategy : IReasoningStrategy
{
    private readonly IUoTExecutor _executor;
    private readonly ILogger<TUoTStrategy> _logger;

    public TUoTStrategy(IUoTExecutor executor, ILogger<TUoTStrategy> logger)
    {
        _executor = executor;
        _logger = logger;
    }

    /// <inheritdoc />
    public StrategyKind Kind => StrategyKind.UotTransformative;

    /// <inheritdoc />
    public string DisplayName => "UoT Transformative";

    /// <inheritdoc />
    public string Description => "变革式思维宇宙。挑战规则本身，暴露隐藏假设，在突变后的规则空间中探索颠覆性创新。";

    /// <inheritdoc />
    public async Task<ReasoningResult> ExecuteAsync(
        string problem,
        ReasoningOptions options,
        IProgress<ReasoningProgress>? progress = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("T-UoT executing: {Problem}", problem[..Math.Min(50, problem.Length)]);

        // 构建 T-UoT 选项
        var uotOptions = new UoTOptions
        {
            Mode = UoTMode.Transformative,
            ProviderName = options.ProviderName ?? AevatarAgentsConstants.DefaultProviderName,
            DomainHint = options.UotDomainHint,
            FeasibilityThreshold = options.UotFeasibilityThreshold,
            UtilityWeight = options.UotUtilityWeight,
            NoveltyWeight = options.UotNoveltyWeight,
            // T-UoT 特定
            MaxRuleSets = options.TUotMaxRuleSets,
            MutationsPerSet = options.TUotMutationsPerSet,
            MinRadicality = options.TUotMinRadicality,
            AllowPhysicalRuleViolation = options.TUotAllowPhysicalViolation,
            OnProgress = p => progress?.Report(MapProgress(p))
        };

        try
        {
            // 使用专用 T-UoT API 获取完整结果
            var result = await _executor.ExecuteTransformativeAsync(problem, uotOptions, ct);

            return new ReasoningResult
            {
                Success = result.Success,
                Content = result.BestSolution?.Content,
                Error = result.Error,
                Duration = result.Trace.Duration,
                TotalLlmCalls = result.Trace.TotalLLMCalls,
                PromptTokens = 0,
                CompletionTokens = 0,
                UotCandidates = result.AllSolutions.Select(MapTransformativeSolution).ToList(),
                UotBestSolution = result.BestSolution == null ? null : MapTransformativeSolution(result.BestSolution),
                // T-UoT 特有数据
                RulesExposed = result.Trace.RulesExposed,
                HiddenAssumptionsFound = result.Trace.HiddenAssumptionsFound,
                RuleSetsExplored = result.Trace.RuleSetsExplored,
                // 暴露的隐藏假设（关键洞察）
                HiddenAssumptions = result.Trace.HiddenAssumptions.Select(r => r.Content).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "T-UoT execution failed");
            return ReasoningResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc />
    public ValidationResult ValidateOptions(ReasoningOptions options)
    {
        var errors = new List<ValidationError>();

        if (options.TUotMaxRuleSets < 1)
            errors.Add(new ValidationError("TUotMaxRuleSets", "Max rule sets must be at least 1"));

        if (options.TUotMutationsPerSet < 1)
            errors.Add(new ValidationError("TUotMutationsPerSet", "Mutations per set must be at least 1"));

        if (options.TUotMinRadicality < 0 || options.TUotMinRadicality > 1)
            errors.Add(new ValidationError("TUotMinRadicality", "Min radicality must be between 0 and 1"));

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
        // T-UoT 进度：用 analogies 字段表示 rules
        AnalogiesFound = p.AnalogiesFound > 0 ? p.AnalogiesFound : null,
        ThoughtsExtracted = p.ThoughtsExtracted > 0 ? p.ThoughtsExtracted : null,
        CandidatesGenerated = p.CandidatesGenerated > 0 ? p.CandidatesGenerated : null,
        CandidatesPassed = p.CandidatesPassed > 0 ? p.CandidatesPassed : null
    };

    private static MeshCandidateSolution MapTransformativeSolution(TUoTSolution s) => new()
    {
        Id = s.Id,
        Content = s.Content,
        Score = s.Score == null ? null : new SolutionScore
        {
            Feasibility = s.Score.Feasibility,
            Utility = s.Score.Utility,
            Novelty = s.Score.Novelty,
            Composite = s.Score.Composite
        },
        // T-UoT 特有：违反的惯例
        ViolatedConventions = s.ViolatedConventions.Select(r => r.Content).ToList(),
        RuleSetId = s.RuleSet?.Id
    };
}

