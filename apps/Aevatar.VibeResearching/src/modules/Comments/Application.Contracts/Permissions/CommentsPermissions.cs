namespace Aevatar.VibeResearching.Comments.Permissions;

/// <summary>
/// Permission constants for the Comments module.
/// </summary>
public static class CommentsPermissions
{
    /// <summary>
    /// Root group name for all Comments permissions.
    /// </summary>
    public const string GroupName = "VibeResearching.Comments";

    public static class Comments
    {
        public const string Default = GroupName;
        public const string Create = Default + ".Create";
        public const string Edit = Default + ".Edit";
        public const string EditAny = Default + ".EditAny";
        public const string Delete = Default + ".Delete";
        public const string DeleteAny = Default + ".DeleteAny";
    }
}
