using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace Aevatar.VibeResearching.UserProviders.Permissions;

/// <summary>
/// Defines permissions for the UserProviders module in the ABP permission system.
/// Uses fixed (non-localized) display names.
/// </summary>
public class UserProvidersPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(
            UserProvidersPermissions.GroupName,
            L("User LLM Provider Management"));

        var providers = group.AddPermission(
            UserProvidersPermissions.Providers.Default,
            L("View user LLM providers"));

        providers.AddChild(
            UserProvidersPermissions.Providers.Create,
            L("Create user LLM providers"));

        providers.AddChild(
            UserProvidersPermissions.Providers.Edit,
            L("Edit user LLM providers"));

        providers.AddChild(
            UserProvidersPermissions.Providers.Delete,
            L("Delete user LLM providers"));

        providers.AddChild(
            UserProvidersPermissions.Providers.Test,
            L("Test user LLM provider connectivity"));

        var codex = group.AddPermission(
            UserProvidersPermissions.Codex.Default,
            L("View Codex OAuth status"));

        codex.AddChild(
            UserProvidersPermissions.Codex.Connect,
            L("Connect Codex OAuth"));

        codex.AddChild(
            UserProvidersPermissions.Codex.Disconnect,
            L("Disconnect Codex OAuth"));
    }

    private static ILocalizableString L(string name)
    {
        return new FixedLocalizableString(name);
    }
}
