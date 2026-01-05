using Proto;

namespace Aevatar.Agents.Runtime.ProtoActor;

/// <summary>
/// Proto.Actor runtime Stream registry
/// Manages all Agent PIDs and Streams
/// </summary>
public class ProtoActorMessageStreamRegistry
{
    private readonly Dictionary<string, PID> _pidRegistry = new();
    private readonly Dictionary<string, ProtoActorMessageStream> _streamRegistry = new();
    private readonly IRootContext _rootContext;
    private readonly Lock _lock = new();

    public ProtoActorMessageStreamRegistry(IRootContext rootContext)
    {
        _rootContext = rootContext;
    }

    /// <summary>
    /// Register Agent's PID
    /// </summary>
    public void RegisterPid(string agentId, PID pid)
    {
        lock (_lock)
        {
            _pidRegistry[agentId] = pid;
            _streamRegistry[agentId] = new ProtoActorMessageStream(agentId, pid, _rootContext);
        }
    }

    /// <summary>
    /// Get Agent's Stream
    /// </summary>
    public ProtoActorMessageStream? GetStream(string agentId)
    {
        lock (_lock)
        {
            _streamRegistry.TryGetValue(agentId, out var stream);
            return stream;
        }
    }

    /// <summary>
    /// Get Agent's PID
    /// </summary>
    public PID? GetPid(string agentId)
    {
        lock (_lock)
        {
            _pidRegistry.TryGetValue(agentId, out var pid);
            return pid;
        }
    }

    /// <summary>
    /// Remove Agent
    /// </summary>
    public void Remove(string agentId)
    {
        lock (_lock)
        {
            _pidRegistry.Remove(agentId);
            _streamRegistry.Remove(agentId);
        }
    }

    /// <summary>
    /// Check if exists
    /// </summary>
    public bool Exists(string agentId)
    {
        lock (_lock)
        {
            return _pidRegistry.ContainsKey(agentId);
        }
    }
}
