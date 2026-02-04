using Aevatar.Agents.Abstractions.CQRS;
using Aevatar.Agents.AI.Abstractions.Tests.Fixtures;
using Aevatar.Agents.AI.Core;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Aevatar.Agents.AI.Core.Tests;

public class CqrsStateQueryInjectionTests(CqrsStateQueryInjectionFixture fixture)
    : IClassFixture<CqrsStateQueryInjectionFixture>
{
    [Fact(DisplayName = "GAgent factory injects IStateQueryService into AIGAgentBase when registered")]
    public void CreateGAgent_ShouldInject_IStateQueryService_IntoAIGAgentBase_WhenRegistered()
    {
        // Arrange
        var expected = fixture.ServiceProvider.GetRequiredService<IStateQueryService>();

        // Act
        var agent = fixture.GAgentFactory.CreateGAgent<CqrsAwareTestAgent>("agent-1");

        // Assert
        agent.ExposedCqrsStateQueryService.ShouldBeSameAs(expected);
    }

    private sealed class CqrsAwareTestAgent : AIGAgentBase
    {
        public IStateQueryService? ExposedCqrsStateQueryService => CqrsStateQueryService;

        public override Task<string> GetDescriptionAsync() => Task.FromResult("cqrs-aware-test-agent");
    }
}

public sealed class CqrsStateQueryInjectionFixture : AITestFixture
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.AddSingleton<IStateQueryService, FakeStateQueryService>();
    }

    private sealed class FakeStateQueryService : IStateQueryService
    {
        public Task<StateQueryResult?> GetByIdAsync(string agentType, string agentId, CancellationToken ct = default)
            => Task.FromResult<StateQueryResult?>(null);

        public Task<PagedStateQueryResult> QueryAsync(StateQuery query, CancellationToken ct = default)
            => Task.FromResult(new PagedStateQueryResult());

        public Task<long> CountAsync(string agentType, string? queryString = null, CancellationToken ct = default)
            => Task.FromResult(0L);
    }
}


