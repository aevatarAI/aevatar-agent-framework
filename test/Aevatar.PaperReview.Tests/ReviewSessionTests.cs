using Aevatar.PaperReview.Models;
using Shouldly;
using Xunit;

namespace Aevatar.PaperReview.Tests;

// ============================================================
//  REVIEW SESSION TESTS
//  Verify review session model state management
// ============================================================

public class ReviewSessionTests
{
    // ─────────────────────────────────────────────────────────
    //  Initialization Tests
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_GeneratesUniqueId()
    {
        var session1 = new ReviewSession();
        var session2 = new ReviewSession();
        
        session1.Id.ShouldNotBeNullOrWhiteSpace();
        session2.Id.ShouldNotBeNullOrWhiteSpace();
        session1.Id.ShouldNotBe(session2.Id);
    }

    [Fact]
    public void Constructor_IdIs12Characters()
    {
        var session = new ReviewSession();
        session.Id.Length.ShouldBe(12);
    }

    [Fact]
    public void Constructor_DefaultsToStandardType()
    {
        var session = new ReviewSession();
        session.Type.ShouldBe(ReviewType.Standard);
    }

    [Fact]
    public void Constructor_DefaultsToPendingStatus()
    {
        var session = new ReviewSession();
        session.Status.ShouldBe(ReviewStatus.Pending);
    }

    [Fact]
    public void Constructor_SetsCreatedAtToUtcNow()
    {
        var before = DateTimeOffset.UtcNow;
        var session = new ReviewSession();
        var after = DateTimeOffset.UtcNow;
        
        session.CreatedAt.ShouldBeGreaterThanOrEqualTo(before);
        session.CreatedAt.ShouldBeLessThanOrEqualTo(after);
    }

    [Fact]
    public void Constructor_InitializesEmptyCollections()
    {
        var session = new ReviewSession();
        
        session.Timeline.ShouldBeEmpty();
        session.Files.ShouldBeEmpty();
        session.EventChannel.ShouldNotBeNull();
        session.CancellationTokenSource.ShouldNotBeNull();
    }

    // ─────────────────────────────────────────────────────────
    //  Property Tests
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void Properties_CanBeSet()
    {
        var session = new ReviewSession
        {
            Title = "Test Paper Title",
            Authors = "John Doe, Jane Smith",
            Type = ReviewType.Detailed,
            VenueType = "NeurIPS",
            PaperContent = "Paper content here...",
            UploadId = "upload-123"
        };
        
        session.Title.ShouldBe("Test Paper Title");
        session.Authors.ShouldBe("John Doe, Jane Smith");
        session.Type.ShouldBe(ReviewType.Detailed);
        session.VenueType.ShouldBe("NeurIPS");
        session.PaperContent.ShouldBe("Paper content here...");
        session.UploadId.ShouldBe("upload-123");
    }

    [Fact]
    public void StatusTransitions_AreValid()
    {
        var session = new ReviewSession();
        
        session.Status.ShouldBe(ReviewStatus.Pending);
        
        session.Status = ReviewStatus.Reviewing;
        session.Status.ShouldBe(ReviewStatus.Reviewing);
        
        session.Status = ReviewStatus.Completed;
        session.Status.ShouldBe(ReviewStatus.Completed);
    }

    // ─────────────────────────────────────────────────────────
    //  Timeline Tests
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void Timeline_CanAddEntries()
    {
        var session = new ReviewSession();
        
        session.Timeline.Add(new TimelineEntry("Starting", "Initializing review", DateTimeOffset.UtcNow));
        session.Timeline.Add(new TimelineEntry("Assessing", "Analyzing complexity", DateTimeOffset.UtcNow));
        
        session.Timeline.Count.ShouldBe(2);
        session.Timeline[0].Phase.ShouldBe("Starting");
        session.Timeline[1].Phase.ShouldBe("Assessing");
    }

    // ─────────────────────────────────────────────────────────
    //  Files Tests
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void Files_CanStoreCategorizedContent()
    {
        var session = new ReviewSession();
        
        session.Files.GetOrAdd("reports", _ => new())[" review_report.md"] = "# Review Report";
        session.Files.GetOrAdd("logs", _ => new())["debug.log"] = "Debug info...";
        
        session.Files.Count.ShouldBe(2);
        session.Files["reports"][" review_report.md"].ShouldStartWith("# Review");
    }

    // ─────────────────────────────────────────────────────────
    //  EventChannel Tests
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task EventChannel_CanWriteAndRead()
    {
        var session = new ReviewSession();
        var evt = new ProgressEvent
        {
            SessionId = session.Id,
            Phase = "Testing",
            Message = "Test message"
        };
        
        session.EventChannel.Writer.TryWrite(evt).ShouldBeTrue();
        
        var success = session.EventChannel.Reader.TryRead(out var readEvt);
        success.ShouldBeTrue();
        readEvt.ShouldBeOfType<ProgressEvent>();
        ((ProgressEvent)readEvt!).Message.ShouldBe("Test message");
    }

    // ─────────────────────────────────────────────────────────
    //  CancellationToken Tests
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void CancellationTokenSource_CanBeCancelled()
    {
        var session = new ReviewSession();
        
        session.CancellationTokenSource.IsCancellationRequested.ShouldBeFalse();
        
        session.CancellationTokenSource.Cancel();
        
        session.CancellationTokenSource.IsCancellationRequested.ShouldBeTrue();
    }

    // ─────────────────────────────────────────────────────────
    //  Statistics Property Tests
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void Statistics_TrackReviewProgress()
    {
        var session = new ReviewSession
        {
            TotalLlmCalls = 15,
            TotalTokens = 50000,
            ProgressPercent = 75,
            Duration = TimeSpan.FromMinutes(5)
        };
        
        session.TotalLlmCalls.ShouldBe(15);
        session.TotalTokens.ShouldBe(50000);
        session.ProgressPercent.ShouldBe(75);
        session.Duration.TotalMinutes.ShouldBe(5);
    }
}

// ============================================================
//  REVIEW EVENT TESTS
//  Verify review event model
// ============================================================

public class ReviewEventTests
{
    [Fact]
    public void ProgressEvent_TypeReturnsCorrectName()
    {
        var evt = new ProgressEvent
        {
            SessionId = "test",
            Phase = "Testing"
        };
        
        evt.Type.ShouldBe("ProgressEvent");
    }

    [Fact]
    public void ReviewEvent_SetsTimestamp()
    {
        var before = DateTimeOffset.UtcNow;
        var evt = new ProgressEvent { SessionId = "test", Phase = "Test" };
        var after = DateTimeOffset.UtcNow;
        
        evt.Timestamp.ShouldBeGreaterThanOrEqualTo(before);
        evt.Timestamp.ShouldBeLessThanOrEqualTo(after);
    }

    [Fact]
    public void LlmCallCompleteEvent_CalculatesTotalTokens()
    {
        var evt = new LlmCallCompleteEvent
        {
            SessionId = "test",
            PromptTokens = 100,
            CompletionTokens = 200
        };
        
        evt.TotalTokens.ShouldBe(300);
    }

    [Fact]
    public void StageStats_AllPropertiesSettable()
    {
        var stats = new StageStats
        {
            LlmCalls = 5,
            Tokens = 10000,
            WorkerCount = 3,
            VotingRounds = 2,
            ConsensusReached = true,
            SubTaskCount = 4
        };
        
        stats.LlmCalls.ShouldBe(5);
        stats.Tokens.ShouldBe(10000);
        stats.WorkerCount.ShouldBe(3);
        stats.VotingRounds.ShouldBe(2);
        stats.ConsensusReached.ShouldBeTrue();
        stats.SubTaskCount.ShouldBe(4);
    }
}
