using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Text.Json;

namespace Aevatar.Agents.Runtime.Local;

/// <summary>
/// Local runtime Stream registry
/// Manages all Agent Message Streams
/// </summary>
public class LocalMessageStreamRegistry
{
    private readonly ConcurrentDictionary<string, LocalMessageStream> _streams = new();
    private static int _registryLogCount;

    /// <summary>
    /// Get or create Agent's Stream
    /// </summary>
    public LocalMessageStream GetOrCreateStream(string agentId, int capacity = 1000)
    {
        var exists = _streams.TryGetValue(agentId, out var existing);
        var stream = _streams.GetOrAdd(agentId, _ => new LocalMessageStream(agentId, capacity));

        if (Interlocked.Increment(ref _registryLogCount) <= 6)
        {
            // #region agent log
            System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                JsonSerializer.Serialize(new
                {
                    sessionId = string.Empty,
                    runId = string.Empty,
                    hypothesisId = "H9",
                    location = "LocalMessageStreamRegistry.cs:GetOrCreateStream",
                    message = "registry_stream_get_or_create",
                    data = new
                    {
                        agentId,
                        existed = exists && existing != null,
                        registryHash = RuntimeHelpers.GetHashCode(this),
                        streamHash = RuntimeHelpers.GetHashCode(stream),
                        streamId = stream.StreamId
                    },
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }) + Environment.NewLine);
            // #endregion
        }

        return stream;
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

        if (Interlocked.Increment(ref _registryLogCount) <= 6)
        {
            // #region agent log
            System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                JsonSerializer.Serialize(new
                {
                    sessionId = string.Empty,
                    runId = string.Empty,
                    hypothesisId = "H9",
                    location = "LocalMessageStreamRegistry.cs:GetStream",
                    message = "registry_stream_get",
                    data = new
                    {
                        agentId,
                        found = stream != null,
                        registryHash = RuntimeHelpers.GetHashCode(this),
                        streamHash = stream != null ? RuntimeHelpers.GetHashCode(stream) : 0,
                        streamId = stream?.StreamId ?? string.Empty
                    },
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }) + Environment.NewLine);
            // #endregion
        }

        return stream;
    }
}