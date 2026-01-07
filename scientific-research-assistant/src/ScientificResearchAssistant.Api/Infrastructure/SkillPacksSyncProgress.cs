using System.Text.Json.Serialization;

namespace ScientificResearchAssistant.Api.Infrastructure;

/// <summary>
/// In-memory progress + log reporter for SkillPacks sync.
/// <para/>
/// Purpose:
/// - Surface "what repo is being cloned/pulled right now" to the frontend.
/// - Keep a bounded log buffer for debugging without spamming SSE/chat streams.
/// <para/>
/// Thread-safety:
/// - All mutations are guarded by a single lock (sync runs are serialized anyway).
/// </summary>
public sealed class SkillPacksSyncProgress
{
    private const int MaxLogs = 200;

    private readonly object _lock = new();
    private readonly Queue<SkillPacksSyncLogEntry> _logs = new();

    private int _runSeq;
    private bool _running;
    private string _mode = string.Empty;
    private DateTimeOffset _startedAtUtc;
    private DateTimeOffset _updatedAtUtc;

    private string? _currentPackName;
    private string? _currentRepoUrl;
    private string? _currentRef;
    private string? _currentRepoDir;
    private string? _currentSkillsRoot;
    private string? _currentStep;
    private int _currentPackIndex;
    private int _totalPacks;

    public SkillPacksSyncProgressSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return new SkillPacksSyncProgressSnapshot
            {
                RunSeq = _runSeq,
                Running = _running,
                Mode = _mode,
                StartedAtUtc = _startedAtUtc,
                UpdatedAtUtc = _updatedAtUtc,
                Current = _running
                    ? new SkillPacksSyncCurrent
                    {
                        PackName = _currentPackName,
                        RepoUrl = _currentRepoUrl,
                        Ref = _currentRef,
                        RepoDir = _currentRepoDir,
                        SkillsRoot = _currentSkillsRoot,
                        Step = _currentStep,
                        PackIndex = _currentPackIndex,
                        TotalPacks = _totalPacks
                    }
                    : null,
                Logs = _logs.ToList()
            };
        }
    }

    public void StartRun(SkillPackSyncMode mode, int totalPacks)
    {
        lock (_lock)
        {
            _runSeq++;
            _running = true;
            _mode = mode.ToString();
            _totalPacks = Math.Max(0, totalPacks);
            _currentPackIndex = 0;
            _startedAtUtc = DateTimeOffset.UtcNow;
            _updatedAtUtc = _startedAtUtc;

            _currentPackName = null;
            _currentRepoUrl = null;
            _currentRef = null;
            _currentRepoDir = null;
            _currentSkillsRoot = null;
            _currentStep = null;

            _logs.Clear();
            Enqueue(new SkillPacksSyncLogEntry("info", "sync_start", $"Sync started ({_mode})."));
        }
    }

    public void FinishRun(bool ok, string? error = null)
    {
        lock (_lock)
        {
            _running = false;
            _updatedAtUtc = DateTimeOffset.UtcNow;
            _currentStep = null;

            var msg = ok ? "Sync finished (ok)." : $"Sync finished (failed): {error ?? "unknown"}";
            Enqueue(new SkillPacksSyncLogEntry(ok ? "info" : "warn", "sync_finish", msg));
        }
    }

    public void BeginPack(int index, SkillPackSpec spec, string repoDir, string skillsRoot)
    {
        ArgumentNullException.ThrowIfNull(spec);
        lock (_lock)
        {
            _updatedAtUtc = DateTimeOffset.UtcNow;
            _currentPackIndex = index;
            _currentPackName = spec.Name;
            _currentRepoUrl = spec.RepoUrl;
            _currentRef = spec.Ref;
            _currentRepoDir = repoDir;
            _currentSkillsRoot = skillsRoot;
            _currentStep = "pack_start";

            Enqueue(new SkillPacksSyncLogEntry(
                "info",
                "pack_start",
                $"[{index + 1}/{Math.Max(1, _totalPacks)}] {spec.Name}: start ({spec.Ref})"));
        }
    }

    public void Step(string step, string message)
    {
        lock (_lock)
        {
            _updatedAtUtc = DateTimeOffset.UtcNow;
            _currentStep = step;
            Enqueue(new SkillPacksSyncLogEntry("info", step, message));
        }
    }

    public void PackResult(SkillPackSyncEntry entry)
    {
        lock (_lock)
        {
            _updatedAtUtc = DateTimeOffset.UtcNow;
            var msg = entry.Ok
                ? $"[{entry.Name}] ok"
                : $"[{entry.Name}] failed: {entry.Error ?? "unknown"}";
            Enqueue(new SkillPacksSyncLogEntry(entry.Ok ? "info" : "warn", "pack_finish", msg));
        }
    }

    private void Enqueue(SkillPacksSyncLogEntry entry)
    {
        _logs.Enqueue(entry with { TimestampUtc = DateTimeOffset.UtcNow });
        while (_logs.Count > MaxLogs)
            _logs.Dequeue();
    }
}

public sealed class SkillPacksSyncProgressSnapshot
{
    public int RunSeq { get; init; }
    public bool Running { get; init; }
    public string Mode { get; init; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public SkillPacksSyncCurrent? Current { get; init; }
    public List<SkillPacksSyncLogEntry> Logs { get; init; } = new();
}

public sealed class SkillPacksSyncCurrent
{
    public string? PackName { get; init; }
    public string? RepoUrl { get; init; }
    public string? Ref { get; init; }
    public string? RepoDir { get; init; }
    public string? SkillsRoot { get; init; }
    public string? Step { get; init; }
    public int PackIndex { get; init; }
    public int TotalPacks { get; init; }
}

public sealed record SkillPacksSyncLogEntry(
    [property: JsonPropertyName("level")] string Level,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message)
{
    [JsonPropertyName("tsUtc")]
    public DateTimeOffset TimestampUtc { get; init; }
}


