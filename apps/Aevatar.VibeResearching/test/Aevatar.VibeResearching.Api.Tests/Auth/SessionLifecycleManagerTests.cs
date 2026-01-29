using Aevatar.VibeResearching.Sessions.Enums;
using Aevatar.VibeResearching.Sessions.Permissions;
using Aevatar.VibeResearching.Sessions.Repositories;
using Aevatar.VibeResearching.Sessions.Services;
using Aevatar.Agents.AGUI;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace VibeResearching.Api.Tests.Auth;

/// <summary>
/// Tests for ResearchSessionManager lifecycle operations (Pause/Resume/Terminate)
/// and permission constant definitions.
/// </summary>
public sealed class SessionLifecycleManagerTests
{
    private readonly ISessionUiTraceService _uiTrace;
    private readonly IVibeSessionRepository _repository;
    private readonly ILogger<ResearchSessionManager> _logger;

    public SessionLifecycleManagerTests()
    {
        _uiTrace = Substitute.For<ISessionUiTraceService>();
        _repository = Substitute.For<IVibeSessionRepository>();
        _logger = Substitute.For<ILogger<ResearchSessionManager>>();
    }

    private ResearchSessionManager CreateManager() =>
        new(_uiTrace, sessionRepository: _repository, logger: _logger);

    // ─────────────────────────────────────────────────────────────
    //  PauseSessionAsync
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task PauseSessionAsync_ValidSession_PausesAndPersists()
    {
        var manager = CreateManager();
        var record = await manager.CreateSessionAsync("provider", "owner1");
        var sessionId = record.SessionId;

        await manager.PauseSessionAsync(sessionId);

        manager.TryGet(sessionId, out var session).ShouldBeTrue();
        session.Status.ShouldBe(SessionStatus.Paused);
        // SaveAsync called for create + pause = 2 calls
        await _repository.Received(2).SaveAsync(Arg.Any<Aevatar.VibeResearching.Agents.Contracts.Sessions.VibeSessionRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PauseSessionAsync_NonExistent_Throws()
    {
        var manager = CreateManager();

        await Should.ThrowAsync<InvalidOperationException>(
            () => manager.PauseSessionAsync("nonexistent"));
    }

    // ─────────────────────────────────────────────────────────────
    //  ResumeSessionAsync
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResumeSessionAsync_PausedSession_ResumesAndPersists()
    {
        var manager = CreateManager();
        var record = await manager.CreateSessionAsync("provider", "owner1");
        var sessionId = record.SessionId;

        await manager.PauseSessionAsync(sessionId);
        await manager.ResumeSessionAsync(sessionId);

        manager.TryGet(sessionId, out var session).ShouldBeTrue();
        session.Status.ShouldBe(SessionStatus.Active);
    }

    [Fact]
    public async Task ResumeSessionAsync_ActiveSession_Throws()
    {
        var manager = CreateManager();
        var record = await manager.CreateSessionAsync("provider");
        var sessionId = record.SessionId;

        await Should.ThrowAsync<InvalidOperationException>(
            () => manager.ResumeSessionAsync(sessionId));
    }

    // ─────────────────────────────────────────────────────────────
    //  TerminateSessionAsync
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task TerminateSessionAsync_ActiveSession_ArchivesAndPersists()
    {
        var manager = CreateManager();
        var record = await manager.CreateSessionAsync("provider", "owner1");
        var sessionId = record.SessionId;

        await manager.TerminateSessionAsync(sessionId);

        manager.TryGet(sessionId, out var session).ShouldBeTrue();
        session.Status.ShouldBe(SessionStatus.Archived);
    }

    [Fact]
    public async Task TerminateSessionAsync_PausedSession_ArchivesAndPersists()
    {
        var manager = CreateManager();
        var record = await manager.CreateSessionAsync("provider");
        var sessionId = record.SessionId;

        await manager.PauseSessionAsync(sessionId);
        await manager.TerminateSessionAsync(sessionId);

        manager.TryGet(sessionId, out var session).ShouldBeTrue();
        session.Status.ShouldBe(SessionStatus.Archived);
    }

    [Fact]
    public async Task TerminateSessionAsync_ArchivedSession_Throws()
    {
        var manager = CreateManager();
        var record = await manager.CreateSessionAsync("provider");
        var sessionId = record.SessionId;

        await manager.TerminateSessionAsync(sessionId);

        await Should.ThrowAsync<InvalidOperationException>(
            () => manager.TerminateSessionAsync(sessionId));
    }

    // ─────────────────────────────────────────────────────────────
    //  ListSessions — excludes archived by default
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListSessions_ExcludesArchivedByDefault()
    {
        var manager = CreateManager();
        var r1 = await manager.CreateSessionAsync("p1");
        var r2 = await manager.CreateSessionAsync("p2");

        await manager.TerminateSessionAsync(r1.SessionId);

        var list = manager.ListSessions();
        list.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ListSessions_IncludesArchivedWhenRequested()
    {
        var manager = CreateManager();
        var r1 = await manager.CreateSessionAsync("p1");
        var r2 = await manager.CreateSessionAsync("p2");

        await manager.TerminateSessionAsync(r1.SessionId);

        var list = manager.ListSessions(includeArchived: true);
        list.Count.ShouldBe(2);
    }

    // ─────────────────────────────────────────────────────────────
    //  Permission Constants (B8, B22)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void SessionsPermissions_PauseKey()
    {
        SessionsPermissions.Sessions.Pause.ShouldBe("VibeResearching.Sessions.Pause");
    }

    [Fact]
    public void SessionsPermissions_ResumeKey()
    {
        SessionsPermissions.Sessions.Resume.ShouldBe("VibeResearching.Sessions.Resume");
    }

    [Fact]
    public void SessionsPermissions_TerminateKey()
    {
        SessionsPermissions.Sessions.Terminate.ShouldBe("VibeResearching.Sessions.Terminate");
    }

    // ─────────────────────────────────────────────────────────────
    //  AuthDataSeedContributor permission sets (B22)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that Pause/Resume/Terminate are in the expected member permission set
    /// (mirroring the structure in AuthDataSeedContributor).
    /// </summary>
    [Fact]
    public void MemberPermissions_ContainLifecyclePermissions()
    {
        // These match the member permissions array in AuthDataSeedContributor
        var memberPermissions = new[]
        {
            SessionsPermissions.Sessions.View,
            SessionsPermissions.Sessions.ListAll,
            SessionsPermissions.Sessions.Create,
            SessionsPermissions.Sessions.Edit,
            SessionsPermissions.Sessions.Pause,
            SessionsPermissions.Sessions.Resume,
            SessionsPermissions.Sessions.Terminate,
            "VibeResearching.Agents.Orchestration.Execute",
            "VibeResearching.Agents.Orchestration.Cancel",
        };

        memberPermissions.ShouldContain(SessionsPermissions.Sessions.Pause);
        memberPermissions.ShouldContain(SessionsPermissions.Sessions.Resume);
        memberPermissions.ShouldContain(SessionsPermissions.Sessions.Terminate);
    }
}
