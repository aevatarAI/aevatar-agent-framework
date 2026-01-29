using Aevatar.Agents.Sessions.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Sessions.Tests;

public sealed class SessionWorkflowCatalogTests(SessionsTestFixture fixture) : IClassFixture<SessionsTestFixture>
{
    private readonly SessionsTestFixture _fixture = fixture;

    [Fact]
    public void EnsureRoleWorkflowYaml_ShouldWriteWorkflowFile()
    {
        var catalog = _fixture.ServiceProvider.GetRequiredService<IWorkflowCatalog>();
        var workflowName = catalog.EnsureRoleWorkflowYaml("tester");
        var expectedPath = Path.Combine(_fixture.WorkflowsDirectory, $"{workflowName}.yaml");

        File.Exists(expectedPath).ShouldBeTrue();
        var content = File.ReadAllText(expectedPath);
        content.ShouldContain("id: tester");
    }
}
