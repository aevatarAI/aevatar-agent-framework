using Aevatar.Agents.Tooling.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Sessions.Tests;

public sealed class AgentToolCatalogTests(SessionsTestFixture fixture) : IClassFixture<SessionsTestFixture>
{
    private readonly SessionsTestFixture _fixture = fixture;

    [Fact]
    public async Task ListDotNetFilesAsync_ShouldReturnToolsInDirectory()
    {
        var catalog = _fixture.ServiceProvider.GetRequiredService<AgentToolCatalog>();
        var filePath = Path.Combine(_fixture.ToolsDirectory, $"SampleTool_{Guid.NewGuid():N}Tool.cs");
        await File.WriteAllTextAsync(filePath, "// tool");

        var tools = await catalog.ListDotNetFilesAsync(CancellationToken.None);

        tools.Any(t => string.Equals(t.FilePath, Path.GetFullPath(filePath), StringComparison.OrdinalIgnoreCase))
            .ShouldBeTrue();
    }
}
