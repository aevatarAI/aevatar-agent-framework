using System.Collections.Concurrent;
using System.Security.Cryptography;
using Aevatar.Novel.Contracts;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Novel.Sidecar.Services;

// ============================================================
//  ProjectFileWatcherHostedService
//
//  PURPOSE:
//  - Listen to SSOT file changes under ProjectRoot:
//    - Chapter text: *.txt (plain text, no markdown output requirement applies to system)
//    - Artifacts:    *.md
//  - Emit debounced "file changed" signals for downstream pipeline:
//    - Deviation analysis (diff)
//    - Narrative tests
//    - Session log updates
//    - SQLite index refresh
//
//  NOTE:
//  - This is an initial skeleton focused on correctness.
//  - We will later connect these signals to Aevatar events + Cognitive Mesh workflows.
// ============================================================

public sealed class ProjectFileWatcherHostedService : BackgroundService
{
    private readonly ILogger<ProjectFileWatcherHostedService> _logger;
    private readonly ProjectRootManager _projectRoot;
    private readonly SidecarEventHub _eventHub;

    // Keep a tiny in-memory snapshot to avoid treating duplicate events as real changes.
    private readonly ConcurrentDictionary<string, string> _fileHash = new(StringComparer.OrdinalIgnoreCase);

    public ProjectFileWatcherHostedService(
        ILogger<ProjectFileWatcherHostedService> logger,
        ProjectRootManager projectRoot,
        SidecarEventHub eventHub)
    {
        _logger = logger;
        _projectRoot = projectRoot;
        _eventHub = eventHub;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Best-effort loop: re-evaluate root periodically (v1).
        // In later iterations we'll support dynamic root switching via API and change tokens.
        while (!stoppingToken.IsCancellationRequested)
        {
            var root = _projectRoot.GetProjectRoot();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                continue;
            }

            using var watcher = CreateWatcher(root);

            _logger.LogInformation("[Novel] Watching SSOT directory: {Root}", root);

            try
            {
                // Hold until root changes or cancellation.
                while (!stoppingToken.IsCancellationRequested)
                {
                    var current = _projectRoot.GetProjectRoot();
                    if (!string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
                        break;

                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
            finally
            {
                _logger.LogInformation("[Novel] Stop watching: {Root}", root);
            }
        }
    }

    private FileSystemWatcher CreateWatcher(string root)
    {
        var watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };

        watcher.Changed += (_, e) => OnFileChanged(root, e.FullPath, FileChangeKind.Changed);
        watcher.Created += (_, e) => OnFileChanged(root, e.FullPath, FileChangeKind.Created);
        watcher.Deleted += (_, e) => OnFileDeleted(root, e.FullPath);
        watcher.Renamed += (_, e) => OnFileRenamed(root, e.OldFullPath, e.FullPath);

        return watcher;
    }

    private void OnFileChanged(string root, string fullPath, FileChangeKind kind)
    {
        // Quick filters to avoid noise.
        if (!IsInterestingFile(fullPath))
            return;

        // NOTE: editors may raise multiple events; we hash content to dedupe.
        var hash = TryHashFile(fullPath);
        if (hash is null)
            return;

        var prev = _fileHash.GetOrAdd(fullPath, _ => string.Empty);
        if (string.Equals(prev, hash, StringComparison.Ordinal))
            return;

        _fileHash[fullPath] = hash;

        var rel = TryGetRelativePath(root, fullPath);
        _logger.LogInformation("[Novel] SSOT changed: {Path}", fullPath);

        _eventHub.Publish(new SidecarEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            FileChanged = new SstFileChangedEvent
            {
                EventId = Guid.NewGuid().ToString("N"),
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                ProjectRoot = root,
                FullPath = fullPath,
                RelativePath = rel,
                Kind = kind
            }
        });
    }

    private void OnFileDeleted(string root, string fullPath)
    {
        if (!IsInterestingFile(fullPath))
            return;

        _fileHash.TryRemove(fullPath, out _);

        var rel = TryGetRelativePath(root, fullPath);
        _logger.LogInformation("[Novel] SSOT deleted: {Path}", fullPath);

        _eventHub.Publish(new SidecarEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            FileChanged = new SstFileChangedEvent
            {
                EventId = Guid.NewGuid().ToString("N"),
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                ProjectRoot = root,
                FullPath = fullPath,
                RelativePath = rel,
                Kind = FileChangeKind.Deleted
            }
        });
    }

    private void OnFileRenamed(string root, string oldFullPath, string fullPath)
    {
        if (!IsInterestingFile(fullPath) && !IsInterestingFile(oldFullPath))
            return;

        // Update hash mapping for the new path if possible.
        _fileHash.TryRemove(oldFullPath, out _);
        var hash = TryHashFile(fullPath);
        if (hash is not null)
        {
            _fileHash[fullPath] = hash;
        }

        var rel = TryGetRelativePath(root, fullPath);
        var oldRel = TryGetRelativePath(root, oldFullPath);
        _logger.LogInformation("[Novel] SSOT renamed: {Old} -> {New}", oldFullPath, fullPath);

        _eventHub.Publish(new SidecarEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            FileChanged = new SstFileChangedEvent
            {
                EventId = Guid.NewGuid().ToString("N"),
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                ProjectRoot = root,
                FullPath = fullPath,
                RelativePath = rel,
                OldFullPath = oldFullPath,
                OldRelativePath = oldRel,
                Kind = FileChangeKind.Renamed
            }
        });
    }

    private static bool IsInterestingFile(string fullPath)
    {
        var ext = Path.GetExtension(fullPath);
        if (!ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".md", StringComparison.OrdinalIgnoreCase))
            return false;

        // Ignore index dir and common build outputs.
        if (fullPath.Contains($"{Path.DirectorySeparatorChar}.index{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            return false;
        if (fullPath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            return false;
        if (fullPath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            return false;

        // Ignore temp files (best-effort).
        var name = Path.GetFileName(fullPath);
        if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            return false;
        if (name.StartsWith("~", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static string TryGetRelativePath(string root, string fullPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(root))
                return string.Empty;

            var relative = Path.GetRelativePath(root, fullPath);
            // If it's outside the root, Path.GetRelativePath will contain "..".
            if (relative.StartsWith("..", StringComparison.Ordinal))
                return string.Empty;

            return relative.Replace('\\', '/');
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string? TryHashFile(string fullPath)
    {
        try
        {
            if (!File.Exists(fullPath))
                return null;

            using var stream = File.Open(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(stream);
            return Convert.ToHexString(bytes);
        }
        catch
        {
            // Best-effort: file might be mid-write or locked.
            return null;
        }
    }
}


