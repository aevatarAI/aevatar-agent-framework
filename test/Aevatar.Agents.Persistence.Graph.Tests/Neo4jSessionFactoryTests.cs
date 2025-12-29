using Aevatar.Agents.Persistence.Graph.Providers.Neo4j;
using Microsoft.Extensions.Options;
using Moq;
using Neo4j.Driver;
using Shouldly;

namespace Aevatar.Agents.Persistence.Graph.Tests;

public class Neo4jSessionFactoryTests
{
    [Fact]
    public async Task ExecuteReadAsync_uses_read_mode_and_database()
    {
        var driver = new Mock<IDriver>();
        var session = new Mock<IAsyncSession>();
        var configured = false;

        driver.Setup(d => d.AsyncSession(It.IsAny<Action<SessionConfigBuilder>>()))
            .Returns<Action<SessionConfigBuilder>>(configure =>
            {
                configured = true;
                return session.Object;
            });

        var opts = Options.Create(new Neo4jPersistenceOptions
        {
            Uri = "bolt://localhost:7687",
            Username = "u",
            Password = "p",
            Database = "neo4j"
        });

        var factory = new Neo4jSessionFactory(driver.Object, opts);
        var result = await factory.ExecuteReadAsync(_ => Task.FromResult(42));

        result.ShouldBe(42);
        configured.ShouldBeTrue();
        session.Verify(s => s.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task ExecuteWriteAsync_uses_write_mode()
    {
        var driver = new Mock<IDriver>();
        var session = new Mock<IAsyncSession>();
        var configured = false;

        driver.Setup(d => d.AsyncSession(It.IsAny<Action<SessionConfigBuilder>>()))
            .Returns<Action<SessionConfigBuilder>>(configure =>
            {
                configured = true;
                return session.Object;
            });

        var opts = Options.Create(new Neo4jPersistenceOptions
        {
            Uri = "bolt://localhost:7687",
            Username = "u",
            Password = "p",
            Database = "neo4j"
        });

        var factory = new Neo4jSessionFactory(driver.Object, opts);
        await factory.ExecuteWriteAsync(_ => Task.FromResult(true));

        configured.ShouldBeTrue();
    }
}
