using Aevatar.Agents.Persistence.Neo4j;
using Moq;
using Neo4j.Driver;
using Shouldly;

namespace Aevatar.Agents.Persistence.Graph.Tests;

public class Neo4jDriverFactoryTests
{
    [Theory]
    [InlineData("", "u", "p", "Neo4j Uri is required.")]
    [InlineData("not-a-uri", "u", "p", "Neo4j Uri format is invalid.")]
    [InlineData("bolt://localhost:7687", "", "p", "Neo4j Username is required.")]
    [InlineData("bolt://localhost:7687", "u", "", "Neo4j Password is required.")]
    public void Create_validates_options(string uri, string user, string pwd, string expectedMessage)
    {
        var factory = new Neo4jDriverFactory();
        var options = new Neo4jPersistenceOptions { Uri = uri, Username = user, Password = pwd };

        var ex = Should.Throw<ArgumentException>(() => factory.Create(options));
        ex.Message.ShouldContain(expectedMessage);
    }
}
