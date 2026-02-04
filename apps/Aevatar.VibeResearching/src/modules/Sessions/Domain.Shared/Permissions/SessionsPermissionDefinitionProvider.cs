using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace Aevatar.VibeResearching.Sessions.Permissions;

/// <summary>
/// Permission definition provider for Sessions module.
/// Registers all permissions with the ABP authorization system.
/// </summary>
public class SessionsPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(SessionsPermissions.GroupName, L("Permission:Sessions"));

        // Sessions
        var sessions = group.AddPermission(SessionsPermissions.Sessions.Default, L("Permission:Sessions"));
        sessions.AddChild(SessionsPermissions.Sessions.Create, L("Permission:Sessions.Create"));
        sessions.AddChild(SessionsPermissions.Sessions.Edit, L("Permission:Sessions.Edit"));
        sessions.AddChild(SessionsPermissions.Sessions.Delete, L("Permission:Sessions.Delete"));
        sessions.AddChild(SessionsPermissions.Sessions.Admin, L("Permission:Sessions.Admin"));
        sessions.AddChild(SessionsPermissions.Sessions.View, L("Permission:Sessions.View"));
        sessions.AddChild(SessionsPermissions.Sessions.ListAll, L("Permission:Sessions.ListAll"));
        sessions.AddChild(SessionsPermissions.Sessions.Pause, L("Permission:Sessions.Pause"));
        sessions.AddChild(SessionsPermissions.Sessions.Resume, L("Permission:Sessions.Resume"));
        sessions.AddChild(SessionsPermissions.Sessions.Terminate, L("Permission:Sessions.Terminate"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<Localization.SessionsResource>(name);
    }
}
