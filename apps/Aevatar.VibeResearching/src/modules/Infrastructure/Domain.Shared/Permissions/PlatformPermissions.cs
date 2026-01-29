namespace Aevatar.VibeResearching.Infrastructure.Permissions;

/// <summary>
/// Platform-level permission names for settings and user management.
/// </summary>
public static class PlatformPermissions
{
    public const string GroupName = "Platform";

    public static class Settings
    {
        public const string Default = GroupName + ".Settings";
        public const string LLM = Default + ".LLM";
    }

    public static class UserManagement
    {
        public const string Default = GroupName + ".UserManagement";
        public const string ManageRoles = Default + ".ManageRoles";
        public const string ManagePermissions = Default + ".ManagePermissions";
    }
}
