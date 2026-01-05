using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.Core.Tracing;
using Google.Protobuf;

namespace Aevatar.Agents.Core.MemoryGraphs;

// ============================================================
//  FileMemoryGraphStore
//
//  Directory Layout (Trace Bundle v1 extension):
//
//  ${AEVATAR_TRACE_DIR}/
//    <executionId>/
//      artifacts/
//        memory_graph.pb
//        memory_graph.json
//
//  NOTES:
//  - Graph is treated as a trace artifact (derived, reproducible).
// ============================================================
public sealed class FileMemoryGraphStore : IMemoryGraphStore
{
    public const string GraphBinaryFileName = "memory_graph.pb";
    public const string GraphJsonFileName = "memory_graph.json";

    private readonly string _traceRootDir;

    public FileMemoryGraphStore(string traceRootDirectory)
    {
        if (string.IsNullOrWhiteSpace(traceRootDirectory))
            throw new ArgumentException("Trace root directory cannot be empty.", nameof(traceRootDirectory));

        _traceRootDir = Path.GetFullPath(traceRootDirectory.Trim());
    }

    public Task SaveAsync(MemoryGraph graph, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(graph);

        var graphId = graph.GraphId?.Trim();
        if (string.IsNullOrWhiteSpace(graphId))
            throw new ArgumentException("MemoryGraph.graph_id cannot be empty.", nameof(graph));

        var bundleDir = FileExecutionTraceStore.GetBundleDirectory(_traceRootDir, graphId);
        var artifactsDir = Path.Combine(bundleDir, FileExecutionTraceStore.ArtifactsDirectoryName);
        Directory.CreateDirectory(artifactsDir);

        var pbPath = Path.Combine(artifactsDir, GraphBinaryFileName);
        var jsonPath = Path.Combine(artifactsDir, GraphJsonFileName);

        var bytes = graph.ToByteArray();
        var writePb = File.WriteAllBytesAsync(pbPath, bytes, ct);

        var json = JsonFormatter.Default.Format(graph);
        var writeJson = File.WriteAllTextAsync(jsonPath, json, ct);

        return Task.WhenAll(writePb, writeJson);
    }

    public async Task<MemoryGraph?> LoadAsync(string graphId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(graphId))
            return null;

        var bundleDir = FileExecutionTraceStore.GetBundleDirectory(_traceRootDir, graphId.Trim());
        var artifactsDir = Path.Combine(bundleDir, FileExecutionTraceStore.ArtifactsDirectoryName);
        var pbPath = Path.Combine(artifactsDir, GraphBinaryFileName);
        if (!File.Exists(pbPath))
            return null;

        var bytes = await File.ReadAllBytesAsync(pbPath, ct);
        return MemoryGraph.Parser.ParseFrom(bytes);
    }

    public Task<bool> ExistsAsync(string graphId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(graphId))
            return Task.FromResult(false);

        var bundleDir = FileExecutionTraceStore.GetBundleDirectory(_traceRootDir, graphId.Trim());
        var artifactsDir = Path.Combine(bundleDir, FileExecutionTraceStore.ArtifactsDirectoryName);
        var pbPath = Path.Combine(artifactsDir, GraphBinaryFileName);
        return Task.FromResult(File.Exists(pbPath));
    }

    public Task DeleteAsync(string graphId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(graphId))
            return Task.CompletedTask;

        var bundleDir = FileExecutionTraceStore.GetBundleDirectory(_traceRootDir, graphId.Trim());
        var artifactsDir = Path.Combine(bundleDir, FileExecutionTraceStore.ArtifactsDirectoryName);
        if (!Directory.Exists(artifactsDir))
            return Task.CompletedTask;

        TryDelete(Path.Combine(artifactsDir, GraphBinaryFileName));
        TryDelete(Path.Combine(artifactsDir, GraphJsonFileName));

        return Task.CompletedTask;
    }

    public static string GetTraceRootFromEnvironmentOrDefault()
        => FileExecutionTraceStore.GetTraceRootFromEnvironmentOrDefault();

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best-effort
        }
    }
}


