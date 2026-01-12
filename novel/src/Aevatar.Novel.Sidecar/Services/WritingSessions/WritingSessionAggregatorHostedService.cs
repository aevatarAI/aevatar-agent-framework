using System.Collections.Concurrent;
using System.Text;
using Aevatar.Novel.Contracts;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Novel.Sidecar.Services.WritingSessions;

// ============================================================
//  WritingSessionAggregatorHostedService (v1)
//
//  Workflow F (session logs):
//  - Aggregate a burst of changes into a "writing session" per story.
//  - Persist:
//    - sessions/<session_id>/writing_session_log.md
//    - sessions/<session_id>/author_intent_prompt.md (v1 stub, still useful for prompting)
//  - Emit SidecarEvent(writing_session_log_updated=...) so UI updates immediately.
//
//  NOTES:
//  - Sessions are derived artifacts (SSOT is still chapter .txt + core .md assets).
//  - We keep it simple: session boundary by inactivity timeout.
// ============================================================

public sealed class WritingSessionAggregatorHostedService : BackgroundService
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);

    private readonly ILogger<WritingSessionAggregatorHostedService> _logger;
    private readonly SidecarEventHub _hub;
    private readonly ProjectRootManager _projectRoot;

    private readonly ConcurrentDictionary<string, SessionState> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public WritingSessionAggregatorHostedService(
        ILogger<WritingSessionAggregatorHostedService> logger,
        SidecarEventHub hub,
        ProjectRootManager projectRoot)
    {
        _logger = logger;
        _hub = hub;
        _projectRoot = projectRoot;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = _hub.Subscribe(stoppingToken);
        await foreach (var evt in reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                switch (evt.PayloadCase)
                {
                    case SidecarEvent.PayloadOneofCase.FileChanged:
                        await OnFileChangedAsync(evt.FileChanged, stoppingToken);
                        break;
                    case SidecarEvent.PayloadOneofCase.UnitTestsCompleted:
                        await OnUnitTestsCompletedAsync(evt.UnitTestsCompleted, stoppingToken);
                        break;
                    case SidecarEvent.PayloadOneofCase.DeviationImpactCompleted:
                        await OnDeviationImpactCompletedAsync(evt.DeviationImpactCompleted, stoppingToken);
                        break;
                    case SidecarEvent.PayloadOneofCase.CanonChangeRecorded:
                        await OnCanonChangeRecordedAsync(evt.CanonChangeRecorded, stoppingToken);
                        break;
                    case SidecarEvent.PayloadOneofCase.SetupPayoffScanCompleted:
                        await OnSetupPayoffScanCompletedAsync(evt.SetupPayoffScanCompleted, stoppingToken);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Novel] Writing session aggregation failed for event {EventId}", evt.EventId);
            }
        }
    }

    private async Task OnFileChangedAsync(SstFileChangedEvent fc, CancellationToken ct)
    {
        // Only aggregate "meaningful author actions" (avoid derived noise).
        var normalized = (fc.FullPath ?? string.Empty).Replace('\\', '/');
        if (normalized.Contains("/sessions/", StringComparison.OrdinalIgnoreCase))
            return;
        if (normalized.Contains("/artifacts/", StringComparison.OrdinalIgnoreCase) &&
            !normalized.EndsWith("/artifacts/tests/narrative_tests.md", StringComparison.OrdinalIgnoreCase) &&
            !normalized.EndsWith("/artifacts/ledger/setup_payoff_ledger.md", StringComparison.OrdinalIgnoreCase))
            return;

        // Only care about chapter edits and test definition edits.
        if (!(normalized.Contains("/chapters/", StringComparison.OrdinalIgnoreCase) && normalized.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) &&
            !normalized.EndsWith("/artifacts/tests/narrative_tests.md", StringComparison.OrdinalIgnoreCase) &&
            !normalized.EndsWith("/artifacts/ledger/setup_payoff_ledger.md", StringComparison.OrdinalIgnoreCase))
            return;

        var storyRoot = TryGetStoryRoot(normalized);
        if (string.IsNullOrWhiteSpace(storyRoot) || !Directory.Exists(storyRoot))
            return;

        var storyId = Path.GetFileName(storyRoot);
        var session = GetOrStartSession(storyId, storyRoot);

        session.LastActivityUtc = DateTime.UtcNow;

        var rel = string.IsNullOrWhiteSpace(fc.RelativePath) ? normalized : fc.RelativePath;
        session.ChangedFiles.Add(rel);

        await PersistAsync(session, $"File changed: `{rel}` ({fc.Kind})", ct);
    }

    private async Task OnUnitTestsCompletedAsync(UnitTestsCompletedEvent ev, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ev.StoryId))
            return;

        // Derive storyRoot from report path (file://.../artifacts/tests/...).
        var storyRoot = TryGetStoryRootFromReport(ev.TestReport?.Uri);
        if (string.IsNullOrWhiteSpace(storyRoot) || !Directory.Exists(storyRoot))
            return;

        var session = GetOrStartSession(ev.StoryId, storyRoot);
        session.LastActivityUtc = DateTime.UtcNow;

        session.LastTestStatus = ev.Summary?.Status.ToString() ?? "";
        session.LastTestReportUri = ev.TestReport?.Uri ?? "";

        var title = ev.Summary?.Status == NarrativeTestRunStatus.Failed
            ? $"Narrative Tests FAILED ({ev.Summary.Failures.Count} failures)"
            : "Narrative Tests PASSED";

        await PersistAsync(session, $"{title} → `{ev.TestReport?.Uri}`", ct);
    }

    private async Task OnDeviationImpactCompletedAsync(StoryDeviationImpactAnalysisCompletedEvent ev, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ev.StoryId))
            return;

        // Derive storyRoot from report uri.
        var storyRoot = TryGetStoryRootFromReport(ev.DeviationReport?.Uri)
                        ?? TryGetStoryRootFromReport(ev.ChangeImpactReport?.Uri)
                        ?? TryGetStoryRootFromReport(ev.BackupOptionsReport?.Uri);
        if (string.IsNullOrWhiteSpace(storyRoot) || !Directory.Exists(storyRoot))
            return;

        var session = GetOrStartSession(ev.StoryId, storyRoot);
        session.LastActivityUtc = DateTime.UtcNow;

        var itemCount = ev.DeviationSummary?.Items?.Count ?? 0;
        var headline = $"Deviation Impact Completed (items={itemCount}, base={ev.BaseRevision}, edited={ev.EditedRevision})";

        await PersistAsync(session,
            $"{headline} → `{ev.DeviationReport?.Uri}`",
            ct);
    }

    private async Task OnCanonChangeRecordedAsync(CanonChangeRecordedEvent ev, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ev.StoryId))
            return;

        var storyRoot = TryGetStoryRootFromReport(ev.CanonChangeRecord?.Uri);
        if (string.IsNullOrWhiteSpace(storyRoot) || !Directory.Exists(storyRoot))
            return;

        var session = GetOrStartSession(ev.StoryId, storyRoot);
        session.LastActivityUtc = DateTime.UtcNow;

        var headline = $"Canon change recorded: `{ev.ChangedRelativePath}`";
        await PersistAsync(session, $"{headline} → `{ev.CanonChangeRecord?.Uri}`", ct);
    }

    private async Task OnSetupPayoffScanCompletedAsync(SetupPayoffLedgerScanCompletedEvent ev, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ev.StoryId))
            return;

        var storyRoot = TryGetStoryRootFromReport(ev.Report?.Uri);
        if (string.IsNullOrWhiteSpace(storyRoot) || !Directory.Exists(storyRoot))
            return;

        var session = GetOrStartSession(ev.StoryId, storyRoot);
        session.LastActivityUtc = DateTime.UtcNow;

        var headline = $"Setup/Payoff scan completed (open={ev.OpenCount}, paid={ev.PaidCount}, broken={ev.BrokenCount}, dueSoon={ev.DueSoonCount})";
        await PersistAsync(session, $"{headline} → `{ev.Report?.Uri}`", ct);
    }

    private SessionState GetOrStartSession(string storyId, string storyRoot)
    {
        var now = DateTime.UtcNow;

        return _sessions.AddOrUpdate(
            storyId,
            _ => new SessionState(storyId, storyRoot, NewSessionId(), now),
            (_, existing) =>
            {
                if (now - existing.LastActivityUtc > IdleTimeout)
                {
                    return new SessionState(storyId, storyRoot, NewSessionId(), now);
                }

                // Keep existing session id, but storyRoot might move (best-effort).
                existing.StoryRoot = storyRoot;
                return existing;
            });
    }

    private async Task PersistAsync(SessionState state, string headline, CancellationToken ct)
    {
        var sessionDir = Path.Combine(state.StoryRoot, "sessions", state.SessionId);
        Directory.CreateDirectory(sessionDir);

        var logPath = Path.Combine(sessionDir, "writing_session_log.md");
        var promptPath = Path.Combine(sessionDir, "author_intent_prompt.md");

        // Build log (v1: overwrite full file; small and safe).
        var log = BuildSessionLog(state, headline);
        await AtomicWriteAsync(logPath, log, ct);

        var prompt = BuildAuthorIntentPrompt(state);
        await AtomicWriteAsync(promptPath, prompt, ct);

        var session = new WritingSession
        {
            SessionId = state.SessionId,
            Kind = WritingSessionKind.Auto,
            StartedAt = Timestamp.FromDateTime(state.StartedAtUtc),
            ProjectId = _projectRoot.GetProjectRoot(),
            StoryId = state.StoryId,
            Title = "Auto session"
        };

        var logRef = new ArtifactRef
        {
            ArtifactId = state.SessionId,
            Kind = ArtifactKind.WritingSessionLog,
            Title = "Writing Session Log",
            Uri = $"file://{logPath.Replace('\\', '/')}"
        };

        var promptRef = new ArtifactRef
        {
            ArtifactId = state.SessionId,
            Kind = ArtifactKind.AuthorIntentPrompt,
            Title = "Author Intent Prompt",
            Uri = $"file://{promptPath.Replace('\\', '/')}"
        };

        _hub.Publish(new SidecarEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            WritingSessionLogUpdated = new WritingSessionLogUpdatedEvent
            {
                Session = session,
                SessionLog = logRef,
                AuthorIntentPrompt = promptRef,
                UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            }
        });
    }

    private static string BuildSessionLog(SessionState state, string headline)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Writing Session Log");
        sb.AppendLine();
        sb.AppendLine($"- **session_id**: `{state.SessionId}`");
        sb.AppendLine($"- **story_id**: `{state.StoryId}`");
        sb.AppendLine($"- **started_at**: `{state.StartedAtUtc:O}`");
        sb.AppendLine($"- **last_activity**: `{state.LastActivityUtc:O}`");
        sb.AppendLine();

        sb.AppendLine("## Latest Event");
        sb.AppendLine();
        sb.AppendLine($"- `{DateTime.UtcNow:O}` {headline}");
        sb.AppendLine();

        if (state.ChangedFiles.Count > 0)
        {
            sb.AppendLine("## Changed Files (session)");
            foreach (var f in state.ChangedFiles.Take(50))
            {
                sb.AppendLine($"- `{f}`");
            }
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(state.LastTestStatus))
        {
            sb.AppendLine("## Narrative Tests (latest)");
            sb.AppendLine();
            sb.AppendLine($"- **status**: `{state.LastTestStatus}`");
            if (!string.IsNullOrWhiteSpace(state.LastTestReportUri))
                sb.AppendLine($"- **report**: `{state.LastTestReportUri}`");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string BuildAuthorIntentPrompt(SessionState state)
    {
        // v1: not a semantic deviation summary yet; still useful as a stable "session context" prompt.
        var sb = new StringBuilder();
        sb.AppendLine("You are continuing a novel writing session.");
        sb.AppendLine("Treat chapter .txt and core .md assets as SSOT (file wins).");
        sb.AppendLine();
        sb.AppendLine("Recent changes (session):");
        foreach (var f in state.ChangedFiles.Take(20))
        {
            sb.AppendLine($"- {f}");
        }
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(state.LastTestStatus))
        {
            sb.AppendLine("Latest narrative tests:");
            sb.AppendLine($"- status: {state.LastTestStatus}");
            if (!string.IsNullOrWhiteSpace(state.LastTestReportUri))
                sb.AppendLine($"- report: {state.LastTestReportUri}");
        }

        sb.AppendLine();
        sb.AppendLine("When generating future chapters or updating outline, ensure all narrative tests pass or propose a concrete fix roadmap.");
        return sb.ToString();
    }

    private static async Task AtomicWriteAsync(string path, string content, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        var tmp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tmp, content, Encoding.UTF8, ct);
        File.Move(tmp, path, overwrite: true);
    }

    private static string NewSessionId() => Guid.NewGuid().ToString("N");

    private static string? TryGetStoryRoot(string normalizedFullPath)
    {
        // normalizedFullPath uses '/'.
        var chaptersIdx = normalizedFullPath.LastIndexOf("/chapters/", StringComparison.OrdinalIgnoreCase);
        if (chaptersIdx >= 0)
        {
            var chaptersDir = normalizedFullPath[..(chaptersIdx + "/chapters".Length)];
            return Directory.GetParent(chaptersDir)?.FullName;
        }

        var artifactsIdx = normalizedFullPath.LastIndexOf("/artifacts/", StringComparison.OrdinalIgnoreCase);
        if (artifactsIdx >= 0)
        {
            return normalizedFullPath[..artifactsIdx].TrimEnd('/');
        }

        return null;
    }

    private static string? TryGetStoryRootFromReport(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        var raw = uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            ? uri["file://".Length..]
            : uri;

        var normalized = raw.Replace('\\', '/');
        var artifactsIdx = normalized.LastIndexOf("/artifacts/", StringComparison.OrdinalIgnoreCase);
        if (artifactsIdx < 0)
            return null;

        return normalized[..artifactsIdx].TrimEnd('/');
    }

    private sealed class SessionState
    {
        public SessionState(string storyId, string storyRoot, string sessionId, DateTime startedAtUtc)
        {
            StoryId = storyId;
            StoryRoot = storyRoot;
            SessionId = sessionId;
            StartedAtUtc = startedAtUtc;
            LastActivityUtc = startedAtUtc;
        }

        public string StoryId { get; }
        public string StoryRoot { get; set; }
        public string SessionId { get; }

        public DateTime StartedAtUtc { get; }
        public DateTime LastActivityUtc { get; set; }

        public HashSet<string> ChangedFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string LastTestStatus { get; set; } = string.Empty;
        public string LastTestReportUri { get; set; } = string.Empty;
    }
}


