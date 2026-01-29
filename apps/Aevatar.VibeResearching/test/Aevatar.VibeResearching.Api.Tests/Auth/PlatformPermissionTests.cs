using Aevatar.VibeResearching.Infrastructure.Permissions;
using Shouldly;

namespace VibeResearching.Api.Tests.Auth;

/// <summary>
/// Unit tests for PlatformPermissions constants and PlatformPermissionDefinitionProvider.
/// Verifies permission keys match the expected hierarchy documented in spec Section 8.
/// </summary>
public sealed class PlatformPermissionTests
{
    // ─────────────────────────────────────────────────────────────
    //  Permission Constants
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void GroupName_IsPlatform()
    {
        PlatformPermissions.GroupName.ShouldBe("Platform");
    }

    [Fact]
    public void Settings_Default_HasCorrectKey()
    {
        PlatformPermissions.Settings.Default.ShouldBe("Platform.Settings");
    }

    [Fact]
    public void Settings_LLM_HasCorrectKey()
    {
        PlatformPermissions.Settings.LLM.ShouldBe("Platform.Settings.LLM");
    }

    [Fact]
    public void UserManagement_Default_HasCorrectKey()
    {
        PlatformPermissions.UserManagement.Default.ShouldBe("Platform.UserManagement");
    }

    [Fact]
    public void UserManagement_ManageRoles_HasCorrectKey()
    {
        PlatformPermissions.UserManagement.ManageRoles.ShouldBe("Platform.UserManagement.ManageRoles");
    }

    [Fact]
    public void UserManagement_ManagePermissions_HasCorrectKey()
    {
        PlatformPermissions.UserManagement.ManagePermissions.ShouldBe("Platform.UserManagement.ManagePermissions");
    }

    // ─────────────────────────────────────────────────────────────
    //  Permission Hierarchy Consistency
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Settings_Children_AreUnderParent()
    {
        PlatformPermissions.Settings.LLM.ShouldStartWith(PlatformPermissions.Settings.Default);
    }

    [Fact]
    public void UserManagement_Children_AreUnderParent()
    {
        PlatformPermissions.UserManagement.ManageRoles.ShouldStartWith(PlatformPermissions.UserManagement.Default);
        PlatformPermissions.UserManagement.ManagePermissions.ShouldStartWith(PlatformPermissions.UserManagement.Default);
    }

    [Fact]
    public void AllPermissions_AreUnderGroupName()
    {
        PlatformPermissions.Settings.Default.ShouldStartWith(PlatformPermissions.GroupName);
        PlatformPermissions.Settings.LLM.ShouldStartWith(PlatformPermissions.GroupName);
        PlatformPermissions.UserManagement.Default.ShouldStartWith(PlatformPermissions.GroupName);
        PlatformPermissions.UserManagement.ManageRoles.ShouldStartWith(PlatformPermissions.GroupName);
        PlatformPermissions.UserManagement.ManagePermissions.ShouldStartWith(PlatformPermissions.GroupName);
    }
}
