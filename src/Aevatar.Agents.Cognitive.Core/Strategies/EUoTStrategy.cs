using Aevatar.Agents.Abstractions;
using Aevatar.Agents.CreativeReasoning.Core;
using Aevatar.Agents.CreativeReasoning.Execution;
using Aevatar.Agents.Cognitive.Core;
using Microsoft.Extensions.Logging;
using UoTCandidateSolution = Aevatar.Agents.CreativeReasoning.Messages.CandidateSolution;
using MeshCandidateSolution = Aevatar.Agents.Cognitive.Core.CandidateSolution;

namespace Aevatar.Agents.Cognitive.Core.Strategies;

// ============================================================
//  E-UOT STRATEGY
//  探索式思维宇宙 - 发现域外思想
//
//  在 C-UoT 基础上添加 Step E: Exploratory Idea Expansion
//  不仅重组已有思想，还主动探索未知领域发现新概念
// ============================================================

/// <summary>
/// E-UoT 探索式策略适配器。
/// 将 Aevatar.Agents.CreativeReasoning E-UoT 封装为 IReasoningStrategy 接口。
/// </summary>
public sealed class EUoTStrategy : IReasoningStrategy
{
    private readonly IUoTExecutor _executor;
    private readonly ILogger<EUoTStrategy> _logger;

    public EUoTStrategy(IUoTExecutor executor, ILogger<EUoTStrategy> logger)
    {
        _executor = executor;
        _logger = logger;
    }

    /// <inheritdoc />
    public StrategyKind Kind => StrategyKind.UotExploratory;

    /// <inheritdoc />
    public string DisplayName => "UoT Exploratory";

    /// <inheritdoc />
    public string Description => "探索式思维宇宙。在 C-UoT 基础上发现域外思想，从未探索的领域引入新概念原语。";

    /// <inheritdoc />
    public async Task<ReasoningResult> ExecuteAsync(
        string problem,
        ReasoningOptions options,
        IProgress<ReasoningProgress>? progress = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("E-UoT executing: {Problem}", problem[..Math.Min(50, problem.Length)]);

        // 构建 E-UoT 选项
        var uotOptions = new UoTOptions
        {
            Mode = UoTMode.Exploratory,
            ProviderName = options.ProviderName ?? AevatarAgentsConstants.DefaultProviderName,
            DomainHint = options.UotDomainHint,
            MaxAnalogies = options.UotMaxAnalogies,
            MaxCandidates = options.UotMaxCandidates,
            FeasibilityThreshold = options.UotFeasibilityThreshold,
            UtilityWeight = options.UotUtilityWeight,
            NoveltyWeight = options.UotNoveltyWeight,
            // E-UoT 特定
            MaxOutsideThoughts = options.EUotMaxOutsideThoughts,
            ExplorationDirections = options.EUotExplorationDirections,
            OutsideThoughtRelevance = options.EUotOutsideThoughtRelevance,
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
                PromptTokens = 0,
                CompletionTokens = 0,
                UotCandidates = result.AllCandidates.Select(MapCandidate).ToList(),
                UotBestSolution = result.BestSolution == null ? null : MapCandidate(result.BestSolution),
                // E-UoT 特有数据
                OutsideThoughtsDiscovered = result.Trace.ThoughtsExtracted - result.Trace.AnalogiesExplored * 3  // 估算
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "E-UoT execution failed");
            return ReasoningResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc />
    public ValidationResult ValidateOptions(ReasoningOptions options)
    {
        var errors = new List<ValidationError>();

        if (options.UotMaxAnalogies < 1)
            errors.Add(new ValidationError("UotMaxAnalogies", "Max analogies must be at least 1"));

        if (options.EUotMaxOutsideThoughts < 1)
            errors.Add(new ValidationError("EUotMaxOutsideThoughts", "Max outside thoughts must be at least 1"));

        if (options.EUotExplorationDirections < 1)
            errors.Add(new ValidationError("EUotExplorationDirections", "Exploration directions must be at least 1"));

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
        SourceAnalogies = null
    };
}

