using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace Aevatar.VibeResearching.Knowledge.Permissions;

/// <summary>
/// Permission definition provider for the Knowledge module.
/// </summary>
public class KnowledgePermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var knowledgeGroup = context.AddGroup(KnowledgePermissions.GroupName, L("Permission:Knowledge"));

        // DAG permissions
        knowledgeGroup.AddPermission(KnowledgePermissions.Dag.View, L("Permission:Knowledge.Dag.View"));
        knowledgeGroup.AddPermission(KnowledgePermissions.Dag.Edit, L("Permission:Knowledge.Dag.Edit"));

        // Fact permissions
        knowledgeGroup.AddPermission(KnowledgePermissions.Facts.Create, L("Permission:Knowledge.Facts.Create"));
        knowledgeGroup.AddPermission(KnowledgePermissions.Facts.Vote, L("Permission:Knowledge.Facts.Vote"));
        knowledgeGroup.AddPermission(KnowledgePermissions.Facts.Verify, L("Permission:Knowledge.Facts.Verify"));
        knowledgeGroup.AddPermission(KnowledgePermissions.Facts.Promote, L("Permission:Knowledge.Facts.Promote"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<KnowledgeResource>(name);
    }
}

/// <summary>
/// Permission constants for the Knowledge module.
/// </summary>
public static class KnowledgePermissions
{
    public const string GroupName = "Knowledge";

    public static class Dag
    {
        public const string Default = GroupName + ".Dag";
        public const string View = Default + ".View";
        public const string Edit = Default + ".Edit";
    }

    public static class Facts
    {
        public const string Default = GroupName + ".Facts";
        public const string Create = Default + ".Create";
        public const string Vote = Default + ".Vote";
        public const string Verify = Default + ".Verify";
        public const string Promote = Default + ".Promote";
    }
}

/// <summary>
/// Localization resource for Knowledge module.
/// </summary>
public class KnowledgeResource
{
}
