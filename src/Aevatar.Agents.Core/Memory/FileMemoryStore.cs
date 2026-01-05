using System.Collections.Concurrent;
using System.Text.Json;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.Core.Tracing;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Core.Memory;

// ============================================================
//  FileMemoryStore
//
//  Directory Layout (Memory Bundle v1):
//
//  ${AEVATAR_MEMORY_DIR}/
//    <memoryId>/
//      entries.pb        # Protobuf delimited stream (append-only)
//      manifest.json     # Bundle metadata for listing
//
//  NOTES:
//  - If env var AEVATAR_MEMORY_DIR is not set, we fall back to "<repoRoot>/memory"
//    (repo root detected via the same heuristic as FileExecutionTraceStore).
//  - Best-effort: callers should wrap with try/catch and/or use NullMemoryStore fallback.
// ============================================================
public sealed class FileMemoryStore : IMemoryStore
{
    public const string EntriesBinaryFileName = "entries.pb";
    public const string ManifestFileName = "manifest.json";
    public const string DefaultMemoryDirectoryName = "memory";

    private readonly string _rootDir;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public FileMemoryStore(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("Memory root directory cannot be empty.", nameof(rootDirectory));

        _rootDir = Path.GetFullPath(rootDirectory.Trim());
    }

    public async Task AppendAsync(MemoryEntry entry, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entry);

        var memoryId = entry.MemoryId?.Trim();
        if (string.IsNullOrWhiteSpace(memoryId))
            throw new ArgumentException("MemoryEntry.memory_id cannot be empty.", nameof(entry));

        // Ensure mandatory fields are present (best-effort defaults).
        if (string.IsNullOrWhiteSpace(entry.EntryId))
            entry.EntryId = Guid.NewGuid().ToString("N");

        entry.CreatedAt ??= Timestamp.FromDateTime(DateTime.UtcNow);

        entry.Scope ??= new MemoryScope { Type = MemoryScopeType.Unspecified, ScopeId = string.Empty };

        var sem = _locks.GetOrAdd(memoryId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            var bundleDir = GetBundleDirectory(_rootDir, memoryId);
            Directory.CreateDirectory(bundleDir);

            var entriesPath = Path.Combine(bundleDir, EntriesBinaryFileName);
            await using (var fs = new FileStream(entriesPath, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                entry.WriteDelimitedTo(fs);
                await fs.FlushAsync(ct);
            }

            // Update manifest (best-effort).
            var manifestPath = Path.Combine(bundleDir, ManifestFileName);
            var exportedAt = DateTimeOffset.UtcNow;
            var existing = TryReadManifest(manifestPath);

            var next = new MemoryBundleManifest
            {
                SchemaVersion = 1,
                MemoryId = memoryId,
                ScopeType = entry.Scope.Type.ToString(),
                ScopeId = entry.Scope.ScopeId ?? string.Empty,
                EntryCount = (existing?.EntryCount ?? 0) + 1,
                LatestAtUtc = exportedAt,
                Files = new MemoryBundleFiles
                {
                    EntriesBinary = EntriesBinaryFileName,
                    Manifest = ManifestFileName
                }
            };

            var json = JsonSerializer.Serialize(next, _jsonOptions);
            await File.WriteAllTextAsync(manifestPath, json, ct);
        }
        finally
        {
            sem.Release();
        }
    }

    public Task<IReadOnlyList<MemoryResourceSummary>> ListResourcesAsync(
        MemoryScopeType? scopeTypeFilter = null,
        int limit = 200,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (limit <= 0)
            return Task.FromResult<IReadOnlyList<MemoryResourceSummary>>([]);

        if (!Directory.Exists(_rootDir))
            return Task.FromResult<IReadOnlyList<MemoryResourceSummary>>([]);

        var dirs = Directory.GetDirectories(_rootDir);
        var list = new List<MemoryResourceSummary>(capacity: Math.Min(limit, dirs.Length));

        foreach (var dir in dirs.OrderByDescending(Directory.GetLastWriteTimeUtc))
        {
            ct.ThrowIfCancellationRequested();
            if (list.Count >= limit) break;

            var manifestPath = Path.Combine(dir, ManifestFileName);
            if (!File.Exists(manifestPath))
                continue;

            try
            {
                var text = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<MemoryBundleManifest>(text, _jsonOptions);
                if (manifest == null || string.IsNullOrWhiteSpace(manifest.MemoryId))
                    continue;

                var scopeType = ParseEnumOrDefault(manifest.ScopeType, MemoryScopeType.Unspecified);
                if (scopeTypeFilter is not null &&
                    scopeTypeFilter.Value != MemoryScopeType.Unspecified &&
                    scopeType != scopeTypeFilter.Value)
                {
                    continue;
                }

                list.Add(new MemoryResourceSummary
                {
                    MemoryId = manifest.MemoryId,
                    Scope = new MemoryScope
                    {
                        Type = scopeType,
                        ScopeId = manifest.ScopeId ?? string.Empty
                    },
                    EntryCount = manifest.EntryCount,
                    LatestAt = Timestamp.FromDateTime(manifest.LatestAtUtc.UtcDateTime)
                });
            }
            catch
            {
                // Ignore malformed bundles (best-effort).
            }
        }

        return Task.FromResult<IReadOnlyList<MemoryResourceSummary>>(list);
    }

    public async Task<IReadOnlyList<MemoryEntry>> ListEntriesAsync(
        string memoryId,
        int limit = 200,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(memoryId))
            return [];

        var bundleDir = GetBundleDirectory(_rootDir, memoryId.Trim());
        var entriesPath = Path.Combine(bundleDir, EntriesBinaryFileName);
        if (!File.Exists(entriesPath))
            return [];

        var take = Math.Clamp(limit, 1, 2000);
        var ring = new Queue<MemoryEntry>(capacity: take);

        await using var fs = new FileStream(entriesPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        while (fs.Position < fs.Length)
        {
            ct.ThrowIfCancellationRequested();

            var e = MemoryEntry.Parser.ParseDelimitedFrom(fs);
            if (e == null) break;

            if (ring.Count == take)
                ring.Dequeue();
            ring.Enqueue(e);
        }

        return ring.ToList();
    }

    public async Task<IReadOnlyList<MemoryEntry>> SearchAsync(
        string query,
        int limit = 50,
        MemoryScopeType? scopeTypeFilter = null,
        string? memoryId = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(query))
            return [];

        var q = query.Trim();
        var take = Math.Clamp(limit, 1, 500);

        if (!Directory.Exists(_rootDir))
            return [];

        var matches = new List<MemoryEntry>();

        IEnumerable<string> dirs;
        if (!string.IsNullOrWhiteSpace(memoryId))
        {
            dirs = new[] { GetBundleDirectory(_rootDir, memoryId.Trim()) };
        }
        else
        {
            dirs = Directory.GetDirectories(_rootDir);
        }

        foreach (var dir in dirs)
        {
            ct.ThrowIfCancellationRequested();

            var entriesPath = Path.Combine(dir, EntriesBinaryFileName);
            if (!File.Exists(entriesPath))
                continue;

            await using var fs = new FileStream(entriesPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            while (fs.Position < fs.Length)
            {
                ct.ThrowIfCancellationRequested();
                if (matches.Count >= take) break;

                var e = MemoryEntry.Parser.ParseDelimitedFrom(fs);
                if (e == null) break;

                if (scopeTypeFilter is not null &&
                    scopeTypeFilter.Value != MemoryScopeType.Unspecified &&
                    (e.Scope?.Type ?? MemoryScopeType.Unspecified) != scopeTypeFilter.Value)
                {
                    continue;
                }

                if ((e.Content ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(e);
                }
            }

            if (matches.Count >= take) break;
        }

        return matches
            .OrderByDescending(e => e.CreatedAt?.ToDateTime() ?? DateTime.MinValue)
            .Take(take)
            .ToList();
    }

    public static string? GetMemoryRootFromEnvironment()
    {
        var env = Environment.GetEnvironmentVariable(AevatarAgentsConstants.MemoryDirEnv);
        if (string.IsNullOrWhiteSpace(env))
            return null;

        try
        {
            return Path.GetFullPath(env.Trim());
        }
        catch
        {
            return null;
        }
    }

    public static string GetMemoryRootFromEnvironmentOrDefault()
    {
        return GetMemoryRootFromEnvironment() ?? GetDefaultMemoryRoot();
    }

    public static string GetDefaultMemoryRoot(string? startDirectory = null)
    {
        // Reuse FileExecutionTraceStore's repo-root detection to avoid duplicated heuristics.
        var traceRoot = FileExecutionTraceStore.GetDefaultTraceRoot(startDirectory);
        var repoRoot = Path.GetDirectoryName(traceRoot) ?? Directory.GetCurrentDirectory();
        return Path.Combine(repoRoot, DefaultMemoryDirectoryName);
    }

    public static string GetBundleDirectory(string memoryRoot, string memoryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(memoryId);
        return Path.Combine(Path.GetFullPath(memoryRoot.Trim()), SanitizeDirectoryName(memoryId.Trim()));
    }

    private static string SanitizeDirectoryName(string name)
    {
        // Eliminate path traversal and invalid file name chars.
        var s = name.Replace(Path.DirectorySeparatorChar, '_')
            .Replace(Path.AltDirectorySeparatorChar, '_');

        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            s = s.Replace(ch, '_');
        }

        // Avoid empty/hidden names.
        if (string.IsNullOrWhiteSpace(s))
            return "memory";

        return s;
    }

    private MemoryBundleManifest? TryReadManifest(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var text = File.ReadAllText(path);
            return JsonSerializer.Deserialize<MemoryBundleManifest>(text, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static TEnum ParseEnumOrDefault<TEnum>(string? value, TEnum fallback)
        where TEnum : struct
    {
        return System.Enum.TryParse<TEnum>(value, ignoreCase: true, out var v) ? v : fallback;
    }

    // ============================================================
    //  Bundle Manifest (JSON)
    // ============================================================

    private sealed record MemoryBundleManifest
    {
        public int SchemaVersion { get; init; }
        public string MemoryId { get; init; } = string.Empty;
        public string ScopeType { get; init; } = string.Empty;
        public string ScopeId { get; init; } = string.Empty;
        public int EntryCount { get; init; }
        public DateTimeOffset LatestAtUtc { get; init; }
        public MemoryBundleFiles Files { get; init; } = new();
    }

    private sealed record MemoryBundleFiles
    {
        public string EntriesBinary { get; init; } = EntriesBinaryFileName;
        public string Manifest { get; init; } = ManifestFileName;
    }
}


