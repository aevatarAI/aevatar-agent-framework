namespace Aevatar.VibeResearching.Sessions.Permissions;

/// <summary>
/// Permission names for Sessions module.
/// Defines a hierarchical structure of permissions for access control.
/// </summary>
public static class SessionsPermissions
{
    /// <summary>
    /// Root group name for all Sessions permissions.
    /// </summary>
    public const string GroupName = "VibeResearching.Sessions";

    /// <summary>
    /// Permissions for research session management.
    /// </summary>
    public static class Sessions
    {
        public const string Default = GroupName;
        public const string Create = Default + ".Create";
        public const string Edit = Default + ".Edit";
        public const string Delete = Default + ".Delete";
        public const string Admin = Default + ".Admin";
        public const string View = Default + ".View";
        public const string ListAll = Default + ".ListAll";
    }
}
