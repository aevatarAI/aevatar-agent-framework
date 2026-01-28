using System.Collections.Concurrent;

namespace Aevatar.VibeResearching.Agents.Pivot;

/// <summary>
/// In-memory pivot status tracker.
/// Tracks per-session pivot operation status using ConcurrentDictionary.
/// </summary>
public sealed class InMemoryPivotStatusService : IPivotStatusService
{
    private sealed record StatusEntry(PivotStatus Status, string? Message, DateTime UpdatedAt);

    private readonly ConcurrentDictionary<string, StatusEntry> _store = new(StringComparer.Ordinal);

    public Task<object> GetStatusAsync(string sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        sessionId = (sessionId ?? string.Empty).Trim();

        if (_store.TryGetValue(sessionId, out var entry))
        {
            return Task.FromResult<object>(new
            {
                sessionId,
                status = entry.Status.ToString(),
                message = entry.Message ?? string.Empty,
                updatedAt = entry.UpdatedAt.ToString("O")
            });
        }

        return Task.FromResult<object>(new
        {
            sessionId,
            status = "none",
            message = string.Empty,
            updatedAt = DateTime.UtcNow.ToString("O")
        });
    }

    public Task UpdateStatusAsync(string sessionId, PivotStatus status, string? message, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        sessionId = (sessionId ?? string.Empty).Trim();

        _store[sessionId] = new StatusEntry(status, message, DateTime.UtcNow);
        return Task.CompletedTask;
    }

    public Task ClearStatusAsync(string sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        sessionId = (sessionId ?? string.Empty).Trim();

        _store.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }
}
