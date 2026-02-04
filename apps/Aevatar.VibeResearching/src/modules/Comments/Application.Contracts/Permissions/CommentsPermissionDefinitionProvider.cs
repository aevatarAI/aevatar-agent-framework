using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace Aevatar.VibeResearching.Comments.Permissions;

/// <summary>
/// Localization resource marker for the Comments module.
/// </summary>
public class CommentsResource;

/// <summary>
/// Permission definition provider for the Comments module.
/// Registers all permissions with the ABP authorization system.
/// </summary>
public class CommentsPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(CommentsPermissions.GroupName, L("Permission:Comments"));

        var comments = group.AddPermission(CommentsPermissions.Comments.Default, L("Permission:Comments"));
        comments.AddChild(CommentsPermissions.Comments.Create, L("Permission:Comments.Create"));
        comments.AddChild(CommentsPermissions.Comments.Edit, L("Permission:Comments.Edit"));
        comments.AddChild(CommentsPermissions.Comments.EditAny, L("Permission:Comments.EditAny"));
        comments.AddChild(CommentsPermissions.Comments.Delete, L("Permission:Comments.Delete"));
        comments.AddChild(CommentsPermissions.Comments.DeleteAny, L("Permission:Comments.DeleteAny"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<CommentsResource>(name);
    }
}
