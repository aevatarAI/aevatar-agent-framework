using Aevatar.VibeResearching.Sessions.Repositories;
using Aevatar.VibeResearching.Sessions.Services;
using Aevatar.Agents.AGUI;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace VibeResearching.Api.Tests.Auth;

/// <summary>
/// Tests for session ownership tracking and persistent deletion (C-2 fix).
/// Verifies that OwnerId is set on session creation and that DeleteSessionAsync
/// removes sessions from both memory and persistent storage.
/// </summary>
public sealed class SessionOwnershipAndDeleteTests
{
    private readonly ISessionUiTraceService _uiTrace;
    private readonly IVibeSessionRepository _repository;
    private readonly ILogger<ResearchSessionManager> _logger;

    public SessionOwnershipAndDeleteTests()
    {
        _uiTrace = Substitute.For<ISessionUiTraceService>();
        _repository = Substitute.For<IVibeSessionRepository>();
        _logger = Substitute.For<ILogger<ResearchSessionManager>>();
    }

    // ─────────────────────────────────────────────────────────────
    //  ResearchSession OwnerId
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ResearchSession_OwnerId_DefaultsToNull()
    {
        var session = new ResearchSession("test-session");
        session.OwnerId.ShouldBeNull();
    }

    [Fact]
    public void ResearchSession_OwnerId_CanBeSet()
    {
        var userId = Guid.NewGuid().ToString();
        var session = new ResearchSession("test-session") { OwnerId = userId };
        session.OwnerId.ShouldBe(userId);
    }

    // ─────────────────────────────────────────────────────────────
    //  DeleteSessionAsync — Persistent Deletion (C-2)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteSessionAsync_WithRepository_CallsDeleteAsync()
    {
        // Arrange
        var manager = new ResearchSessionManager(
            _uiTrace,
            sessionRepository: _repository,
            logger: _logger);

        // First create a session so there's something to delete
        var record = await manager.CreateSessionAsync("test-provider", "owner-123");
        var sessionId = record.SessionId;

        // Act
        await manager.DeleteSessionAsync(sessionId);

        // Assert: repository.DeleteAsync should be called
        await _repository.Received(1).DeleteAsync(sessionId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteSessionAsync_WithoutRepository_DoesNotThrow()
    {
        // Arrange: no repository configured
        var manager = new ResearchSessionManager(
            _uiTrace,
            sessionRepository: null,
            logger: _logger);

        var record = await manager.CreateSessionAsync("test-provider");
        var sessionId = record.SessionId;

        // Act & Assert: should not throw even without repository
        await Should.NotThrowAsync(() => manager.DeleteSessionAsync(sessionId));
    }

    [Fact]
    public async Task DeleteSessionAsync_RemovesFromMemory()
    {
        // Arrange
        var manager = new ResearchSessionManager(
            _uiTrace,
            sessionRepository: _repository,
            logger: _logger);

        var record = await manager.CreateSessionAsync("test-provider");
        var sessionId = record.SessionId;

        // Verify session exists before delete
        manager.TryGet(sessionId, out _).ShouldBeTrue();

        // Act
        await manager.DeleteSessionAsync(sessionId);

        // Assert: session should be removed from memory
        manager.TryGet(sessionId, out _).ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteSessionAsync_NonExistentSession_StillCallsRepositoryDelete()
    {
        // Arrange
        var manager = new ResearchSessionManager(
            _uiTrace,
            sessionRepository: _repository,
            logger: _logger);

        // Act: delete a session that doesn't exist in memory
        await manager.DeleteSessionAsync("nonexistent");

        // Assert: repository.DeleteAsync should still be called
        // (session might exist in persistent storage but not in memory)
        await _repository.Received(1).DeleteAsync("nonexistent", Arg.Any<CancellationToken>());
    }

    // ─────────────────────────────────────────────────────────────
    //  CreateSessionAsync — Owner Tracking
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSessionAsync_SetsOwnerId()
    {
        // Arrange
        var manager = new ResearchSessionManager(
            _uiTrace,
            sessionRepository: _repository,
            logger: _logger);

        // Act
        var record = await manager.CreateSessionAsync("provider", "user-456");

        // Assert
        record.OwnerId.ShouldBe("user-456");
    }

    [Fact]
    public async Task CreateSessionAsync_WithoutOwnerId_LeavesEmpty()
    {
        // Arrange
        var manager = new ResearchSessionManager(
            _uiTrace,
            sessionRepository: _repository,
            logger: _logger);

        // Act
        var record = await manager.CreateSessionAsync("provider");

        // Assert
        record.OwnerId.ShouldBeNullOrEmpty();
    }
}
