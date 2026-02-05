namespace Aevatar.VibeResearching.UserProviders.Permissions;

/// <summary>
/// Permission names for the UserProviders module.
/// </summary>
public static class UserProvidersPermissions
{
    public const string GroupName = "VibeResearching.UserProviders";

    public static class Providers
    {
        public const string Default = GroupName;
        public const string Create = Default + ".Create";
        public const string Edit = Default + ".Edit";
        public const string Delete = Default + ".Delete";
        public const string Test = Default + ".Test";
    }

    public static class Codex
    {
        public const string Default = GroupName + ".Codex";
        public const string Connect = Default + ".Connect";
        public const string Disconnect = Default + ".Disconnect";
    }
}
