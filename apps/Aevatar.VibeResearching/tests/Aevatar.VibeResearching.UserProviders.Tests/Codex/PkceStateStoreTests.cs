using Aevatar.VibeResearching.UserProviders.Infrastructure.Codex;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Xunit;

namespace Aevatar.VibeResearching.UserProviders.Tests.Codex;

/// <summary>
/// Unit tests for the PKCE state store.
/// Tests store, consume, single-use, and expiry behavior.
/// </summary>
public sealed class PkceStateStoreTests
{
    private static PkceStateStore CreateStore()
    {
        var cache = new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions()));
        return new PkceStateStore(cache);
    }

    [Fact]
    public async Task StoreAndConsume_ReturnsStoredEntry()
    {
        // Arrange
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var entry = new PkceStateEntry(userId, "verifier123", DateTimeOffset.UtcNow);

        // Act
        await store.StoreAsync("state1", entry);
        var result = await store.ConsumeAsync("state1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(userId, result.UserId);
        Assert.Equal("verifier123", result.CodeVerifier);
    }

    [Fact]
    public async Task ConsumeAsync_SingleUse_SecondConsumeReturnsNull()
    {
        // Arrange
        var store = CreateStore();
        var entry = new PkceStateEntry(Guid.NewGuid(), "verifier", DateTimeOffset.UtcNow);
        await store.StoreAsync("state2", entry);

        // Act
        var first = await store.ConsumeAsync("state2");
        var second = await store.ConsumeAsync("state2");

        // Assert
        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public async Task ConsumeAsync_NonexistentState_ReturnsNull()
    {
        // Arrange
        var store = CreateStore();

        // Act
        var result = await store.ConsumeAsync("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task StoreAsync_MultipleStates_IndependentConsumption()
    {
        // Arrange
        var store = CreateStore();
        var entry1 = new PkceStateEntry(Guid.NewGuid(), "v1", DateTimeOffset.UtcNow);
        var entry2 = new PkceStateEntry(Guid.NewGuid(), "v2", DateTimeOffset.UtcNow);

        await store.StoreAsync("s1", entry1);
        await store.StoreAsync("s2", entry2);

        // Act
        var r1 = await store.ConsumeAsync("s1");
        var r2 = await store.ConsumeAsync("s2");

        // Assert
        Assert.NotNull(r1);
        Assert.Equal("v1", r1.CodeVerifier);
        Assert.NotNull(r2);
        Assert.Equal("v2", r2.CodeVerifier);
    }

    [Fact]
    public async Task ConsumeAsync_ExpiredState_ReturnsNull()
    {
        // Arrange -- use a cache with very short absolute expiration
        var cacheOptions = Options.Create(new MemoryDistributedCacheOptions());
        var cache = new MemoryDistributedCache(cacheOptions);

        // Store with a 1-millisecond TTL by using the raw cache directly
        var entry = new PkceStateEntry(Guid.NewGuid(), "expired-verifier", DateTimeOffset.UtcNow);
        var json = System.Text.Json.JsonSerializer.Serialize(entry);
        await cache.SetStringAsync("codex:pkce:expired-state", json, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(1)
        });

        // Wait for the entry to expire
        await Task.Delay(50);

        // Act -- wrap the expired-cache entry through the PkceStateStore interface
        var store = new PkceStateStore(cache);
        var result = await store.ConsumeAsync("expired-state");

        // Assert
        Assert.Null(result);
    }
}
