using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Memory;
using Google.Protobuf;

namespace Aevatar.Agents.Core.Memory;

// ============================================================
//  FileMemoryVectorIndex
//
//  Directory Layout (Vector Bundle v1):
//
//  ${AEVATAR_MEMORY_VECTOR_DIR or AEVATAR_MEMORY_DIR}/
//    <memoryId>/
//      vectors.pb        # Protobuf delimited stream (append-only)
//
//  NOTES:
//  - Default root is co-located with FileMemoryStore to simplify portability.
//  - Search is brute-force cosine similarity (best-effort, no external deps).
// ============================================================
public sealed class FileMemoryVectorIndex : IMemoryVectorIndex
{
    public const string VectorsBinaryFileName = "vectors.pb";

    private readonly string _rootDir;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public FileMemoryVectorIndex(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("Vector index root directory cannot be empty.", nameof(rootDirectory));

        _rootDir = Path.GetFullPath(rootDirectory.Trim());
    }

    public async Task UpsertAsync(MemoryVectorRecord record, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(record);

        var memoryId = record.MemoryId?.Trim();
        if (string.IsNullOrWhiteSpace(memoryId))
            throw new ArgumentException("MemoryVectorRecord.memory_id cannot be empty.", nameof(record));

        if (string.IsNullOrWhiteSpace(record.EntryId))
            throw new ArgumentException("MemoryVectorRecord.entry_id cannot be empty.", nameof(record));

        if (record.Embedding == null || record.Embedding.Count == 0)
            throw new ArgumentException("MemoryVectorRecord.embedding cannot be empty.", nameof(record));

        var sem = _locks.GetOrAdd(memoryId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            var bundleDir = FileMemoryStore.GetBundleDirectory(_rootDir, memoryId);
            Directory.CreateDirectory(bundleDir);

            var vectorsPath = Path.Combine(bundleDir, VectorsBinaryFileName);
            await using var fs = new FileStream(vectorsPath, FileMode.Append, FileAccess.Write, FileShare.Read);
            record.WriteDelimitedTo(fs);
            await fs.FlushAsync(ct);
        }
        finally
        {
            sem.Release();
        }
    }

    public async Task<IReadOnlyList<MemoryVectorMatch>> SearchAsync(
        IReadOnlyList<float> queryEmbedding,
        int limit = 20,
        string? memoryId = null,
        MemoryScopeType? scopeTypeFilter = null,
        string? scopeId = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (queryEmbedding == null || queryEmbedding.Count == 0)
            return [];

        var take = Math.Clamp(limit, 1, 200);

        if (!Directory.Exists(_rootDir))
            return [];

        IEnumerable<string> dirs;
        if (!string.IsNullOrWhiteSpace(memoryId))
        {
            dirs = new[] { FileMemoryStore.GetBundleDirectory(_rootDir, memoryId.Trim()) };
        }
        else
        {
            dirs = Directory.GetDirectories(_rootDir);
        }

        // Keep the best K only.
        var pq = new PriorityQueue<MemoryVectorMatch, double>();

        foreach (var dir in dirs)
        {
            ct.ThrowIfCancellationRequested();

            var vectorsPath = Path.Combine(dir, VectorsBinaryFileName);
            if (!File.Exists(vectorsPath))
                continue;

            await using var fs = new FileStream(vectorsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            while (fs.Position < fs.Length)
            {
                ct.ThrowIfCancellationRequested();

                var r = MemoryVectorRecord.Parser.ParseDelimitedFrom(fs);
                if (r == null) break;

                if (scopeTypeFilter is not null &&
                    scopeTypeFilter.Value != MemoryScopeType.Unspecified &&
                    (r.Scope?.Type ?? MemoryScopeType.Unspecified) != scopeTypeFilter.Value)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(scopeId))
                {
                    var sid = r.Scope?.ScopeId ?? string.Empty;
                    if (!string.Equals(sid, scopeId.Trim(), StringComparison.Ordinal))
                        continue;
                }

                var similarity = CosineSimilarity(queryEmbedding, r.Embedding);
                if (double.IsNaN(similarity) || double.IsInfinity(similarity))
                    continue;

                var match = new MemoryVectorMatch { Record = r, Similarity = similarity };

                if (pq.Count < take)
                {
                    pq.Enqueue(match, similarity);
                    continue;
                }

                if (!pq.TryPeek(out _, out var min))
                    continue;

                if (similarity <= min)
                    continue;

                pq.Dequeue();
                pq.Enqueue(match, similarity);
            }
        }

        if (pq.Count == 0)
            return [];

        // Sort descending for final output.
        var result = pq.UnorderedItems
            .Select(i => i.Element)
            .OrderByDescending(m => m.Similarity)
            .ToList();

        return result;
    }

    public static string? GetVectorRootFromEnvironment()
    {
        var env = Environment.GetEnvironmentVariable(AevatarAgentsConstants.MemoryVectorDirEnv);
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

    public static string GetVectorRootFromEnvironmentOrDefault()
    {
        // Default: co-locate with FileMemoryStore root.
        return GetVectorRootFromEnvironment() ?? FileMemoryStore.GetMemoryRootFromEnvironmentOrDefault();
    }

    private static double CosineSimilarity(IReadOnlyList<float> query, Google.Protobuf.Collections.RepeatedField<float> candidate)
    {
        if (candidate == null || candidate.Count == 0)
            return double.NaN;

        if (query.Count != candidate.Count)
            return double.NaN;

        double dot = 0;
        double magQ = 0;
        double magC = 0;

        for (var i = 0; i < query.Count; i++)
        {
            var q = query[i];
            var c = candidate[i];
            dot += q * c;
            magQ += q * q;
            magC += c * c;
        }

        if (magQ <= 0 || magC <= 0)
            return double.NaN;

        return dot / (Math.Sqrt(magQ) * Math.Sqrt(magC));
    }
}


