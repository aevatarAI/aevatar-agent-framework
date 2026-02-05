using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace Aevatar.VibeResearching.UserProviders.Infrastructure.Codex;

/// <summary>
/// Stores PKCE state + code_verifier for the Codex OAuth flow.
/// Uses IDistributedCache with 5-minute TTL.
/// State entries are single-use (deleted after callback).
/// </summary>
public sealed class PkceStateStore
{
    private readonly IDistributedCache _cache;
    private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(5);
    private const string KeyPrefix = "codex:pkce:";

    public PkceStateStore(IDistributedCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    /// <summary>
    /// Stores a PKCE state entry with 5-minute TTL.
    /// </summary>
    public async Task StoreAsync(string state, PkceStateEntry entry, CancellationToken ct = default)
    {
        var key = KeyPrefix + state;
        var json = JsonSerializer.Serialize(entry);
        await _cache.SetStringAsync(key, json, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = StateTtl
        }, ct);
    }

    /// <summary>
    /// Reads a PKCE state entry without consuming it. Returns null if not found or expired.
    /// Used by the callback listener to extract userId before the full flow.
    /// </summary>
    public async Task<PkceStateEntry?> PeekAsync(string state, CancellationToken ct = default)
    {
        var key = KeyPrefix + state;
        var json = await _cache.GetStringAsync(key, ct);
        if (string.IsNullOrWhiteSpace(json))
            return null;

        return JsonSerializer.Deserialize<PkceStateEntry>(json);
    }

    /// <summary>
    /// Consumes (retrieves and deletes) a PKCE state entry. Returns null if not found or expired.
    /// </summary>
    public async Task<PkceStateEntry?> ConsumeAsync(string state, CancellationToken ct = default)
    {
        var key = KeyPrefix + state;
        var json = await _cache.GetStringAsync(key, ct);

        if (string.IsNullOrWhiteSpace(json))
            return null;

        // Delete immediately (one-time use)
        await _cache.RemoveAsync(key, ct);
        return JsonSerializer.Deserialize<PkceStateEntry>(json);
    }
}

/// <summary>
/// PKCE state entry stored in cache during OAuth flow.
/// </summary>
public sealed record PkceStateEntry(
    Guid UserId,
    string CodeVerifier,
    DateTimeOffset CreatedAt);
