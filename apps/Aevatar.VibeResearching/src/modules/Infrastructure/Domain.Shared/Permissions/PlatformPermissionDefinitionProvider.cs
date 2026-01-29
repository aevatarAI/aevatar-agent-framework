using Aevatar.VibeResearching.Infrastructure.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace Aevatar.VibeResearching.Infrastructure.Permissions;

/// <summary>
/// Registers Platform-level permissions with the ABP authorization system.
/// </summary>
public class PlatformPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(PlatformPermissions.GroupName, L("Permission:Platform"));

        var settings = group.AddPermission(
            PlatformPermissions.Settings.Default, L("Permission:Platform.Settings"));
        settings.AddChild(
            PlatformPermissions.Settings.LLM, L("Permission:Platform.Settings.LLM"));

        var userMgmt = group.AddPermission(
            PlatformPermissions.UserManagement.Default, L("Permission:Platform.UserManagement"));
        userMgmt.AddChild(
            PlatformPermissions.UserManagement.ManageRoles, L("Permission:Platform.UserManagement.ManageRoles"));
        userMgmt.AddChild(
            PlatformPermissions.UserManagement.ManagePermissions, L("Permission:Platform.UserManagement.ManagePermissions"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<PlatformResource>(name);
    }
}
