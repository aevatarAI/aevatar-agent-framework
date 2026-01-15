using System.Collections.Concurrent;

namespace AevatarKit.Runtime.Run;

/// <summary>
/// In-memory registry for runs (MVP).
/// </summary>
public interface IRunRegistry
{
    RunInstance Create(string? sessionId, string? graphId, string? graphName, string? graphSource, string? input);
    bool TryGet(string runId, out RunInstance run);
    IReadOnlyList<RunInstance> List();
}

public sealed class InMemoryRunRegistry : IRunRegistry
{
    private readonly ConcurrentDictionary<string, RunInstance> _runs = new();

    public RunInstance Create(string? sessionId, string? graphId, string? graphName, string? graphSource, string? input)
    {
        var runId = Guid.NewGuid().ToString("N");
        var run = new RunInstance
        {
            RunId = runId,
            CreatedAtUtc = DateTime.UtcNow,
            SessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId.Trim(),
            GraphId = string.IsNullOrWhiteSpace(graphId) ? null : graphId.Trim(),
            GraphName = string.IsNullOrWhiteSpace(graphName) ? null : graphName.Trim(),
            GraphSource = graphSource,
            Input = input
        };

        _runs[runId] = run;
        return run;
    }

    public bool TryGet(string runId, out RunInstance run)
    {
        return _runs.TryGetValue(runId, out run!);
    }

    public IReadOnlyList<RunInstance> List()
    {
        return _runs.Values
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToList();
    }
}


