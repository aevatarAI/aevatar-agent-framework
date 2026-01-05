using System.Collections.Concurrent;

namespace Aevatar.Agents.Runtime.Local;

/// <summary>
/// Local runtime Stream registry
/// Manages all Agent Message Streams
/// </summary>
public class LocalMessageStreamRegistry
{
    private readonly ConcurrentDictionary<string, LocalMessageStream> _streams = new();

    /// <summary>
    /// Get or create Agent's Stream
    /// </summary>
    public LocalMessageStream GetOrCreateStream(string agentId, int capacity = 1000)
    {
        return _streams.GetOrAdd(agentId, _ => new LocalMessageStream(agentId, capacity));
    }

    /// <summary>
    /// Check if Stream already exists
    /// </summary>
    public bool StreamExists(string agentId)
    {
        return _streams.ContainsKey(agentId);
    }

    /// <summary>
    /// Remove Agent's Stream
    /// </summary>
    public void RemoveStream(string agentId)
    {
        if (_streams.TryRemove(agentId, out var stream))
        {
            stream.Stop();
        }
    }

    /// <summary>
    /// Get Stream (if exists)
    /// </summary>
    public LocalMessageStream? GetStream(string agentId)
    {
        _streams.TryGetValue(agentId, out var stream);
        return stream;
    }
}