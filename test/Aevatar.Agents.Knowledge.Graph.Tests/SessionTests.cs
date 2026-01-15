using Aevatar.Agents.Knowledge.Graph.Models;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Knowledge.Graph.Tests;

/// <summary>
/// Unit tests for Session lifecycle per FR-015a.
/// </summary>
public class SessionTests
{
    #region Session.Create Tests

    [Fact]
    public void Create_WithoutSessionId_GeneratesNewId()
    {
        var session = Session.Create();

        session.ShouldNotBeNull();
        session.Id.ShouldStartWith("session-");
        session.Id.Length.ShouldBeGreaterThan("session-".Length);
    }

    [Fact]
    public void Create_WithSessionId_UsesProvidedId()
    {
        var session = Session.Create("my-custom-session");

        session.Id.ShouldBe("my-custom-session");
    }

    [Fact]
    public void Create_SetsStartedAtToNow()
    {
        var before = DateTimeOffset.UtcNow;
        var session = Session.Create();
        var after = DateTimeOffset.UtcNow;

        session.StartedAt.ShouldBeGreaterThanOrEqualTo(before);
        session.StartedAt.ShouldBeLessThanOrEqualTo(after);
    }

    [Fact]
    public void Create_SetsStatusToActive()
    {
        var session = Session.Create();

        session.Status.ShouldBe(SessionStatus.Active);
    }

    [Fact]
    public void Create_EndedAtIsNull()
    {
        var session = Session.Create();

        session.EndedAt.ShouldBeNull();
    }

    #endregion

    #region Session.End Tests

    [Fact]
    public void End_ActiveSession_SetsStatusToCompleted()
    {
        var session = Session.Create();

        session.End(SessionStatus.Completed);

        session.Status.ShouldBe(SessionStatus.Completed);
    }

    [Fact]
    public void End_ActiveSession_SetsStatusToAbandoned()
    {
        var session = Session.Create();

        session.End(SessionStatus.Abandoned);

        session.Status.ShouldBe(SessionStatus.Abandoned);
    }

    [Fact]
    public void End_DefaultsToCompleted()
    {
        var session = Session.Create();

        session.End();

        session.Status.ShouldBe(SessionStatus.Completed);
    }

    [Fact]
    public void End_SetsEndedAtToNow()
    {
        var session = Session.Create();
        var before = DateTimeOffset.UtcNow;

        session.End();

        var after = DateTimeOffset.UtcNow;
        session.EndedAt.ShouldNotBeNull();
        session.EndedAt.Value.ShouldBeGreaterThanOrEqualTo(before);
        session.EndedAt.Value.ShouldBeLessThanOrEqualTo(after);
    }

    [Fact]
    public void End_AlreadyCompletedSession_ThrowsInvalidOperationException()
    {
        var session = Session.Create();
        session.End(SessionStatus.Completed);

        var ex = Should.Throw<InvalidOperationException>(() => session.End());
        ex.Message.ShouldContain("Completed");
    }

    [Fact]
    public void End_AlreadyAbandonedSession_ThrowsInvalidOperationException()
    {
        var session = Session.Create();
        session.End(SessionStatus.Abandoned);

        var ex = Should.Throw<InvalidOperationException>(() => session.End());
        ex.Message.ShouldContain("Abandoned");
    }

    [Fact]
    public void End_WithActiveStatus_ThrowsArgumentException()
    {
        var session = Session.Create();

        var ex = Should.Throw<ArgumentException>(() => session.End(SessionStatus.Active));
        ex.ParamName.ShouldBe("status");
    }

    #endregion

    #region SessionStatus Enum Tests

    [Fact]
    public void SessionStatus_HasExpectedValues()
    {
        Enum.GetValues<SessionStatus>().ShouldContain(SessionStatus.Active);
        Enum.GetValues<SessionStatus>().ShouldContain(SessionStatus.Completed);
        Enum.GetValues<SessionStatus>().ShouldContain(SessionStatus.Abandoned);
    }

    #endregion
}
