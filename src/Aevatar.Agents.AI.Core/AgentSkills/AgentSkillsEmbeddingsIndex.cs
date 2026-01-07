using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core.AgentSkills;

/// <summary>
/// Disk-backed embeddings index for Agent Skills discovery.
/// <para/>
/// Design goals:
/// - Avoid per-query embedding generation for all skills (expensive).
/// - Build once (e.g., after git sync) and reuse.
/// - If index missing or stale, rebuild lazily on query.
/// </summary>
public static class AgentSkillsEmbeddingsIndex
{
    public const int CurrentVersion = 1;

    /// <summary>
    /// If set, overrides the default index directory.
    /// </summary>
    public const string IndexDirEnv = "AEVATAR_AGENT_SKILLS_INDEX_DIR";

    public static string GetIndexBaseDirectory()
    {
        var env = Environment.GetEnvironmentVariable(IndexDirEnv);
        if (!string.IsNullOrWhiteSpace(env))
        {
            try
            {
                var full = Path.GetFullPath(env.Trim());
                Directory.CreateDirectory(full);
                return full;
            }
            catch
            {
                // fallthrough
            }
        }

        // Default: ~/.aevatar/agent_skills_index
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
            home = Path.GetTempPath();

        var dir = Path.Combine(home, ".aevatar", "agent_skills_index");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string GetIndexFilePathForRoot(string skillsRootDirectory, string? indexBaseDirOverride = null)
    {
        var root = Path.GetFullPath(skillsRootDirectory);
        var baseDir = string.IsNullOrWhiteSpace(indexBaseDirOverride)
            ? GetIndexBaseDirectory()
            : Path.GetFullPath(indexBaseDirOverride!);

        Directory.CreateDirectory(baseDir);

        var key = ComputeRootKey(root);
        return Path.Combine(baseDir, $"skills-index-{key}.json");
    }

    public static async Task<AgentSkillsEmbeddingsIndexData?> TryLoadAsync(
        string indexFilePath,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(indexFilePath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(indexFilePath, cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            var data = JsonSerializer.Deserialize<AgentSkillsEmbeddingsIndexData>(json, JsonOptions);
            return data is { Version: CurrentVersion } ? data : null;
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to load skill embeddings index (best-effort). File={File}", indexFilePath);
            return null;
        }
    }

    public static async Task<AgentSkillsEmbeddingsIndexData> BuildAsync(
        string skillsRootDirectory,
        IReadOnlyList<AgentSkillsEmbeddingDocument> documents,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        EmbeddingGenerationOptions? embeddingOptions,
        string? indexBaseDirOverride,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        if (documents == null)
            throw new ArgumentNullException(nameof(documents));

        var root = Path.GetFullPath(skillsRootDirectory);
        var indexPath = GetIndexFilePathForRoot(root, indexBaseDirOverride);
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);

        // Batch embeddings to keep requests bounded.
        const int BatchSize = 32;

        var entries = new List<AgentSkillsEmbeddingsIndexEntry>(documents.Count);

        for (var i = 0; i < documents.Count; i += BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = documents.Skip(i).Take(BatchSize).ToList();
            var inputs = batch.Select(d => d.TextToEmbed ?? string.Empty).ToList();

            IReadOnlyList<Embedding<float>> embeds;
            try
            {
                embeds = await embeddingGenerator.GenerateAsync(inputs, embeddingOptions, cancellationToken);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Embedding generation failed while building skills index (best-effort).");
                throw;
            }

            for (var j = 0; j < batch.Count; j++)
            {
                var doc = batch[j];
                var emb = embeds.Count > j ? embeds[j] : default;
                var vec = emb is { Vector.Length: > 0 } e2 ? e2.Vector.ToArray() : Array.Empty<float>();

                entries.Add(new AgentSkillsEmbeddingsIndexEntry
                {
                    Name = doc.Name,
                    FolderName = doc.FolderName,
                    DirectoryPath = doc.DirectoryPath,
                    SkillFilePath = doc.SkillFilePath,
                    SkillFileLastWriteUtcTicks = doc.SkillFileLastWriteUtcTicks,
                    Vector = vec
                });
            }
        }

        var dim = entries.FirstOrDefault(x => x.Vector.Length > 0)?.Vector.Length ?? 0;

        var data = new AgentSkillsEmbeddingsIndexData
        {
            Version = CurrentVersion,
            SkillsRoot = root,
            BuiltAtUtc = DateTimeOffset.UtcNow,
            ModelId = embeddingOptions?.ModelId,
            Dimensions = dim,
            Entries = entries
        };

        try
        {
            var json = JsonSerializer.Serialize(data, JsonOptions);
            await File.WriteAllTextAsync(indexPath, json, cancellationToken);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to write skills embeddings index (best-effort). File={File}", indexPath);
            // still return data
        }

        return data;
    }

    public static async Task<AgentSkillsEmbeddingsIndexData?> EnsureIndexAsync(
        string skillsRootDirectory,
        Func<CancellationToken, Task<IReadOnlyList<AgentSkillsEmbeddingDocument>>> discoverDocumentsAsync,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        EmbeddingGenerationOptions? embeddingOptions,
        string? indexBaseDirOverride,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(skillsRootDirectory);
        var indexPath = GetIndexFilePathForRoot(root, indexBaseDirOverride);

        var docs = await discoverDocumentsAsync(cancellationToken);
        if (docs.Count == 0)
        {
            return await TryLoadAsync(indexPath, logger, cancellationToken);
        }

        var existing = await TryLoadAsync(indexPath, logger, cancellationToken);
        if (existing != null &&
            existing.IsFreshFor(root) &&
            existing.IsCompatibleWithDocs(docs))
        {
            return existing;
        }

        try
        {
            return await BuildAsync(root, docs, embeddingGenerator, embeddingOptions, indexBaseDirOverride, logger, cancellationToken);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to build skills embeddings index (best-effort). Root={Root}", root);
            return existing;
        }
    }

    private static string ComputeRootKey(string root)
    {
        // Keep filename short-ish but stable.
        var bytes = Encoding.UTF8.GetBytes(root.ToLowerInvariant());
        var hash = SHA256.HashData(bytes);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return hex.Length >= 16 ? hex[..16] : hex;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}

public sealed class AgentSkillsEmbeddingsIndexData
{
    public int Version { get; set; } = AgentSkillsEmbeddingsIndex.CurrentVersion;
    public string SkillsRoot { get; set; } = string.Empty;
    public DateTimeOffset BuiltAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional model id as provided via EmbeddingGenerationOptions.
    /// </summary>
    public string? ModelId { get; set; }

    public int Dimensions { get; set; }

    public List<AgentSkillsEmbeddingsIndexEntry> Entries { get; set; } = new();

    public bool IsFreshFor(string skillsRoot)
        => string.Equals(Path.GetFullPath(skillsRoot), Path.GetFullPath(SkillsRoot), StringComparison.OrdinalIgnoreCase);

    public bool IsCompatibleWithDocs(IReadOnlyList<AgentSkillsEmbeddingDocument> docs)
    {
        if (docs == null || docs.Count == 0)
            return true;

        if (Entries == null || Entries.Count == 0)
            return false;

        // Dimension check (best-effort): if we have vectors, enforce consistent dims.
        if (Dimensions > 0)
        {
            var anyMismatch = Entries.Any(e => e.Vector is { Length: > 0 } v && v.Length != Dimensions);
            if (anyMismatch)
                return false;
        }

        var map = new Dictionary<string, AgentSkillsEmbeddingsIndexEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in Entries)
        {
            if (!string.IsNullOrWhiteSpace(e.SkillFilePath))
                map[e.SkillFilePath] = e;
        }

        foreach (var d in docs)
        {
            if (!map.TryGetValue(d.SkillFilePath, out var e))
                return false;

            if (e.SkillFileLastWriteUtcTicks != d.SkillFileLastWriteUtcTicks)
                return false;

            if (Dimensions > 0 && e.Vector is { Length: > 0 } v && v.Length != Dimensions)
                return false;
        }

        return true;
    }
}

public sealed class AgentSkillsEmbeddingsIndexEntry
{
    public string Name { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
    public string DirectoryPath { get; set; } = string.Empty;
    public string SkillFilePath { get; set; } = string.Empty;
    public long SkillFileLastWriteUtcTicks { get; set; }
    public float[] Vector { get; set; } = Array.Empty<float>();
}

public sealed class AgentSkillsEmbeddingDocument
{
    public required string Name { get; init; }
    public required string FolderName { get; init; }
    public required string DirectoryPath { get; init; }
    public required string SkillFilePath { get; init; }
    public required long SkillFileLastWriteUtcTicks { get; init; }
    public required string TextToEmbed { get; init; }
}


