using Aevatar.PaperReview.Models;
using Shouldly;
using Xunit;

namespace Aevatar.PaperReview.Tests;

// ============================================================
//  STAGE TRACKER TESTS
//  Verify stage tracker statistics and state management
// ============================================================

public class StageTrackerTests
{
    // ─────────────────────────────────────────────────────────
    //  Initialization Tests
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_InitializesWithDefaultValues()
    {
        var tracker = new StageTracker();
        
        tracker.CurrentStage.ShouldBe("");
        tracker.LlmCalls.ShouldBe(0);
        tracker.Tokens.ShouldBe(0);
        tracker.VotingRounds.ShouldBe(0);
        tracker.WorkerCount.ShouldBe(0);
        tracker.ConsensusReached.ShouldBeFalse();
        tracker.Candidates.ShouldBeEmpty();
        tracker.WorkerOutputs.ShouldBeEmpty();
        tracker.WinnerContent.ShouldBeNull();
    }

    // ─────────────────────────────────────────────────────────
    //  StartStage Tests
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void StartStage_SetsStageNameAndTime()
    {
        var tracker = new StageTracker();
        var before = DateTimeOffset.UtcNow;
        
        tracker.StartStage("Assessing");
        
        tracker.CurrentStage.ShouldBe("Assessing");
        tracker.StageStartTime.ShouldBeGreaterThanOrEqualTo(before);
        tracker.StageStartTime.ShouldBeLessThanOrEqualTo(DateTimeOffset.UtcNow);
    }

    [Fact]
    public void StartStage_ResetsStatistics()
    {
        var tracker = new StageTracker();
        
        // Add some data first
        tracker.LlmCalls = 5;
        tracker.Tokens = 1000;
        tracker.VotingRounds = 3;
        tracker.WorkerCount = 5;
        tracker.ConsensusReached = true;
        tracker.Candidates.Add(new CandidateDetail { Id = "test" });
        tracker.WorkerOutputs.Add(new WorkerOutput { WorkerId = "w1" });
        tracker.WinnerContent = "winner";
        
        // Start new stage
        tracker.StartStage("Decomposing");
        
        // Verify statistics are reset
        tracker.CurrentStage.ShouldBe("Decomposing");
        tracker.LlmCalls.ShouldBe(0);
        tracker.Tokens.ShouldBe(0);
        tracker.VotingRounds.ShouldBe(0);
        tracker.WorkerCount.ShouldBe(0);
        tracker.ConsensusReached.ShouldBeFalse();
        tracker.Candidates.ShouldBeEmpty();
        tracker.WorkerOutputs.ShouldBeEmpty();
        tracker.WinnerContent.ShouldBeNull();
    }

    // ─────────────────────────────────────────────────────────
    //  Reset Tests
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void Reset_ClearsAllStatisticsButKeepsStage()
    {
        var tracker = new StageTracker();
        tracker.CurrentStage = "Voting";
        tracker.LlmCalls = 10;
        tracker.Tokens = 5000;
        tracker.VotingRounds = 2;
        tracker.Candidates.Add(new CandidateDetail { Id = "c1" });
        
        tracker.Reset();
        
        // Statistics are cleared
        tracker.LlmCalls.ShouldBe(0);
        tracker.Tokens.ShouldBe(0);
        tracker.VotingRounds.ShouldBe(0);
        tracker.Candidates.ShouldBeEmpty();
        
        // But CurrentStage remains unchanged (Reset doesn't modify CurrentStage)
        tracker.CurrentStage.ShouldBe("Voting");
    }

    // ─────────────────────────────────────────────────────────
    //  BuildStats Tests
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void BuildStats_ReturnsCorrectStatistics()
    {
        var tracker = new StageTracker
        {
            LlmCalls = 7,
            Tokens = 3500,
            WorkerCount = 5,
            VotingRounds = 2,
            ConsensusReached = true
        };
        
        var stats = tracker.BuildStats();
        
        stats.LlmCalls.ShouldBe(7);
        stats.Tokens.ShouldBe(3500);
        stats.WorkerCount.ShouldBe(5);
        stats.VotingRounds.ShouldBe(2);
        stats.ConsensusReached.ShouldBeTrue();
    }

    // ─────────────────────────────────────────────────────────
    //  BuildDetails Tests
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void BuildDetails_WithCandidates_ReturnsDetails()
    {
        var tracker = new StageTracker();
        tracker.Candidates.Add(new CandidateDetail { Id = "c1", Votes = 3 });
        tracker.Candidates.Add(new CandidateDetail { Id = "c2", Votes = 2 });
        tracker.WinnerContent = "Winner content here";
        
        var details = tracker.BuildDetails();
        
        details.Candidates.ShouldNotBeNull();
        details.Candidates!.Count.ShouldBe(2);
        details.WinnerContent.ShouldBe("Winner content here");
    }

    [Fact]
    public void BuildDetails_WithWorkerOutputs_ReturnsDetails()
    {
        var tracker = new StageTracker();
        tracker.WorkerOutputs.Add(new WorkerOutput 
        { 
            WorkerId = "worker-0", 
            Content = "Output 1",
            Success = true,
            Tokens = 100
        });
        tracker.WorkerOutputs.Add(new WorkerOutput 
        { 
            WorkerId = "worker-1", 
            Content = "Output 2",
            Success = true,
            Tokens = 150
        });
        
        var details = tracker.BuildDetails();
        
        details.WorkerOutputs.ShouldNotBeNull();
        details.WorkerOutputs!.Count.ShouldBe(2);
        details.WorkerOutputs[0].WorkerId.ShouldBe("worker-0");
        details.WorkerOutputs[1].WorkerId.ShouldBe("worker-1");
    }

    [Fact]
    public void BuildDetails_Empty_ReturnsNullLists()
    {
        var tracker = new StageTracker();
        
        var details = tracker.BuildDetails();
        
        details.Candidates.ShouldBeNull();
        details.WorkerOutputs.ShouldBeNull();
        details.WinnerContent.ShouldBeNull();
    }

    // ─────────────────────────────────────────────────────────
    //  Accumulated Statistics Tests
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void AccumulateStats_CanTrackMultipleWorkers()
    {
        var tracker = new StageTracker();
        tracker.StartStage("Solving");
        
        // Simulate multiple Workers completing work
        for (var i = 0; i < 5; i++)
        {
            tracker.LlmCalls++;
            tracker.Tokens += 500;
            tracker.WorkerCount = i + 1;
            tracker.WorkerOutputs.Add(new WorkerOutput 
            { 
                WorkerId = $"worker-{i}",
                Content = $"Solution {i}",
                Tokens = 500,
                Success = true
            });
        }
        
        tracker.LlmCalls.ShouldBe(5);
        tracker.Tokens.ShouldBe(2500);
        tracker.WorkerCount.ShouldBe(5);
        tracker.WorkerOutputs.Count.ShouldBe(5);
    }

    [Fact]
    public void TrackVoting_RecordsVotingProgress()
    {
        var tracker = new StageTracker();
        tracker.StartStage("Voting");
        
        // First voting round
        tracker.VotingRounds = 1;
        tracker.Candidates.Add(new CandidateDetail { Id = "c1", Votes = 2 });
        tracker.Candidates.Add(new CandidateDetail { Id = "c2", Votes = 1 });
        
        // Second voting round
        tracker.VotingRounds = 2;
        tracker.Candidates.Clear();
        tracker.Candidates.Add(new CandidateDetail { Id = "c1", Votes = 3, IsWinner = true });
        tracker.Candidates.Add(new CandidateDetail { Id = "c2", Votes = 1 });
        tracker.ConsensusReached = true;
        tracker.WinnerContent = "Final consensus content";
        
        var stats = tracker.BuildStats();
        var details = tracker.BuildDetails();
        
        stats.VotingRounds.ShouldBe(2);
        stats.ConsensusReached.ShouldBeTrue();
        details.WinnerContent.ShouldBe("Final consensus content");
        details.Candidates!.First(c => c.IsWinner).Id.ShouldBe("c1");
    }
}
