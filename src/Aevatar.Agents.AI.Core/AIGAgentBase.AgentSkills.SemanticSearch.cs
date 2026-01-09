using Aevatar.Agents.AI.Core.AgentSkills;
using Aevatar.Agents.AI.Tool.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

public abstract partial class AIGAgentBase
{
    private sealed record RankedSkill(AgentSkillDescriptor Skill, double Score, List<string> Why);

    private async Task<(string Mode, List<RankedSkill> Items)> TryRankSkillsByEmbeddingsAsync(
        string query,
        IReadOnlyList<string> roots,
        IReadOnlyList<AgentSkillDescriptor> skills,
        int maxResults,
        ToolExecutionContext? executionContext,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!TryGetEmbeddingGenerator(out var generator))
                return ("lexical", new List<RankedSkill>());

            if (ActiveProviderConfig?.Embeddings == null)
                return ("lexical", new List<RankedSkill>());

            var q = await GenerateEmbeddingAsync(query, cancellationToken: cancellationToken);
            if (q == null || q.Vector.Length == 0)
                return ("lexical", new List<RankedSkill>());

            var options = BuildDefaultEmbeddingOptions();

            // Ensure index exists for each root (build lazily when missing/stale).
            var embeddingMap = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var rootFull = Path.GetFullPath(root);
                var progress = executionContext?.ReportProgressAsync;
                if (progress != null)
                {
                    try
                    {
                        await progress($"skills.index: ensure ({Path.GetFileName(rootFull)})", cancellationToken);
                    }
                    catch
                    {
                        // best-effort
                    }
                }

                var index = await AgentSkillsEmbeddingsIndex.EnsureIndexAsync(
                    rootFull,
                    discoverDocumentsAsync: _ => Task.FromResult<IReadOnlyList<AgentSkillsEmbeddingDocument>>(BuildDocsForRoot(rootFull, skills)),
                    embeddingGenerator: generator!,
                    embeddingOptions: options,
                    indexBaseDirOverride: null,
                    logger: Logger,
                    progress: progress == null
                        ? null
                        : (msg, ct) => progress($"skills.index({Path.GetFileName(rootFull)}): {msg}", ct),
                    cancellationToken: cancellationToken);

                if (index?.Entries == null)
                    continue;

                foreach (var e in index.Entries)
                {
                    if (string.IsNullOrWhiteSpace(e.SkillFilePath) || e.Vector is not { Length: > 0 })
                        continue;

                    embeddingMap[e.SkillFilePath] = e.Vector;
                }
            }

            if (embeddingMap.Count == 0)
                return ("lexical", new List<RankedSkill>());

            var qSpan = q.Vector.Span;

            var ranked = new List<RankedSkill>(skills.Count);
            foreach (var s in skills)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!embeddingMap.TryGetValue(s.SkillFilePath, out var vec) || vec.Length == 0)
                    continue;

                var sim = CosineSimilarity(qSpan, vec);
                ranked.Add(new RankedSkill(s, sim, new List<string> { "embedding" }));
            }

            var top = ranked
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Skill.Name, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Clamp(maxResults, 1, 10))
                .ToList();

            return ("embedding", top);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Embedding-based skill search failed (best-effort). Falling back to lexical.");
            return ("lexical", new List<RankedSkill>());
        }
    }

    private static IReadOnlyList<AgentSkillsEmbeddingDocument> BuildDocsForRoot(
        string rootFull,
        IReadOnlyList<AgentSkillDescriptor> allSkills)
    {
        var rootPrefix = rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var docs = new List<AgentSkillsEmbeddingDocument>();

        foreach (var s in allSkills)
        {
            try
            {
                var dir = Path.GetFullPath(s.DirectoryPath);
                if (!dir.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(dir, rootFull, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                long ticks;
                try { ticks = File.GetLastWriteTimeUtc(s.SkillFilePath).Ticks; }
                catch { ticks = 0; }

                var text = $"{s.Name}\n{s.Description}";
                docs.Add(new AgentSkillsEmbeddingDocument
                {
                    Name = s.Name,
                    FolderName = s.FolderName,
                    DirectoryPath = s.DirectoryPath,
                    SkillFilePath = s.SkillFilePath,
                    SkillFileLastWriteUtcTicks = ticks,
                    TextToEmbed = text
                });
            }
            catch
            {
                // best-effort
            }
        }

        return docs;
    }

    private static double CosineSimilarity(ReadOnlySpan<float> left, float[] right)
    {
        if (right == null || right.Length == 0)
            return 0;

        if (left.Length != right.Length)
            return 0;

        double dot = 0;
        double magLeft = 0;
        double magRight = 0;

        for (var i = 0; i < left.Length; i++)
        {
            var l = left[i];
            var r = right[i];
            dot += l * r;
            magLeft += l * l;
            magRight += r * r;
        }

        if (magLeft == 0 || magRight == 0)
            return 0;

        return dot / (Math.Sqrt(magLeft) * Math.Sqrt(magRight));
    }
}


