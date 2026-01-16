using System.Text;
using System.Text.RegularExpressions;
using Aevatar.Agents.AI.Core.AgentSkills;
using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

internal sealed partial class AgentSkillsRuntime
{
    private sealed record RankedSkill(AgentSkillDescriptor Skill, double Score, List<string> Why);

    internal async Task<IMessage> ExecuteListSkillsToolAsync(
        ToolExecutionContext? executionContext,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        var roots = GetEffectiveRoots();
        var skills = DiscoverAgentSkills(roots, cancellationToken);

        // For debugging, include very lightweight doc counts (bounded).
        var items = new List<object>(skills.Count);
        foreach (var s in skills)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (scriptsCount, referencesCount, assetsCount) = CountResourceDocsBestEffort(s.DirectoryPath, maxFilesPerDir: 500);
            var sourceRoot = FindContainingRoot(roots, s.DirectoryPath);

            items.Add(new
            {
                name = s.Name,
                description = s.Description,
                folderName = s.FolderName,
                path = s.DirectoryPath,
                source = sourceRoot,
                documents = new
                {
                    scripts = scriptsCount,
                    references = referencesCount,
                    assets = assetsCount,
                    total = scriptsCount + referencesCount + assetsCount
                }
            });
        }

        return ToStruct(new
        {
            success = true,
            roots,
            count = items.Count,
            skills = items
        });
    }

    internal async Task<IMessage> ExecuteFindHelpfulSkillsToolAsync(
        Dictionary<string, object> parameters,
        ToolExecutionContext? executionContext,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        var query = parameters.GetValueOrDefault("query")?.ToString() ?? string.Empty;
        query = query.Trim();
        if (query.Length == 0)
            return ToStruct(new { success = false, error = "Parameter 'query' is required." });

        var maxResults = ClampInt(parameters.GetValueOrDefault("max_results"), fallback: 5, min: 1, max: 10);
        var maxCharsPerSkill = ClampInt(parameters.GetValueOrDefault("max_chars_per_skill"), fallback: 1200, min: 200, max: 6000);

        var roots = GetEffectiveRoots();
        var skills = DiscoverAgentSkills(roots, cancellationToken);

        // Prefer semantic (embeddings) ranking when available; fall back to lexical.
        if (executionContext?.ReportProgressAsync != null)
        {
            try { await executionContext.ReportProgressAsync("skills.search: ranking candidates…", cancellationToken); }
            catch { /* best-effort */ }
        }

        var ranked = await TryRankSkillsByEmbeddingsAsync(query, roots, skills, maxResults, executionContext, cancellationToken);
        var mode = ranked.Mode;

        // If embeddings are unavailable/failed, fall back to lexical scoring.
        var top = ranked.Items;
        if (top.Count == 0)
        {
            var tokens = TokenizeQuery(query);
            var scored = new List<(AgentSkillDescriptor Skill, int Score, List<string> Why)>(skills.Count);

            foreach (var s in skills)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (score, why) = ScoreSkill(tokens, query, s);
                if (score > 0)
                    scored.Add((s, score, why));
            }

            top = scored
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Skill.Name, StringComparer.OrdinalIgnoreCase)
                .Take(maxResults)
                .Select(x => new RankedSkill(x.Skill, x.Score, x.Why))
                .ToList();

            mode = "lexical";
        }

        if (top.Count == 0)
        {
            return ToStruct(new
            {
                success = true,
                query,
                roots,
                mode,
                count = 0,
                candidates = Array.Empty<object>(),
                note = "No matches. If you need exploration/debugging, call list_skills."
            });
        }

        var candidates = new List<object>(top.Count);
        foreach (var item in top)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var skill = item.Skill;
            var score = item.Score;
            var why = item.Why;

            var guidance = TryExtractGuidanceSnippet(skill.SkillFilePath, maxCharsPerSkill, cancellationToken);
            candidates.Add(new
            {
                name = skill.Name,
                description = skill.Description,
                folderName = skill.FolderName,
                path = skill.DirectoryPath,
                score,
                why,
                guidance,
                next = new[]
                {
                    $"skills_load(name=\"{skill.Name}\")",
                    $"read_skill_document(name=\"{skill.Name}\", pattern=\"references/**/*.md\")",
                    $"read_skill_document(name=\"{skill.Name}\", pattern=\"scripts/*.py\")"
                }
            });
        }

        return ToStruct(new
        {
            success = true,
            query,
            roots,
            mode,
            count = candidates.Count,
            candidates
        });
    }

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
            if (!_owner.InternalTryGetEmbeddingGenerator(out var generator))
                return ("lexical", new List<RankedSkill>());

            if (_owner.InternalActiveProviderConfig?.Embeddings == null)
                return ("lexical", new List<RankedSkill>());

            var q = await _owner.InternalGenerateEmbeddingAsync(query, cancellationToken);
            if (q == null || q.Vector.Length == 0)
                return ("lexical", new List<RankedSkill>());

            var options = _owner.InternalBuildDefaultEmbeddingOptions();

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
                    logger: _owner.InternalLogger,
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
            _owner.InternalLogger.LogDebug(ex, "Embedding-based skill search failed (best-effort). Falling back to lexical.");
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

    private static List<string> TokenizeQuery(string query)
    {
        var tokens = new List<string>();
        foreach (var raw in Regex.Split(query.ToLowerInvariant(), @"[^\p{L}\p{N}]+"))
        {
            var t = raw.Trim();
            if (t.Length >= 2)
                tokens.Add(t);
        }
        return tokens;
    }

    private static (int Score, List<string> Why) ScoreSkill(
        List<string> tokens,
        string rawQuery,
        AgentSkillDescriptor skill)
    {
        var why = new List<string>();
        var score = 0;

        var name = (skill.Name ?? string.Empty).ToLowerInvariant();
        var desc = (skill.Description ?? string.Empty).ToLowerInvariant();
        var folder = (skill.FolderName ?? string.Empty).ToLowerInvariant();

        foreach (var t in tokens)
        {
            if (name.Contains(t, StringComparison.Ordinal))
            {
                score += 6;
                why.Add($"name:{t}");
                continue;
            }
            if (desc.Contains(t, StringComparison.Ordinal))
            {
                score += 3;
                why.Add($"desc:{t}");
                continue;
            }
            if (folder.Contains(t, StringComparison.Ordinal))
            {
                score += 1;
                why.Add($"folder:{t}");
            }
        }

        if (rawQuery.Length >= 4 &&
            (name.Contains(rawQuery.ToLowerInvariant(), StringComparison.Ordinal) ||
             desc.Contains(rawQuery.ToLowerInvariant(), StringComparison.Ordinal)))
        {
            score += 4;
            why.Add("phrase");
        }

        return (score, why.Distinct(StringComparer.Ordinal).Take(8).ToList());
    }

    private string? TryExtractGuidanceSnippet(string skillFilePath, int maxChars, CancellationToken cancellationToken)
    {
        try
        {
            var text = ReadAllTextWithLimit(skillFilePath, maxChars: 24_000, cancellationToken);
            var parsed = ParseSkillMarkdown(text);
            var body = parsed.Body ?? string.Empty;

            var snippet = ExtractProcedureSnippet(body, maxChars);
            return string.IsNullOrWhiteSpace(snippet) ? null : snippet;
        }
        catch (Exception ex)
        {
            _owner.InternalLogger.LogDebug(ex, "Failed to extract guidance snippet (best-effort).");
            return null;
        }
    }

    private static string ExtractProcedureSnippet(string body, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        var lines = body.Replace("\r\n", "\n").Split('\n');

        // Try to locate a "procedure" section header.
        var headerIdx = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            var l = lines[i].Trim();
            if (!(l.StartsWith("##") || l.StartsWith("###")))
                continue;

            var t = l.TrimStart('#').Trim().ToLowerInvariant();
            if (t.Contains("workflow") || t.Contains("procedure") || t.Contains("steps") || t.Contains("usage") ||
                t.Contains("how to") || t.Contains("流程") || t.Contains("步骤") || t.Contains("用法") || t.Contains("使用"))
            {
                headerIdx = i;
                break;
            }
        }

        var sb = new StringBuilder();

        var start = headerIdx >= 0 ? headerIdx + 1 : 0;
        for (var i = start; i < lines.Length; i++)
        {
            var l = lines[i];
            var trimmed = l.Trim();

            if (headerIdx >= 0 && i > headerIdx + 1 && (trimmed.StartsWith("##") || trimmed.StartsWith("###")))
                break;

            // Prefer list-like lines when we found a procedure header.
            if (headerIdx >= 0)
            {
                if (!(trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || Regex.IsMatch(trimmed, @"^\d+\.\s+")))
                {
                    // keep short paragraphs too
                    if (trimmed.Length == 0) continue;
                }
            }

            sb.AppendLine(l.TrimEnd());
            if (sb.Length >= maxChars)
                break;
        }

        var result = sb.ToString().Trim();
        if (result.Length > maxChars)
            result = result[..maxChars];
        return result;
    }

    private static (int Scripts, int References, int Assets) CountResourceDocsBestEffort(string skillDir, int maxFilesPerDir)
    {
        var root = skillDir;

        int Count(string sub)
        {
            try
            {
                var dir = Path.Combine(root, sub);
                if (!Directory.Exists(dir))
                    return 0;
                return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Take(maxFilesPerDir + 1).Count();
            }
            catch
            {
                return 0;
            }
        }

        var scripts = Count("scripts");
        var refs = Count("references");
        var assets = Count("assets");
        return (Math.Min(scripts, maxFilesPerDir), Math.Min(refs, maxFilesPerDir), Math.Min(assets, maxFilesPerDir));
    }

    private static string? FindContainingRoot(IReadOnlyList<string> roots, string path)
    {
        var full = Path.GetFullPath(path);
        string? best = null;
        foreach (var r in roots)
        {
            try
            {
                var rr = Path.GetFullPath(r);
                if (full.StartsWith(rr.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(full, rr, StringComparison.OrdinalIgnoreCase))
                {
                    if (best == null || rr.Length > best.Length)
                        best = rr;
                }
            }
            catch
            {
                // ignore
            }
        }
        return best;
    }
}


