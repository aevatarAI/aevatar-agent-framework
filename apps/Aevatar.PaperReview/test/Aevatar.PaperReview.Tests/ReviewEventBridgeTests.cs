using System.Threading.Channels;
using Aevatar.Agents.Cognitive.Core;
using Aevatar.PaperReview.Models;
using Aevatar.PaperReview.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

// 解决命名冲突
using ProgressEvent = Aevatar.PaperReview.Models.ProgressEvent;

namespace Aevatar.PaperReview.Tests;

// ============================================================
//  REVIEW EVENT BRIDGE TESTS
//  验证 ReasoningProgress → UI 事件转换的正确性
// ============================================================

public class ReviewEventBridgeTests
{
    private readonly ReviewEventBridge _bridge;
    private readonly ReviewSession _session;

    public ReviewEventBridgeTests()
    {
        var logger = NullLoggerFactory.Instance.CreateLogger<ReviewEventBridge>();
        _bridge = new ReviewEventBridge(logger);
        _session = new ReviewSession
        {
            Title = "Test Paper",
            Authors = "Test Author"
        };
    }

    // ─────────────────────────────────────────────────────────
    //  基础进度事件测试
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleProgress_BasicProgress_EmitsProgressEvent()
    {
        var progress = new ReasoningProgress
        {
            Phase = "ANALYZE",
            Message = "Analyzing task complexity"
        };
        
        _bridge.HandleProgress(_session, progress);
        
        var evt = await ReadEventAsync<ProgressEvent>();
        evt.ShouldNotBeNull();
        evt.SessionId.ShouldBe(_session.Id);
        evt.Message.ShouldBe("Analyzing task complexity");
    }

    [Fact]
    public async Task HandleProgress_WithDepth_IncludesDepthInEvent()
    {
        var progress = new ReasoningProgress
        {
            Phase = "SOLVE",
            Depth = 3,
            Message = "Solving at depth 3"
        };
        
        _bridge.HandleProgress(_session, progress);
        
        var evt = await ReadEventAsync<ProgressEvent>();
        evt.ShouldNotBeNull();
        evt.Depth.ShouldBe(3);
    }

    // ─────────────────────────────────────────────────────────
    //  Phase 变更测试
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleProgress_PhaseChange_EmitsPhaseChangeEvent()
    {
        // 第一个阶段
        _bridge.HandleProgress(_session, new ReasoningProgress
        {
            Phase = "START"
        });
        
        // 阶段变更
        _bridge.HandleProgress(_session, new ReasoningProgress
        {
            Phase = "ANALYZE"
        });
        
        // 读取事件
        var events = await ReadAllEventsAsync(timeout: 100);
        var phaseChange = events.OfType<PhaseChangeEvent>().LastOrDefault();
        
        phaseChange.ShouldNotBeNull();
        phaseChange.OldPhase.ShouldBe("Starting");
        phaseChange.NewPhase.ShouldBe("Assessing");
    }

    // ─────────────────────────────────────────────────────────
    //  Stage 日志测试
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleProgress_StageStart_EmitsStageLogEvent()
    {
        var progress = new ReasoningProgress
        {
            Phase = "DECOMPOSE",
            Message = "Starting decomposition"
        };
        
        _bridge.HandleProgress(_session, progress);
        
        var events = await ReadAllEventsAsync(timeout: 100);
        var stageLog = events.OfType<StageLogEvent>().FirstOrDefault(e => e.Status == "started");
        
        stageLog.ShouldNotBeNull();
        stageLog.Stage.ShouldBe("Decomposing");
    }

    // ─────────────────────────────────────────────────────────
    //  Streaming Token 测试
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleProgress_StreamingToken_EmitsLlmStreamingEvent()
    {
        var progress = new ReasoningProgress
        {
            Phase = "SOLVE",
            TaskId = "gen[1]",
            StreamingToken = new StreamingTokenProgress
            {
                WorkerId = "gen[1]",
                ProposalId = "prop-1",
                Token = "Hello",
                AccumulatedContent = "Hello",
                TokenIndex = 1,
                IsFirstToken = true,
                IsLastToken = false
            }
        };
        
        _bridge.HandleProgress(_session, progress);
        
        var events = await ReadAllEventsAsync(timeout: 100);
        var streamingEvt = events.OfType<LlmStreamingEvent>().FirstOrDefault();
        
        streamingEvt.ShouldNotBeNull();
        streamingEvt.Token.ShouldBe("Hello");
        streamingEvt.IsFirstToken.ShouldBeTrue();
        streamingEvt.IsLastToken.ShouldBeFalse();
    }

    [Fact]
    public async Task HandleProgress_StreamingToken_First_EmitsWorkerStartAndLlmStart()
    {
        var progress = new ReasoningProgress
        {
            Phase = "SOLVE",
            TaskId = "gen[1]",
            StreamingToken = new StreamingTokenProgress
            {
                WorkerId = "gen[1]",
                ProposalId = "prop-1",
                Token = "First",
                IsFirstToken = true,
                IsLastToken = false,
                ProviderName = "deepseek"
            }
        };
        
        _bridge.HandleProgress(_session, progress);
        
        var events = await ReadAllEventsAsync(timeout: 100);
        
        // 应该有 WorkerStartedEvent
        var workerStart = events.OfType<WorkerStartedEvent>().FirstOrDefault();
        workerStart.ShouldNotBeNull();
        workerStart.WorkerId.ShouldStartWith("worker-");
        workerStart.ProviderName.ShouldBe("deepseek");
        
        // 应该有 LlmCallStartEvent
        var llmStart = events.OfType<LlmCallStartEvent>().FirstOrDefault();
        llmStart.ShouldNotBeNull();
        llmStart.ProviderName.ShouldBe("deepseek");
    }

    [Fact]
    public async Task HandleProgress_StreamingToken_Last_EmitsLlmCallComplete()
    {
        // 先发送第一个 token
        _bridge.HandleProgress(_session, new ReasoningProgress
        {
            Phase = "SOLVE",
            TaskId = "gen[1]",
            StreamingToken = new StreamingTokenProgress
            {
                WorkerId = "gen[1]",
                ProposalId = "prop-1",
                Token = "Hello",
                AccumulatedContent = "Hello",
                IsFirstToken = true,
                IsLastToken = false
            }
        });
        
        // 清空事件
        await ReadAllEventsAsync(timeout: 100);
        
        // 发送最后一个 token
        _bridge.HandleProgress(_session, new ReasoningProgress
        {
            Phase = "SOLVE",
            TaskId = "gen[1]",
            StreamingToken = new StreamingTokenProgress
            {
                WorkerId = "gen[1]",
                ProposalId = "prop-1",
                Token = " World",
                AccumulatedContent = "Hello World",
                IsFirstToken = false,
                IsLastToken = true
            }
        });
        
        var events = await ReadAllEventsAsync(timeout: 100);
        var llmComplete = events.OfType<LlmCallCompleteEvent>().FirstOrDefault();
        
        llmComplete.ShouldNotBeNull();
        llmComplete.Success.ShouldBeTrue();
        llmComplete.Content.ShouldBe("Hello World");
    }

    // ─────────────────────────────────────────────────────────
    //  Vote 步骤测试
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleProgress_VoteStep_EmitsVotingRoundEvent()
    {
        var progress = new ReasoningProgress
        {
            Phase = "VOTE",
            StepType = "vote",
            StepStatus = "Running",
            VoteRound = 1,
            VoteK = 2,
            VoteCurrentVotes = 1
        };
        
        _bridge.HandleProgress(_session, progress);
        
        var events = await ReadAllEventsAsync(timeout: 100);
        var votingEvt = events.OfType<VotingRoundEvent>().FirstOrDefault();
        
        votingEvt.ShouldNotBeNull();
        votingEvt.Round.ShouldBe(1);
        votingEvt.VotesNeeded.ShouldBe(2);
    }

    [Fact]
    public async Task HandleProgress_VoteConsensus_EmitsConsensusEvent()
    {
        var progress = new ReasoningProgress
        {
            Phase = "VOTE",
            StepType = "vote",
            StepStatus = "Completed",
            VoteRound = 2,
            VoteK = 2,
            VoteCurrentVotes = 2
        };
        
        _bridge.HandleProgress(_session, progress);
        
        var events = await ReadAllEventsAsync(timeout: 100);
        var consensus = events.OfType<ConsensusEvent>().FirstOrDefault();
        
        consensus.ShouldNotBeNull();
        consensus.Round.ShouldBe(2);
        consensus.LeaderVotes.ShouldBe(2);
    }

    // ─────────────────────────────────────────────────────────
    //  Proposal 完成测试
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleProgress_ProposalComplete_EmitsWorkerEvents()
    {
        var progress = new ReasoningProgress
        {
            Phase = "SOLVE",
            TaskId = "gen[1]",
            Proposal = new ProposalProgress
            {
                ProposalId = "prop-1",
                Content = "This is the proposal content",
                Success = true,
                PromptTokens = 100,
                CompletionTokens = 200,
                ProviderName = "deepseek"
            }
        };
        
        _bridge.HandleProgress(_session, progress);
        
        var events = await ReadAllEventsAsync(timeout: 100);
        
        // 应该有 WorkerCompletedEvent
        var workerComplete = events.OfType<WorkerCompletedEvent>().FirstOrDefault();
        workerComplete.ShouldNotBeNull();
        workerComplete.Success.ShouldBeTrue();
        workerComplete.TotalTokens.ShouldBe(300);
        
        // 应该有 LlmCallCompleteEvent
        var llmComplete = events.OfType<LlmCallCompleteEvent>().FirstOrDefault();
        llmComplete.ShouldNotBeNull();
        llmComplete.PromptTokens.ShouldBe(100);
        llmComplete.CompletionTokens.ShouldBe(200);
    }

    // ─────────────────────────────────────────────────────────
    //  VotingProgress 对象测试
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleProgress_VotingProgress_EmitsVotingRoundEvent()
    {
        var progress = new ReasoningProgress
        {
            Phase = "Voting round 1",
            Voting = new VotingProgress
            {
                Type = "Solution",
                Round = 1,
                TotalVotes = 5,
                VotesNeeded = 2,
                LeaderVotes = 2,
                RunnerUpVotes = 1
            }
        };
        
        _bridge.HandleProgress(_session, progress);
        
        var events = await ReadAllEventsAsync(timeout: 100);
        var votingEvt = events.OfType<VotingRoundEvent>().FirstOrDefault();
        
        votingEvt.ShouldNotBeNull();
        votingEvt.VotingType.ShouldBe("Solution");
        votingEvt.Round.ShouldBe(1);
        votingEvt.VotesNeeded.ShouldBe(2);
        votingEvt.ConsensusReached.ShouldBeTrue();
        votingEvt.Candidates.Count.ShouldBeGreaterThan(0);
    }

    // ─────────────────────────────────────────────────────────
    //  并行任务进度测试
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleProgress_ParallelProgress_IncludesParallelStats()
    {
        var progress = new ReasoningProgress
        {
            Phase = "EXECUTE",
            StepType = "fan_out",
            ParallelTotal = 5,
            ParallelCompleted = 3,
            ParallelFailed = 1
        };
        
        _bridge.HandleProgress(_session, progress);
        
        var evt = await ReadEventAsync<ProgressEvent>();
        evt.ShouldNotBeNull();
        evt.ParallelTotal.ShouldBe(5);
        evt.ParallelCompleted.ShouldBe(3);
        evt.ParallelFailed.ShouldBe(1);
    }

    // ─────────────────────────────────────────────────────────
    //  Timeline 更新测试
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void HandleProgress_UpdatesSessionTimeline()
    {
        var progress = new ReasoningProgress
        {
            Phase = "ANALYZE",
            Message = "Starting analysis"
        };
        
        _bridge.HandleProgress(_session, progress);
        
        _session.Timeline.ShouldNotBeEmpty();
        _session.Timeline.Last().Message.ShouldBe("Starting analysis");
    }

    [Fact]
    public void HandleProgress_UpdatesCurrentPhase()
    {
        _bridge.HandleProgress(_session, new ReasoningProgress { Phase = "START" });
        _session.CurrentPhase.ShouldBe("Starting");
        
        _bridge.HandleProgress(_session, new ReasoningProgress { Phase = "ANALYZE" });
        _session.CurrentPhase.ShouldBe("Assessing");
        
        _bridge.HandleProgress(_session, new ReasoningProgress { Phase = "DECOMPOSE" });
        _session.CurrentPhase.ShouldBe("Decomposing");
    }

    // ─────────────────────────────────────────────────────────
    //  Worker ID 追踪测试
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleProgress_TracksKValue_FromParallelTotal()
    {
        // 设置 N 值 (Worker 数量)，K 会被计算为 (N+1)/2
        // N=5 → K=(5+1)/2=3
        _bridge.HandleProgress(_session, new ReasoningProgress
        {
            Phase = "EXECUTE",
            ParallelTotal = 5
        });
        
        // Worker ID 规范化应使用 N（Worker 数量）进行循环映射
        _bridge.HandleProgress(_session, new ReasoningProgress
        {
            Phase = "SOLVE",
            TaskId = "gen[6]",  // 第 6 个任务，N=5 时: (6-1) % 5 = 0 → worker-0
            StreamingToken = new StreamingTokenProgress
            {
                WorkerId = "gen[6]",
                ProposalId = "prop-6",
                IsFirstToken = true,
                IsLastToken = false
            }
        });
        
        var events = await ReadAllEventsAsync(timeout: 100);
        var workerStart = events.OfType<WorkerStartedEvent>().LastOrDefault();
        
        workerStart.ShouldNotBeNull();
        workerStart.WorkerId.ShouldBe("worker-0");  // (6-1) % 5 = 0
    }

    // ─────────────────────────────────────────────────────────
    //  辅助方法
    // ─────────────────────────────────────────────────────────

    private async Task<T?> ReadEventAsync<T>(int timeoutMs = 100) where T : ReviewEvent
    {
        var events = await ReadAllEventsAsync(timeoutMs);
        return events.OfType<T>().FirstOrDefault();
    }

    private async Task<List<ReviewEvent>> ReadAllEventsAsync(int timeout = 100)
    {
        var events = new List<ReviewEvent>();
        using var cts = new CancellationTokenSource(timeout);
        
        try
        {
            while (await _session.EventChannel.Reader.WaitToReadAsync(cts.Token))
            {
                while (_session.EventChannel.Reader.TryRead(out var evt))
                {
                    events.Add(evt);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout reached, return collected events
        }
        
        return events;
    }
}
