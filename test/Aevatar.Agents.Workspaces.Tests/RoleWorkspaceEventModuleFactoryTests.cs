using Aevatar.Agents.Workspaces.Events;
using Aevatar.Agents.Workspaces.Hubs;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Workspaces.Tests;

public sealed class RoleWorkspaceEventModuleFactoryTests
{
    [Theory]
    [InlineData("workspace_chat_trace", typeof(RoleWorkspaceChatTraceModule))]
    [InlineData("workspace_ping", typeof(RoleWorkspacePingModule))]
    [InlineData("workspace_group_agui", typeof(RoleWorkspaceGroupAgUiModule))]
    [InlineData("workspace_group_task", typeof(RoleWorkspaceGroupTaskModule))]
    public void TryCreate_ShouldReturnModule(string name, Type expected)
    {
        var factory = new RoleWorkspaceEventModuleFactory(new RoleAgUiHub());

        factory.TryCreate(name, out var module).ShouldBeTrue();
        module.ShouldNotBeNull();
        module.ShouldBeOfType(expected);
    }
}
