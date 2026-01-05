using System.Text.Json;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Tracing;
using Google.Protobuf;

namespace Aevatar.Agents.Core.Tracing;

// ============================================================
//  FileExecutionTraceStore
//
//  Directory Layout (Trace Bundle v1):
//
//  ${AEVATAR_TRACE_DIR}/
//    <executionId>/
//      trace.pb          # Protobuf binary (canonical)
//      trace.json        # JSON export (human/UI)
//      manifest.json     # Bundle metadata
//      artifacts/        # Optional extra files (UI/debug)
//
//  NOTES:
//  - If env var AEVATAR_TRACE_DIR is not set, the framework will fall back to a default:
//    <repoRoot>/trace (best-effort detected by walking up from current directory).
//  - This store is intentionally simple and synchronous-by-design at the directory level:
//    One executionId -> one directory.
// ============================================================
public sealed class FileExecutionTraceStore : IExecutionTraceStore
{
    public const string TraceBinaryFileName = "trace.pb";
    public const string TraceJsonFileName = "trace.json";
    public const string ManifestFileName = "manifest.json";
    public const string ArtifactsDirectoryName = "artifacts";
    public const string DefaultTraceDirectoryName = "trace";

    private readonly string _rootDir;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public FileExecutionTraceStore(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("Trace root directory cannot be empty.", nameof(rootDirectory));

        _rootDir = Path.GetFullPath(rootDirectory.Trim());
    }

    public Task SaveAsync(ExecutionTrace trace, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(trace);

        var executionId = trace.ExecutionId?.Trim();
        if (string.IsNullOrEmpty(executionId))
            throw new ArgumentException("ExecutionTrace.execution_id cannot be empty.", nameof(trace));

        var bundleDir = GetBundleDirectory(executionId);
        Directory.CreateDirectory(bundleDir);

        // NOTE: Only create artifacts dir when needed by caller.
        // (Keep bundle compact by default.)

        var pbPath = Path.Combine(bundleDir, TraceBinaryFileName);
        var jsonPath = Path.Combine(bundleDir, TraceJsonFileName);
        var manifestPath = Path.Combine(bundleDir, ManifestFileName);

        var exportedAt = DateTimeOffset.UtcNow;

        // ============================================================
        //  Persist binary (canonical)
        // ============================================================
        var bytes = trace.ToByteArray();
        var writePb = File.WriteAllBytesAsync(pbPath, bytes, ct);

        // ============================================================
        //  Persist JSON (human/UI)
        // ============================================================
        var json = trace.ToJsonString();
        var writeJson = File.WriteAllTextAsync(jsonPath, json, ct);

        // ============================================================
        //  Persist manifest (bundle metadata)
        // ============================================================
        var manifest = new ExecutionTraceBundleManifest
        {
            SchemaVersion = 1,
            ExecutionId = executionId,
            Kind = trace.Kind.ToString(),
            Status = trace.Status.ToString(),
            Name = trace.Name ?? string.Empty,
            ExportedAtUtc = exportedAt,
            Files = new ExecutionTraceBundleFiles
            {
                TraceBinary = TraceBinaryFileName,
                TraceJson = TraceJsonFileName,
                Manifest = ManifestFileName,
                ArtifactsDir = ArtifactsDirectoryName
            }
        };

        var manifestJson = JsonSerializer.Serialize(manifest, _jsonOptions);
        var writeManifest = File.WriteAllTextAsync(manifestPath, manifestJson, ct);

        return Task.WhenAll(writePb, writeJson, writeManifest);
    }

    public async Task<ExecutionTrace?> LoadAsync(string executionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(executionId))
            return null;

        var bundleDir = GetBundleDirectory(executionId.Trim());
        var pbPath = Path.Combine(bundleDir, TraceBinaryFileName);
        if (!File.Exists(pbPath))
            return null;

        var bytes = await File.ReadAllBytesAsync(pbPath, ct);
        return ExecutionTrace.Parser.ParseFrom(bytes);
    }

    public Task<bool> ExistsAsync(string executionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(executionId))
            return Task.FromResult(false);

        var bundleDir = GetBundleDirectory(executionId.Trim());
        var pbPath = Path.Combine(bundleDir, TraceBinaryFileName);
        return Task.FromResult(File.Exists(pbPath));
    }

    public Task DeleteAsync(string executionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(executionId))
            return Task.CompletedTask;

        var bundleDir = GetBundleDirectory(executionId.Trim());
        if (!Directory.Exists(bundleDir))
            return Task.CompletedTask;

        // NOTE: Directory.Delete is sync; trace bundles are typically small.
        Directory.Delete(bundleDir, recursive: true);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ExecutionTraceBundleInfo>> ListAsync(int limit = 200, CancellationToken ct = default)
    {
        // NOTE:
        // - Best-effort enumeration.
        // - We prefer manifest.json if present; fall back to directory name.
        if (limit <= 0)
            return Task.FromResult<IReadOnlyList<ExecutionTraceBundleInfo>>([]);

        if (!Directory.Exists(_rootDir))
            return Task.FromResult<IReadOnlyList<ExecutionTraceBundleInfo>>([]);

        var dirs = Directory.GetDirectories(_rootDir);
        var list = new List<ExecutionTraceBundleInfo>(capacity: Math.Min(limit, dirs.Length));

        foreach (var dir in dirs.OrderByDescending(Directory.GetLastWriteTimeUtc))
        {
            if (list.Count >= limit)
                break;

            var manifestPath = Path.Combine(dir, ManifestFileName);
            if (!File.Exists(manifestPath))
                continue;

            try
            {
                var text = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<ExecutionTraceBundleManifest>(text, _jsonOptions);
                if (manifest == null || string.IsNullOrWhiteSpace(manifest.ExecutionId))
                    continue;

                list.Add(new ExecutionTraceBundleInfo
                {
                    ExecutionId = manifest.ExecutionId,
                    Kind = ParseEnumOrDefault(manifest.Kind, ExecutionTraceKind.Unspecified),
                    Status = ParseEnumOrDefault(manifest.Status, ExecutionTraceStatus.Unspecified),
                    Name = manifest.Name ?? string.Empty,
                    ExportedAtUtc = manifest.ExportedAtUtc
                });
            }
            catch
            {
                // Ignore malformed bundles (best-effort).
            }
        }

        return Task.FromResult<IReadOnlyList<ExecutionTraceBundleInfo>>(list);
    }

    public static string? GetTraceRootFromEnvironment()
    {
        var env = Environment.GetEnvironmentVariable(AevatarAgentsConstants.TraceDirEnv);
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

    public static string GetTraceRootFromEnvironmentOrDefault()
    {
        return GetTraceRootFromEnvironment() ?? GetDefaultTraceRoot();
    }

    public static string GetDefaultTraceRoot(string? startDirectory = null)
    {
        var start = startDirectory;
        if (string.IsNullOrWhiteSpace(start))
        {
            start = Directory.GetCurrentDirectory();
        }

        var repoRoot = TryFindRepoRoot(start!) ?? start!;
        try
        {
            repoRoot = Path.GetFullPath(repoRoot);
        }
        catch
        {
            // Ignore invalid paths, fall back to raw string.
        }

        return Path.Combine(repoRoot, DefaultTraceDirectoryName);
    }

    private static string? TryFindRepoRoot(string startDirectory)
    {
        DirectoryInfo? dir;
        try
        {
            dir = new DirectoryInfo(Path.GetFullPath(startDirectory));
        }
        catch
        {
            dir = new DirectoryInfo(startDirectory);
        }

        // Safety bound: avoid walking too far in pathological environments.
        for (var i = 0; i < 32 && dir != null; i++, dir = dir.Parent)
        {
            try
            {
                // 1) Git root
                var git = Path.Combine(dir.FullName, ".git");
                if (Directory.Exists(git) || File.Exists(git))
                    return dir.FullName;

                // 2) .NET repo root (central package management)
                if (File.Exists(Path.Combine(dir.FullName, "Directory.Packages.props")))
                    return dir.FullName;

                // 3) Solution root
                if (Directory.EnumerateFiles(dir.FullName, "*.sln").Any() ||
                    Directory.EnumerateFiles(dir.FullName, "*.slnx").Any())
                    return dir.FullName;
            }
            catch
            {
                // Ignore and continue walking up.
            }
        }

        return null;
    }

    public static string GetBundleDirectory(string traceRoot, string executionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(traceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        return Path.Combine(Path.GetFullPath(traceRoot.Trim()), SanitizeDirectoryName(executionId.Trim()));
    }

    private string GetBundleDirectory(string executionId)
    {
        return GetBundleDirectory(_rootDir, executionId);
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
            return "trace";

        return s;
    }

    private static TEnum ParseEnumOrDefault<TEnum>(string? value, TEnum fallback)
        where TEnum : struct
    {
        return Enum.TryParse<TEnum>(value, ignoreCase: true, out var v) ? v : fallback;
    }

    // ============================================================
    //  Bundle Manifest (JSON)
    // ============================================================

    private sealed record ExecutionTraceBundleManifest
    {
        public int SchemaVersion { get; init; }
        public string ExecutionId { get; init; } = string.Empty;
        public string Kind { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public DateTimeOffset ExportedAtUtc { get; init; }
        public ExecutionTraceBundleFiles Files { get; init; } = new();
    }

    private sealed record ExecutionTraceBundleFiles
    {
        public string TraceBinary { get; init; } = TraceBinaryFileName;
        public string TraceJson { get; init; } = TraceJsonFileName;
        public string Manifest { get; init; } = ManifestFileName;
        public string ArtifactsDir { get; init; } = ArtifactsDirectoryName;
    }
}


