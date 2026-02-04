using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace Aevatar.VibeResearching.Agents.Application.Contracts.Permissions;

public class AgentsPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var agentsGroup = context.AddGroup("VibeResearching.Agents", L("Permission:Agents"));

        var orchestration = agentsGroup.AddPermission("VibeResearching.Agents.Orchestration", L("Permission:Agents.Orchestration"));
        orchestration.AddChild("VibeResearching.Agents.Orchestration.Execute", L("Permission:Agents.Orchestration.Execute"));
        orchestration.AddChild("VibeResearching.Agents.Orchestration.Cancel", L("Permission:Agents.Orchestration.Cancel"));

        var pivot = agentsGroup.AddPermission("VibeResearching.Agents.Pivot", L("Permission:Agents.Pivot"));
        pivot.AddChild("VibeResearching.Agents.Pivot.Execute", L("Permission:Agents.Pivot.Execute"));

        var reviewAgent = agentsGroup.AddPermission("VibeResearching.Agents.ReviewAgent", L("Permission:Agents.ReviewAgent"));
        reviewAgent.AddChild("VibeResearching.Agents.ReviewAgent.Trigger", L("Permission:Agents.ReviewAgent.Trigger"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<AgentsResource>(name);
    }
}

public class AgentsResource
{
}
