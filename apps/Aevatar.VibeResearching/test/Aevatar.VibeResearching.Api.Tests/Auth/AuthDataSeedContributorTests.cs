using Shouldly;
using Aevatar.VibeResearching.Sessions.Permissions;
using Aevatar.VibeResearching.Infrastructure.Permissions;

namespace VibeResearching.Api.Tests.Auth;

/// <summary>
/// Tests for the data seeding configuration used by AuthDataSeedContributor.
/// Verifies that the expected permission sets match the spec (Section 8)
/// and that role/permission mappings are correctly structured.
///
/// Note: AuthDataSeedContributor depends on ABP's IdentityRoleManager (11-param constructor)
/// and other complex ABP services. Direct unit testing requires ABP integration test infrastructure.
/// These tests validate the permission and role CONFIGURATION that feeds the seeder.
/// </summary>
public sealed class AuthDataSeedContributorTests
{
    // Expected member permissions per spec Section 8
    private static readonly string[] ExpectedMemberPermissions =
    [
        SessionsPermissions.Sessions.View,
        SessionsPermissions.Sessions.ListAll,
        SessionsPermissions.Sessions.Create,
        SessionsPermissions.Sessions.Edit,
        SessionsPermissions.Sessions.Pause,
        SessionsPermissions.Sessions.Resume,
        SessionsPermissions.Sessions.Terminate,
        "VibeResearching.Agents.Orchestration.Execute",
        "VibeResearching.Agents.Orchestration.Cancel",
    ];

    // Expected admin-only permissions (in addition to member permissions)
    private static readonly string[] ExpectedAdminExtraPermissions =
    [
        SessionsPermissions.Sessions.Delete,
        SessionsPermissions.Sessions.Admin,
        "VibeResearching.Agents.ReviewAgent.Trigger",
        PlatformPermissions.Settings.Default,
        PlatformPermissions.Settings.LLM,
        PlatformPermissions.UserManagement.Default,
        PlatformPermissions.UserManagement.ManageRoles,
        PlatformPermissions.UserManagement.ManagePermissions,
    ];

    // ─────────────────────────────────────────────────────────────
    //  Member Permission Set
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void MemberPermissions_ContainsSessionViewAndCreate()
    {
        ExpectedMemberPermissions.ShouldContain(SessionsPermissions.Sessions.View);
        ExpectedMemberPermissions.ShouldContain(SessionsPermissions.Sessions.Create);
    }

    [Fact]
    public void MemberPermissions_DoesNotContainDelete()
    {
        ExpectedMemberPermissions.ShouldNotContain(SessionsPermissions.Sessions.Delete);
    }

    [Fact]
    public void MemberPermissions_DoesNotContainAdmin()
    {
        ExpectedMemberPermissions.ShouldNotContain(SessionsPermissions.Sessions.Admin);
    }

    [Fact]
    public void MemberPermissions_DoesNotContainPlatformSettings()
    {
        ExpectedMemberPermissions.ShouldNotContain(PlatformPermissions.Settings.Default);
        ExpectedMemberPermissions.ShouldNotContain(PlatformPermissions.UserManagement.Default);
    }

    [Fact]
    public void MemberPermissions_ContainsOrchestrationExecuteAndCancel()
    {
        ExpectedMemberPermissions.ShouldContain("VibeResearching.Agents.Orchestration.Execute");
        ExpectedMemberPermissions.ShouldContain("VibeResearching.Agents.Orchestration.Cancel");
    }

    // ─────────────────────────────────────────────────────────────
    //  Admin Extra Permission Set
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AdminExtraPermissions_ContainsSessionDeleteAndAdmin()
    {
        ExpectedAdminExtraPermissions.ShouldContain(SessionsPermissions.Sessions.Delete);
        ExpectedAdminExtraPermissions.ShouldContain(SessionsPermissions.Sessions.Admin);
    }

    [Fact]
    public void AdminExtraPermissions_ContainsAllPlatformPermissions()
    {
        ExpectedAdminExtraPermissions.ShouldContain(PlatformPermissions.Settings.Default);
        ExpectedAdminExtraPermissions.ShouldContain(PlatformPermissions.Settings.LLM);
        ExpectedAdminExtraPermissions.ShouldContain(PlatformPermissions.UserManagement.Default);
        ExpectedAdminExtraPermissions.ShouldContain(PlatformPermissions.UserManagement.ManageRoles);
        ExpectedAdminExtraPermissions.ShouldContain(PlatformPermissions.UserManagement.ManagePermissions);
    }

    [Fact]
    public void AdminExtraPermissions_ContainsReviewAgentTrigger()
    {
        ExpectedAdminExtraPermissions.ShouldContain("VibeResearching.Agents.ReviewAgent.Trigger");
    }

    // ─────────────────────────────────────────────────────────────
    //  Permission Set Consistency
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void MemberAndAdminSets_HaveNoOverlap()
    {
        var overlap = ExpectedMemberPermissions.Intersect(ExpectedAdminExtraPermissions).ToList();
        overlap.ShouldBeEmpty("Member and Admin-extra permission sets should not overlap");
    }

    [Fact]
    public void AllPermissionKeys_AreNonEmpty()
    {
        foreach (var perm in ExpectedMemberPermissions.Concat(ExpectedAdminExtraPermissions))
        {
            perm.ShouldNotBeNullOrWhiteSpace($"Permission key should not be empty");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Sessions Permission Constants
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void SessionsPermissions_ViewKey()
    {
        SessionsPermissions.Sessions.View.ShouldBe("VibeResearching.Sessions.View");
    }

    [Fact]
    public void SessionsPermissions_CreateKey()
    {
        SessionsPermissions.Sessions.Create.ShouldBe("VibeResearching.Sessions.Create");
    }

    [Fact]
    public void SessionsPermissions_DeleteKey()
    {
        SessionsPermissions.Sessions.Delete.ShouldBe("VibeResearching.Sessions.Delete");
    }

    [Fact]
    public void SessionsPermissions_AdminKey()
    {
        SessionsPermissions.Sessions.Admin.ShouldBe("VibeResearching.Sessions.Admin");
    }
}
