using System.Text;
using System.Text.RegularExpressions;
using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

public abstract partial class AIGAgentBase
{
    // ============================================================
    //  Agent Skills: dynamic search + document read (no eager load)
    //
    //  Goal:
    //  - Avoid dumping a full skill inventory into LLM context every turn.
    //  - Provide a "search first" tool that returns only a few candidates.
    //  - Provide pattern-based document reading (scripts/references/assets) without executing code.
    // ============================================================

    private async Task RegisterAgentSkillsDynamicReadToolsAsync(CancellationToken cancellationToken = default)
    {
        if (!EnableAgentSkills)
            return;

        EnsureToolManagerInitialized();

        // find_helpful_skills
        var findTool = new ToolDefinition
        {
            Name = "find_helpful_skills",
            Description =
                "Search Agent Skills by query and return ranked candidates with brief guidance. Uses embeddings when configured; otherwise falls back to lexical matching. Prefer this over listing all skills.",
            Category = ToolCategory.Core,
            Version = "1.0.0",
            Tags = new List<string> { "skills", "agent-skills", "search", "semantic", "discovery" },
            RequiresInternalAccess = true,
            IsDangerous = false,
            CanBeOverridden = true,
            Parameters = new ToolParameters
            {
                Items = new Dictionary<string, ToolParameter>
                {
                    ["query"] = new()
                    {
                        Type = "string",
                        Required = true,
                        Description = "User request or keywords to search for"
                    },
                    ["max_results"] = new()
                    {
                        Type = "integer",
                        Required = false,
                        Description = "Max candidates to return (default: 5; range 1..10)",
                        DefaultValue = 5
                    },
                    ["max_chars_per_skill"] = new()
                    {
                        Type = "integer",
                        Required = false,
                        Description = "Max chars of guidance snippet per skill (default: 1200; range 200..6000)",
                        DefaultValue = 1200
                    }
                },
                Required = new[] { "query" }
            },
            ExecuteAsync = async (parameters, executionContext, ct) =>
                await ExecuteFindHelpfulSkillsToolAsync(parameters, executionContext, ct)
        };

        // list_skills (debug/exploration)
        var listTool = new ToolDefinition
        {
            Name = "list_skills",
            Description =
                "List the full inventory of available skills (debug/exploration). For task-driven work, prefer find_helpful_skills.",
            Category = ToolCategory.Core,
            Version = "1.0.0",
            Tags = new List<string> { "skills", "agent-skills", "filesystem", "inventory" },
            RequiresInternalAccess = true,
            IsDangerous = false,
            CanBeOverridden = true,
            Parameters = new ToolParameters(),
            ExecuteAsync = async (_, executionContext, ct) =>
                await ExecuteListSkillsToolAsync(executionContext, ct)
        };

        // read_skill_document
        var readDocTool = new ToolDefinition
        {
            Name = "read_skill_document",
            Description =
                "Read text documents inside a skill folder by pattern (e.g., scripts/*.py, references/**/*.md). Never executes code.",
            Category = ToolCategory.Core,
            Version = "1.0.0",
            Tags = new List<string> { "skills", "agent-skills", "filesystem", "read", "glob" },
            RequiresInternalAccess = true,
            IsDangerous = false,
            CanBeOverridden = true,
            Parameters = new ToolParameters
            {
                Items = new Dictionary<string, ToolParameter>
                {
                    ["name"] = new()
                    {
                        Type = "string",
                        Required = true,
                        Description = "Skill name (from SKILL.md front matter 'name') or folder name"
                    },
                    ["pattern"] = new()
                    {
                        Type = "string",
                        Required = true,
                        Description = "Glob pattern relative to skill root, e.g. 'scripts/*.py' or 'references/**/*.md'"
                    },
                    ["max_files"] = new()
                    {
                        Type = "integer",
                        Required = false,
                        Description = "Max files to return (default: 6; range 1..20)",
                        DefaultValue = 6
                    },
                    ["max_chars_per_file"] = new()
                    {
                        Type = "integer",
                        Required = false,
                        Description = "Max chars per file (default: 8000; range 200..50000)",
                        DefaultValue = 8000
                    },
                    ["total_max_chars"] = new()
                    {
                        Type = "integer",
                        Required = false,
                        Description = "Total output char budget for all files combined (default: 16000; range 1000..200000)",
                        DefaultValue = 16000
                    },
                    ["restrict_to_resource_dirs"] = new()
                    {
                        Type = "boolean",
                        Required = false,
                        Description = "If true, only allow reading under scripts/, references/, assets/ (default: true).",
                        DefaultValue = true
                    }
                },
                Required = new[] { "name", "pattern" }
            },
            ExecuteAsync = async (parameters, executionContext, ct) =>
                await ExecuteReadSkillDocumentToolAsync(parameters, executionContext, ct)
        };

        await ToolManager.RegisterToolAsync(findTool, cancellationToken);
        await ToolManager.RegisterToolAsync(listTool, cancellationToken);
        await ToolManager.RegisterToolAsync(readDocTool, cancellationToken);
    }

    private async Task<IMessage> ExecuteListSkillsToolAsync(
        ToolExecutionContext? executionContext,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        var roots = GetEffectiveAgentSkillsRoots();
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

    private async Task<IMessage> ExecuteFindHelpfulSkillsToolAsync(
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

        var roots = GetEffectiveAgentSkillsRoots();
        var skills = DiscoverAgentSkills(roots, cancellationToken);

        // Prefer semantic (embeddings) ranking when available; fall back to lexical.
        var ranked = await TryRankSkillsByEmbeddingsAsync(query, roots, skills, maxResults, cancellationToken);
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

    private async Task<IMessage> ExecuteReadSkillDocumentToolAsync(
        Dictionary<string, object> parameters,
        ToolExecutionContext? executionContext,
        CancellationToken cancellationToken)
    {
        var name = parameters.GetValueOrDefault("name")?.ToString();
        var pattern = parameters.GetValueOrDefault("pattern")?.ToString();

        if (string.IsNullOrWhiteSpace(name))
            return ToStruct(new { success = false, error = "Parameter 'name' is required." });
        if (string.IsNullOrWhiteSpace(pattern))
            return ToStruct(new { success = false, error = "Parameter 'pattern' is required." });

        var maxFiles = ClampInt(parameters.GetValueOrDefault("max_files"), fallback: 6, min: 1, max: 20);
        var maxCharsPerFile = ClampInt(parameters.GetValueOrDefault("max_chars_per_file"), fallback: 8000, min: 200, max: 50_000);
        var totalMaxChars = ClampInt(parameters.GetValueOrDefault("total_max_chars"), fallback: 16_000, min: 1_000, max: 200_000);
        var restrictToResourceDirs = TryGetBool(parameters.GetValueOrDefault("restrict_to_resource_dirs"), fallback: true);

        var match = FindSkillByName(name.Trim(), cancellationToken);
        if (match == null)
            return ToStruct(new { success = false, error = $"Skill '{name}' not found." });

        var skillDir = Path.GetFullPath(match.DirectoryPath);
        var rawPattern = NormalizeRel(pattern!);

        // Fast path: no glob => single file
        if (!LooksLikeGlob(rawPattern))
        {
            if (restrictToResourceDirs && !IsUnderResourceDirs(rawPattern))
                return ToStruct(new { success = false, error = "Path is outside scripts/references/assets (restricted by policy)." });

            var file = ResolvePathUnderRoot(skillDir, rawPattern);
            if (file == null)
                return ToStruct(new { success = false, error = "Invalid path (path traversal denied)." });
            if (!File.Exists(file))
                return ToStruct(new { success = false, error = $"File not found: {rawPattern}", skill = match.Name });

            var content = await ReadAllTextWithLimitAsync(file, Math.Min(maxCharsPerFile, totalMaxChars), cancellationToken);
            long? sizeBytes = null;
            try { sizeBytes = new FileInfo(file).Length; } catch { /* ignore */ }

            return ToStruct(new
            {
                success = true,
                name = match.Name,
                pattern = rawPattern,
                matched = 1,
                returned = 1,
                files = new[]
                {
                    new
                    {
                        path = rawPattern,
                        sizeBytes,
                        truncated = content.Length >= Math.Min(maxCharsPerFile, totalMaxChars),
                        content
                    }
                }
            });
        }

        // Glob path: enumerate + match
        IEnumerable<string> allFiles;
        try
        {
            allFiles = Directory.EnumerateFiles(skillDir, "*", SearchOption.AllDirectories);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to enumerate skill files (best-effort).");
            return ToStruct(new { success = false, error = ex.Message });
        }

        var matcher = BuildGlobRegex(rawPattern);
        var selected = new List<(string Rel, string Full)>();

        foreach (var f in allFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rel = NormalizeRel(Path.GetRelativePath(skillDir, f));

            if (restrictToResourceDirs && !IsUnderResourceDirs(rel))
                continue;

            if (matcher.IsMatch(rel))
            {
                selected.Add((rel, f));
            }
        }

        selected = selected
            .OrderBy(x => x.Rel, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var files = new List<object>();
        var skipped = new List<object>();
        var returned = 0;
        var usedChars = 0;
        var truncatedAny = false;

        foreach (var (rel, full) in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (returned >= maxFiles)
            {
                truncatedAny = true;
                break;
            }

            // crude binary check: only read small UTF-8 text-ish files
            if (LooksLikeBinaryByExtension(rel))
            {
                skipped.Add(new { path = rel, reason = "binary_or_unsupported_extension" });
                continue;
            }

            var remaining = totalMaxChars - usedChars;
            if (remaining <= 0)
            {
                truncatedAny = true;
                break;
            }

            var perFileBudget = Math.Min(maxCharsPerFile, remaining);

            string content;
            try
            {
                content = await ReadAllTextWithLimitAsync(full, perFileBudget, cancellationToken);
            }
            catch (Exception ex)
            {
                skipped.Add(new { path = rel, reason = ex.Message });
                continue;
            }

            long? sizeBytes = null;
            try { sizeBytes = new FileInfo(full).Length; } catch { /* ignore */ }

            usedChars += content.Length;
            returned++;

            files.Add(new
            {
                path = rel,
                sizeBytes,
                truncated = content.Length >= perFileBudget,
                content
            });
        }

        return ToStruct(new
        {
            success = true,
            name = match.Name,
            pattern = rawPattern,
            matched = selected.Count,
            returned,
            truncated = truncatedAny,
            totalChars = usedChars,
            files,
            skipped
        });
    }

    private static bool LooksLikeGlob(string pattern)
        => pattern.Contains('*') || pattern.Contains('?');

    private static string NormalizeRel(string rel)
        => rel.Replace('\\', '/').TrimStart('/');

    private static bool IsUnderResourceDirs(string rel)
        => rel.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase) ||
           rel.StartsWith("references/", StringComparison.OrdinalIgnoreCase) ||
           rel.StartsWith("assets/", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeBinaryByExtension(string rel)
    {
        var ext = Path.GetExtension(rel);
        if (string.IsNullOrWhiteSpace(ext)) return false;
        ext = ext.ToLowerInvariant();

        return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".pdf" or ".zip" or ".gz" or ".tar" or ".7z" or ".mp4" or ".mov";
    }

    private static Regex BuildGlobRegex(string pattern)
    {
        // Simple glob:
        // - ** matches any chars including '/'
        // - * matches any chars except '/'
        // - ? matches one char except '/'
        var sb = new StringBuilder();
        sb.Append("^");

        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];

            if (c == '*')
            {
                var isDouble = i + 1 < pattern.Length && pattern[i + 1] == '*';
                if (isDouble)
                {
                    sb.Append(".*");
                    i++;
                }
                else
                {
                    sb.Append("[^/]*");
                }
                continue;
            }

            if (c == '?')
            {
                sb.Append("[^/]");
                continue;
            }

            sb.Append(Regex.Escape(c.ToString()));
        }

        sb.Append("$");
        return new Regex(sb.ToString(), RegexOptions.Compiled | RegexOptions.IgnoreCase);
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
            Logger.LogDebug(ex, "Failed to extract guidance snippet (best-effort).");
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


