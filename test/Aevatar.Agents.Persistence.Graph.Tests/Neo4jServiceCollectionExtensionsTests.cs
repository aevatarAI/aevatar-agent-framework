using Aevatar.Agents.Persistence.Graph.Abstractions;
using Aevatar.Agents.Persistence.Graph.Core;
using Aevatar.Agents.Persistence.Graph.Providers.Neo4j;
using Microsoft.Extensions.DependencyInjection;
using Neo4j.Driver;
using Shouldly;

namespace Aevatar.Agents.Persistence.Graph.Tests;

public class Neo4jServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAevatarGraphNeo4j_registers_services()
    {
        var services = new ServiceCollection();

        services.AddAevatarGraphNeo4j("bolt://localhost:7687", "u", "p", "neo4j");

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<INeo4jDriverFactory>().ShouldNotBeNull();
        provider.GetRequiredService<IDriver>().ShouldNotBeNull();
        provider.GetRequiredService<INeo4jSessionFactory>().ShouldNotBeNull();
        provider.GetRequiredService<INeo4jClient>().ShouldNotBeNull();
        provider.GetRequiredService<IGraphCompiler<CypherCommand>>().ShouldBeOfType<CypherCompiler>();
        provider.GetRequiredService<IGraphExecutor<CypherCommand>>().ShouldBeOfType<Neo4jExecutor>();
        provider.GetRequiredService<IGraphClient>().ShouldBeOfType<GraphClient>();
    }
}
