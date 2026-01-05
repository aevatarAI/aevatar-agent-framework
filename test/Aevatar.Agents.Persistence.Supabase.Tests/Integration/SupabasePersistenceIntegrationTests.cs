using Aevatar.Agents.Abstractions.EventRouting;
using Aevatar.Agents.Persistence.Supabase.GAgent.Stores;
using FluentAssertions;
using Xunit;

namespace Aevatar.Agents.Persistence.Supabase.Tests.Integration;

/// <summary>
/// Optional integration tests (real Postgres/Supabase).
/// 
/// Enable by setting env var:
/// - AEVATAR_SUPABASE_TEST_CONNECTION_STRING
/// </summary>
[Collection(nameof(SupabaseIntegrationCollection))]
public class SupabasePersistenceIntegrationTests
{
    private readonly SupabaseIntegrationFixture _fx;

    public SupabasePersistenceIntegrationTests(SupabaseIntegrationFixture fx)
    {
        _fx = fx;
    }

    [Fact]
    public async Task StateStore_ShouldSaveLoadVersionDelete()
    {
        if (!IsEnabled())
        {
            return;
        }

        var store = new SupabaseStateStore<StateStoreTestState>(_fx.DataSource!, _fx.Options!);

        var agentId = Guid.NewGuid().ToString("D");
        var state = new StateStoreTestState { Name = "n", Value = 42 };

        await store.SaveAsync(agentId, state, expectedVersion: 123);

        (await store.ExistsAsync(agentId)).Should().BeTrue();
        (await store.GetCurrentVersionAsync(agentId)).Should().Be(123);

        var loaded = await store.LoadAsync(agentId);
        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("n");
        loaded.Value.Should().Be(42);

        await store.DeleteAsync(agentId);
        (await store.ExistsAsync(agentId)).Should().BeFalse();
    }

    [Fact]
    public async Task ConfigStore_ShouldSaveLoadDelete()
    {
        if (!IsEnabled())
        {
            return;
        }

        var store = new SupabaseConfigStore<TestConfig>(_fx.DataSource!, _fx.Options!);

        var agentType = GetType();
        var agentId = Guid.NewGuid().ToString("D");
        var cfg = new TestConfig { Setting = "s1" };

        await store.SaveAsync(agentType, agentId, cfg);
        (await store.ExistsAsync(agentType, agentId)).Should().BeTrue();

        var loaded = await store.LoadAsync(agentType, agentId);
        loaded.Should().NotBeNull();
        loaded!.Setting.Should().Be("s1");

        await store.DeleteAsync(agentType, agentId);
        (await store.ExistsAsync(agentType, agentId)).Should().BeFalse();
    }

    [Fact]
    public async Task EventRouterStore_ShouldSaveLoadDelete()
    {
        if (!IsEnabled())
        {
            return;
        }

        var store = new SupabaseEventRouterStore(_fx.DataSource!, _fx.Options!);

        var agentId = Guid.NewGuid().ToString("D");
        var parentId = Guid.NewGuid().ToString("D");
        var childId = Guid.NewGuid().ToString("D");

        var hierarchy = new EventRouterHierarchy
        {
            ParentId = parentId,
            ChildrenIds = new HashSet<string> { childId }
        };

        await store.SaveAsync(agentId, hierarchy);

        var loaded = await store.LoadAsync(agentId);
        loaded.Should().NotBeNull();
        loaded!.ParentId.Should().Be(parentId);
        loaded.ChildrenIds.Should().Contain(childId);

        await store.DeleteAsync(agentId);
        (await store.ExistsAsync(agentId)).Should().BeFalse();
    }

    private bool IsEnabled() => _fx.IsEnabled;
}


