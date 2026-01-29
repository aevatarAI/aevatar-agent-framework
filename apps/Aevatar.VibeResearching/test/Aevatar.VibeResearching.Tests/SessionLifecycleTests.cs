using Aevatar.VibeResearching.Sessions.Enums;
using Aevatar.VibeResearching.Sessions.Services;
using Shouldly;

namespace VibeResearching.Tests;

/// <summary>
/// Tests for session lifecycle state transitions (Pause/Resume/Terminate).
/// Covers valid transitions, invalid transitions, and side effects.
/// </summary>
public sealed class SessionLifecycleTests
{
    // ─────────────────────────────────────────────────────────────
    //  Default State
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void NewSession_DefaultsToActive()
    {
        var session = new ResearchSession("test-1");
        session.Status.ShouldBe(SessionStatus.Active);
        session.PausedAt.ShouldBeNull();
        session.ArchivedAt.ShouldBeNull();
    }

    // ─────────────────────────────────────────────────────────────
    //  Pause
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Pause_FromActive_SetsPausedStatus()
    {
        var session = new ResearchSession("test-1");

        session.Pause();

        session.Status.ShouldBe(SessionStatus.Paused);
        session.PausedAt.ShouldNotBeNull();
    }

    [Fact]
    public void Pause_FromPaused_ThrowsInvalidOperationException()
    {
        var session = new ResearchSession("test-1");
        session.Pause();

        Should.Throw<InvalidOperationException>(() => session.Pause())
            .Message.ShouldContain("Paused");
    }

    [Fact]
    public void Pause_FromArchived_ThrowsInvalidOperationException()
    {
        var session = new ResearchSession("test-1");
        session.Terminate();

        Should.Throw<InvalidOperationException>(() => session.Pause())
            .Message.ShouldContain("Archived");
    }

    [Fact]
    public void Pause_CancelsActiveRun()
    {
        var session = new ResearchSession("test-1");
        var run = session.BeginNewRun("test-1:1", reason: "test", out _);
        run.IsCancellationRequested.ShouldBeFalse();

        session.Pause();

        run.IsCancellationRequested.ShouldBeTrue();
        session.ActiveRun.ShouldBeNull();

        run.Dispose();
    }

    // ─────────────────────────────────────────────────────────────
    //  Resume
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Resume_FromPaused_SetsActiveStatus()
    {
        var session = new ResearchSession("test-1");
        session.Pause();

        session.Resume();

        session.Status.ShouldBe(SessionStatus.Active);
        session.PausedAt.ShouldBeNull();
    }

    [Fact]
    public void Resume_FromActive_ThrowsInvalidOperationException()
    {
        var session = new ResearchSession("test-1");

        Should.Throw<InvalidOperationException>(() => session.Resume())
            .Message.ShouldContain("Active");
    }

    [Fact]
    public void Resume_FromArchived_ThrowsInvalidOperationException()
    {
        var session = new ResearchSession("test-1");
        session.Terminate();

        Should.Throw<InvalidOperationException>(() => session.Resume())
            .Message.ShouldContain("Archived");
    }

    // ─────────────────────────────────────────────────────────────
    //  Terminate
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Terminate_FromActive_SetsArchivedStatus()
    {
        var session = new ResearchSession("test-1");

        session.Terminate();

        session.Status.ShouldBe(SessionStatus.Archived);
        session.ArchivedAt.ShouldNotBeNull();
    }

    [Fact]
    public void Terminate_FromPaused_SetsArchivedStatus()
    {
        var session = new ResearchSession("test-1");
        session.Pause();

        session.Terminate();

        session.Status.ShouldBe(SessionStatus.Archived);
        session.ArchivedAt.ShouldNotBeNull();
    }

    [Fact]
    public void Terminate_FromArchived_ThrowsInvalidOperationException()
    {
        var session = new ResearchSession("test-1");
        session.Terminate();

        Should.Throw<InvalidOperationException>(() => session.Terminate())
            .Message.ShouldContain("archived");
    }

    [Fact]
    public void Terminate_CancelsActiveRun()
    {
        var session = new ResearchSession("test-1");
        var run = session.BeginNewRun("test-1:1", reason: "test", out _);

        session.Terminate();

        run.IsCancellationRequested.ShouldBeTrue();
        session.ActiveRun.ShouldBeNull();

        run.Dispose();
    }

    // ─────────────────────────────────────────────────────────────
    //  EnsureAcceptsInput
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void EnsureAcceptsInput_Active_DoesNotThrow()
    {
        var session = new ResearchSession("test-1");
        Should.NotThrow(() => session.EnsureAcceptsInput());
    }

    [Fact]
    public void EnsureAcceptsInput_Paused_ThrowsWithMessage()
    {
        var session = new ResearchSession("test-1");
        session.Pause();

        Should.Throw<InvalidOperationException>(() => session.EnsureAcceptsInput())
            .Message.ShouldBe("Session is paused");
    }

    [Fact]
    public void EnsureAcceptsInput_Archived_ThrowsWithMessage()
    {
        var session = new ResearchSession("test-1");
        session.Terminate();

        Should.Throw<InvalidOperationException>(() => session.EnsureAcceptsInput())
            .Message.ShouldBe("Session is archived");
    }

    // ─────────────────────────────────────────────────────────────
    //  RestoreStatus (backward compatibility)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void RestoreStatus_UnspecifiedTreatedAsActive()
    {
        var session = new ResearchSession("test-1");
        session.RestoreStatus(0, null, null);

        session.Status.ShouldBe(SessionStatus.Active);
    }

    [Fact]
    public void RestoreStatus_PausedRestoredCorrectly()
    {
        var session = new ResearchSession("test-1");
        var pausedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        session.RestoreStatus(SessionStatus.Paused, pausedAt, null);

        session.Status.ShouldBe(SessionStatus.Paused);
        session.PausedAt.ShouldBe(pausedAt);
        session.ArchivedAt.ShouldBeNull();
    }

    [Fact]
    public void RestoreStatus_ArchivedRestoredCorrectly()
    {
        var session = new ResearchSession("test-1");
        var archivedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        session.RestoreStatus(SessionStatus.Archived, null, archivedAt);

        session.Status.ShouldBe(SessionStatus.Archived);
        session.ArchivedAt.ShouldBe(archivedAt);
    }

    // ─────────────────────────────────────────────────────────────
    //  Full Lifecycle: Active -> Paused -> Resume -> Terminate
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void FullLifecycle_ActivePauseResumeTerminate()
    {
        var session = new ResearchSession("test-1");
        session.Status.ShouldBe(SessionStatus.Active);

        session.Pause();
        session.Status.ShouldBe(SessionStatus.Paused);

        session.Resume();
        session.Status.ShouldBe(SessionStatus.Active);

        session.Terminate();
        session.Status.ShouldBe(SessionStatus.Archived);

        // No transitions out of Archived
        Should.Throw<InvalidOperationException>(() => session.Pause());
        Should.Throw<InvalidOperationException>(() => session.Resume());
        Should.Throw<InvalidOperationException>(() => session.Terminate());
    }
}
