using Aevatar.Agents.Workspaces.Core;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Workspaces.Tests;

[Collection("EnvVarNonParallel")]
public sealed class RoleWorkspaceServiceTests(WorkspacesTestFixture fixture) : IClassFixture<WorkspacesTestFixture>
{
    private readonly WorkspacesTestFixture _fixture = fixture;

    [Fact]
    public async Task EnsureRoleAsync_ShouldLinkToRoot()
    {
        var workspace = _fixture.ServiceProvider.GetRequiredService<RoleWorkspaceService>();

        var root = await workspace.EnsureRootAsync(CancellationToken.None);
        root.Role.ShouldBe("sisyphus");

        await workspace.EnsureRoleAsync("athena", linkToRoot: true, CancellationToken.None);

        var graph = workspace.GetGraphSnapshot();
        graph.Nodes.ShouldContain(n => n.Role == "sisyphus");
        graph.Nodes.ShouldContain(n => n.Role == "athena");
        graph.Edges.ShouldContain(e => e.ParentRole == "sisyphus" && e.ChildRole == "athena");
    }

    [Fact]
    public async Task UnlinkAsync_ShouldRemoveEdge()
    {
        var workspace = _fixture.ServiceProvider.GetRequiredService<RoleWorkspaceService>();

        await workspace.EnsureRootAsync(CancellationToken.None);
        await workspace.EnsureRoleAsync("athena", linkToRoot: true, CancellationToken.None);

        await workspace.UnlinkAsync("sisyphus", "athena", CancellationToken.None);

        var graph = workspace.GetGraphSnapshot();
        graph.Edges.ShouldNotContain(e => e.ParentRole == "sisyphus" && e.ChildRole == "athena");
    }
}
