using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Maker;
using Aevatar.Agents.Cognitive.Core;
using Microsoft.Extensions.Logging;
using MeshMakerTrace = Aevatar.Agents.Cognitive.Core.MakerTrace;
using MeshTaskNode = Aevatar.Agents.Cognitive.Core.TaskNode;
using MeshVotingSession = Aevatar.Agents.Cognitive.Core.VotingSession;
using MeshVotingCandidate = Aevatar.Agents.Cognitive.Core.VotingCandidate;
using MeshVotingProgress = Aevatar.Agents.Cognitive.Core.VotingProgress;
using MeshStreamingTokenProgress = Aevatar.Agents.Cognitive.Core.StreamingTokenProgress;

namespace Aevatar.Agents.Cognitive.Core.Strategies;

// ============================================================
//  MAKER STRATEGY
//  MAKER 策略适配器 - 分解-共识-合成
// ============================================================

/// <summary>
/// MAKER 策略适配器。
/// 将 Aevatar.Agents.Maker 封装为 IReasoningStrategy 接口。
/// </summary>
public sealed class MakerStrategy : IReasoningStrategy
{
    private readonly IMakerExecutor _executor;
    private readonly ILogger<MakerStrategy> _logger;

    public MakerStrategy(IMakerExecutor executor, ILogger<MakerStrategy> logger)
    {
        _executor = executor;
        _logger = logger;
    }

    /// <inheritdoc />
    public StrategyKind Kind => StrategyKind.Maker;

    /// <inheritdoc />
    public string DisplayName => "MAKER Consensus";

    /// <inheritdoc />
    public string Description => "多 Agent 协作，通过投票达成共识。任务分解→并行求解→共识投票→结果合成。";

    /// <inheritdoc />
    public async Task<ReasoningResult> ExecuteAsync(
        string problem,
        ReasoningOptions options,
        IProgress<ReasoningProgress>? progress = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("MAKER executing: {Problem}", problem[..Math.Min(50, problem.Length)]);

        // 构建 MAKER 选项
        var makerOptions = new MakerOptions
        {
            ProviderName = options.ProviderName ?? AevatarAgentsConstants.DefaultProviderName,
            Reliability = options.MakerReliability switch
            {
                MakerReliability.Low => ReliabilityLevel.Low,
                MakerReliability.Medium => ReliabilityLevel.Medium,
                MakerReliability.High => ReliabilityLevel.High,
                MakerReliability.Critical => ReliabilityLevel.Critical,
                _ => ReliabilityLevel.Medium
            },
            CustomK = options.MakerConsensusK > 0 ? options.MakerConsensusK : null,
            UseMultipleProviders = options.MakerUseMultipleProviders,
            Mode = options.MakerExecutionMode == MakerExecutionMode.Academic
                ? ExecutionMode.Academic
                : ExecutionMode.Production,
            MaxTotalLlmCalls = options.MaxLlmCalls,
            MaxTotalTokens = options.MaxTokens,
            MaxDuration = options.MaxDuration,
            StepTimeout = options.StepTimeout,
            Context = options.Context,
            OnProgress = p => progress?.Report(MapProgress(p))
        };

        try
        {
            var result = await _executor.ExecuteAsync(problem, makerOptions, ct);

            return new ReasoningResult
            {
                Success = result.Success,
                Content = result.Content,
                Error = result.Error,
                Duration = result.Duration,
                TotalLlmCalls = result.TotalLLMCalls,
                PromptTokens = result.PromptTokens,
                CompletionTokens = result.CompletionTokens,
                MakerTrace = MapTrace(result.Trace)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MAKER execution failed");
            return ReasoningResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc />
    public ValidationResult ValidateOptions(ReasoningOptions options)
    {
        var errors = new List<ValidationError>();

        if (options.MakerConsensusK < 1)
            errors.Add(new ValidationError("MakerConsensusK", "Consensus K must be at least 1"));

        if (options.MaxLlmCalls < 10)
            errors.Add(new ValidationError("MaxLlmCalls", "Max LLM calls must be at least 10"));

        return errors.Count > 0
            ? ValidationResult.Failed(errors.ToArray())
            : ValidationResult.Success();
    }

    private static ReasoningProgress MapProgress(MakerProgress p) => new()
    {
        Phase = p.Phase.ToString(),
        ProgressPercent = 0, // MAKER 没有精确进度
        Message = p.Message,
        TaskId = p.TaskId,
        Timestamp = p.Timestamp,
        Depth = p.Depth,
        Voting = p.Voting == null ? null : new MeshVotingProgress
        {
            Type = p.Voting.Type.ToString(),
            Round = p.Voting.Round,
            TotalVotes = p.Voting.TotalVotes,
            VotesNeeded = p.Voting.VotesNeeded,
            LeaderVotes = p.Voting.LeaderVotes,
            RunnerUpVotes = p.Voting.RunnerUpVotes,
            ClusterCount = p.Voting.ClusterCount,
            UsedSemanticClustering = p.Voting.UsedSemanticClustering
        },
        Proposal = p.Proposal == null ? null : new ProposalProgress
        {
            ProposalId = p.Proposal.ProposalId,
            Content = p.Proposal.Content,
            Success = p.Proposal.Success,
            Error = p.Proposal.Error,
            PromptTokens = p.Proposal.PromptTokens,
            CompletionTokens = p.Proposal.CompletionTokens,
            ProviderName = p.Proposal.ProviderName
        },
        StreamingToken = p.StreamingToken == null ? null : new MeshStreamingTokenProgress
        {
            WorkerId = p.StreamingToken.WorkerId,
            ProposalId = p.StreamingToken.ProposalId,
            Token = p.StreamingToken.Token,
            AccumulatedContent = p.StreamingToken.AccumulatedContent,
            TokenIndex = p.StreamingToken.TokenIndex,
            IsFirstToken = p.StreamingToken.IsFirstToken,
            IsLastToken = p.StreamingToken.IsLastToken,
            SystemPrompt = p.StreamingToken.SystemPrompt,
            UserPrompt = p.StreamingToken.UserPrompt,
            ProviderName = p.StreamingToken.ProviderName
        }
    };

    private static MeshMakerTrace? MapTrace(Aevatar.Agents.Maker.MakerTrace? trace)
    {
        if (trace?.RootTask == null) return null;

        return new MeshMakerTrace
        {
            RootTask = MapTaskNode(trace.RootTask),
            MaxDepthReached = GetMaxDepth(trace.RootTask),
            TotalTasks = CountTasks(trace.RootTask),
            AtomicTasks = CountAtomicTasks(trace.RootTask)
        };
    }

    private static MeshTaskNode MapTaskNode(Aevatar.Agents.Maker.TaskNode node) => new()
    {
        TaskId = node.TaskId,
        Description = node.Description,
        IsAtomic = node.IsAtomic,
        Result = node.Result,
        Children = node.Children.Select(MapTaskNode).ToList(),
        VotingSessions = node.VotingSessions.Select(s => new MeshVotingSession
        {
            Type = s.Type.ToString(),
            Rounds = s.Rounds,
            Winner = s.Winner == null ? null : new MeshVotingCandidate
            {
                Hash = s.Winner.Hash,
                Votes = s.Winner.Votes,
                Content = s.Winner.Content
            },
            Candidates = s.Candidates.Select(c => new MeshVotingCandidate
            {
                Hash = c.Hash,
                Votes = c.Votes,
                Content = c.Content
            }).ToList()
        }).ToList()
    };

    private static int GetMaxDepth(Aevatar.Agents.Maker.TaskNode node, int current = 0) =>
        node.Children.Count == 0
            ? current
            : node.Children.Max(c => GetMaxDepth(c, current + 1));

    private static int CountTasks(Aevatar.Agents.Maker.TaskNode node) =>
        1 + node.Children.Sum(CountTasks);

    private static int CountAtomicTasks(Aevatar.Agents.Maker.TaskNode node) =>
        (node.IsAtomic ? 1 : 0) + node.Children.Sum(CountAtomicTasks);
}
