using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions.CQRS;
using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Utils;
using Aevatar.Agents.AI.WithTool.Abstractions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.WithTool.Tools.BuiltIn;

/// <summary>
/// Memory search tool - built-in AI tool (simplified implementation)
/// <para/>
/// Search for relevant information in Agent's memory, including working memory, conversation history, and long-term memory
/// </summary>
[AevatarTool(
    Name = "search_memory",
    Description = "Search agent memory for relevant information across working memory, conversation history, and long-term memory",
    Category = ToolCategory.Core,
    Version = "1.0.0",
    AutoRegister = true,
    Tags = ["memory", "search", "recall"]
)]
public class AevatarMemorySearchTool : AevatarToolBase
{
    private readonly ILogger<AevatarMemorySearchTool> _logger;
    private readonly IStateQueryService? _stateQueryService;
    private readonly IMemoryStore? _memoryStore;
    private readonly IMemoryVectorIndex? _memoryVectorIndex;

    // Semantic rerank is best-effort and bounded (avoid hidden cost explosion).
    private const int SemanticMaxCandidates = 30;
    private const int SemanticMaxCharsPerCandidate = 2000;

    public AevatarMemorySearchTool(
        ILogger<AevatarMemorySearchTool> logger,
        IStateQueryService? stateQueryService = null,
        IMemoryStore? memoryStore = null,
        IMemoryVectorIndex? memoryVectorIndex = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _stateQueryService = stateQueryService;
        _memoryStore = memoryStore;
        _memoryVectorIndex = memoryVectorIndex;
    }

    public override string Name => "search_memory";
    public override string Description => "Search agent memory for relevant information";
    public override ToolCategory Category => ToolCategory.Core;
    public override string Version => "1.0.0";
    public override IList<string> Tags => new List<string> { "memory", "search", "recall" };

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Required = new[] { "query" },
            Items = new Dictionary<string, ToolParameter>
            {
                ["query"] = new ToolParameter
                {
                    Type = "string",
                    Description = "Search query to find relevant information in memory",
                    Required = true
                },
                ["maxResults"] = new ToolParameter
                {
                    Type = "integer",
                    Description = "Maximum number of results to return",
                    Required = false,
                    DefaultValue = 10
                },
                ["memoryType"] = new ToolParameter
                {
                    Type = "string",
                    Description = "Type of memory to search (all, working, conversation)",
                    Required = false,
                    DefaultValue = "all",
                    Enum = new[] { "all", "working", "conversation" }
                },
                ["memoryId"] = new ToolParameter
                {
                    Type = "string",
                    Description = "Optional memory resource id to search (e.g. privateagent::<agentId>, session::<sessionId>). If omitted, defaults to privateagent::<agentId>.",
                    Required = false
                }
            }
        };
    }

    public override async Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = parameters.GetValueOrDefault("query")?.ToString();
            var maxResultsObj = parameters.GetValueOrDefault("maxResults");
            var memoryType = parameters.GetValueOrDefault("memoryType")?.ToString() ?? "all";
            var memoryId = parameters.GetValueOrDefault("memoryId")?.ToString();

            if (string.IsNullOrWhiteSpace(query))
            {
                _logger.LogWarning("Search query is required but not provided");
                throw new ArgumentException("Search query is required");
            }

            // Parse maximum results count
            var maxResults = 10; // Default value
            if (maxResultsObj != null)
            {
                if (!int.TryParse(maxResultsObj.ToString(), out maxResults) || maxResults <= 0)
                {
                    _logger.LogWarning("Invalid maxResults value: {MaxResults}, using default 10", maxResultsObj);
                    maxResults = 10;
                }
            }

            // Validate memory type
            var validMemoryTypes = new[] { "all", "working", "conversation" };
            if (!validMemoryTypes.Contains(memoryType.ToLower()))
            {
                _logger.LogWarning("Invalid memory type: {MemoryType}, defaulting to 'all'", memoryType);
                memoryType = "all";
            }

            // ============================================================
            //  Real memory search (best-effort)
            //
            //  Priority:
            //  1) CQRS read-model (IStateQueryService): projected state snapshot (preferred when wired)
            //  2) State snapshot (ToolContext.GetStateCallback): current history window + rolling summary
            //
            //  NOTE:
            //  - If Memory is not wired yet, the tool still provides value by searching State.History + summary.
            // ============================================================
            var results = await SearchAsync(query, memoryType, maxResults, memoryId, context, cancellationToken);

            _logger.LogInformation("Memory search completed: {Query} found {Count} results in {MemoryType} memory",
                query, results.Count, memoryType);

            var resultObj = new
            {
                results,
                count = results.Count,
                query,
                memoryType,
                memoryId
            };
            
            var json = JsonSerializer.Serialize(resultObj);
            return JsonParser.Default.Parse<Struct>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Memory search failed for query: {Query}", parameters.GetValueOrDefault("query"));
            throw;
        }
    }

    public override ToolParameterValidationResult ValidateParameters(Dictionary<string, object?> parameters)
    {
        var result = new ToolParameterValidationResult { IsValid = true };

        if (!parameters.ContainsKey("query") || string.IsNullOrWhiteSpace(parameters["query"]?.ToString()))
        {
            result.IsValid = false;
            result.Errors.Add("Required parameter 'query' is missing or empty");
        }

        return result;
    }

    private async Task<List<MemoryItem>> SearchAsync(
        string query,
        string memoryType,
        int maxResults,
        string? memoryId,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        var allResults = new List<MemoryItem>();

        var type = memoryType.ToLowerInvariant();
        var includeConversation = type is "all" or "conversation";
        var includeWorking = type is "all" or "working";

        // ============================================================
        //  -1) Persistent long-term memory (MemoryStore + VectorIndex)
        // ============================================================
        if (includeWorking)
        {
            var effectiveMemoryId = BuildEffectiveMemoryId(context, memoryId);
            if (!string.IsNullOrWhiteSpace(effectiveMemoryId))
            {
                var addedVector = false;

                //  -1.1 Vector index (semantic, persistent)
                if (_memoryVectorIndex != null && context.GenerateEmbeddingsAsync != null)
                {
                    try
                    {
                        var vectors = await SearchVectorIndexAsync(
                            query,
                            maxResults,
                            effectiveMemoryId,
                            context,
                            cancellationToken);
                        if (vectors.Count > 0)
                        {
                            allResults.AddRange(vectors);
                            addedVector = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Vector-index memory search failed (best-effort)");
                    }
                }

                //  -1.2 MemoryStore fallback (lexical, persistent)
                if (!addedVector && _memoryStore != null)
                {
                    try
                    {
                        var lexical = await SearchMemoryStoreLexicalAsync(
                            query,
                            maxResults,
                            effectiveMemoryId,
                            cancellationToken);
                        if (lexical.Count > 0)
                        {
                            allResults.AddRange(lexical);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "MemoryStore-based search failed (best-effort)");
                    }
                }
            }
        }

        // 0) Projected state read-model (CQRS) - preferred when available
        // WHY:
        // - The CQRS index is built from OnStateChangedAsync projections.
        // - Tools can query it without binding to in-memory state structure.
        if ((includeConversation || includeWorking) &&
            _stateQueryService != null &&
            !string.IsNullOrWhiteSpace(context.AgentType) &&
            !string.IsNullOrWhiteSpace(context.AgentId))
        {
            try
            {
                // Prefer FTS-capable QueryAsync (best-effort).
                // - ES plugin supports Lucene query_string.
                // - In-memory/demo implementations may not parse Lucene; we fall back to GetByIdAsync.
                var cqrsResults = await SearchCqrsAsync(query, maxResults, context, cancellationToken);
                if (cqrsResults.Count > 0)
                {
                    allResults.AddRange(cqrsResults);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "CQRS-based memory search failed (best-effort)");
            }
        }

        // 1) Conversation/working memory from agent State snapshot
        if (includeConversation || includeWorking)
        {
            try
            {
                var state = context.GetStateCallback?.Invoke() as AevatarAIAgentState;
                if (state != null)
                {
                    // Rolling summary (Layer 2) lives in state.Context["history_summary"]
                    if (state.Context != null &&
                        state.Context.TryGetValue(AIGAgentKeys.HistorySummary, out var summary) &&
                        !string.IsNullOrWhiteSpace(summary) &&
                        summary.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        allResults.Add(new MemoryItem
                        {
                            Id = AIGAgentKeys.HistorySummary,
                            Type = "conversation_summary",
                            Content = summary,
                            Timestamp = DateTime.UtcNow,
                            Metadata = new Dictionary<string, object>
                            {
                                ["source"] = "state.context.history_summary"
                            }
                        });
                    }

                    // Recent history window (Layer 1) from state.History
                    if (state.History != null && state.History.Count > 0)
                    {
                        foreach (var msg in state.History)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var content = msg.Content ?? string.Empty;
                            if (string.IsNullOrWhiteSpace(content)) continue;
                            if (!content.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;

                            allResults.Add(new MemoryItem
                            {
                                Id = string.IsNullOrWhiteSpace(msg.Id) ? Guid.NewGuid().ToString("N") : msg.Id,
                                Type = "conversation",
                                Content = $"{msg.Role}: {content}",
                                Timestamp = msg.Timestamp?.ToDateTime() ?? DateTime.UtcNow,
                                Metadata = new Dictionary<string, object>
                                {
                                    ["role"] = msg.Role.ToString(),
                                    ["source"] = "state.history"
                                }
                            });
                        }
                    }

                    // Semantic fallback (state): only when no lexical hits are found anywhere.
                    // This keeps costs bounded and improves recall for paraphrased queries.
                    if (allResults.Count == 0)
                    {
                        var semantic = await SearchStateSemanticAsync(query, maxResults, context, state, cancellationToken);
                        if (semantic.Count > 0)
                        {
                            allResults.AddRange(semantic);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "State-based memory search failed (best-effort)");
            }
        }

        // Deduplicate + cap results (stable order: keep earlier matches first)
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var final = new List<MemoryItem>(Math.Min(maxResults, allResults.Count));
        foreach (var item in allResults)
        {
            if (final.Count >= maxResults) break;
            var key = $"{item.Type}:{item.Content}";
            if (!seen.Add(key)) continue;
            final.Add(item);
        }

        return final;
    }

    private static string BuildEffectiveMemoryId(ToolContext context, string? memoryId)
    {
        if (!string.IsNullOrWhiteSpace(memoryId))
            return memoryId.Trim();

        if (string.IsNullOrWhiteSpace(context.AgentId))
            return string.Empty;

        // Default: private agent scope
        var type = MemoryScopeType.PrivateAgent.ToString().ToLowerInvariant();
        return $"{type}::{context.AgentId.Trim()}";
    }

    private async Task<List<MemoryItem>> SearchVectorIndexAsync(
        string query,
        int maxResults,
        string memoryId,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        if (_memoryVectorIndex == null)
            return new List<MemoryItem>();

        var embed = context.GenerateEmbeddingsAsync;
        if (embed == null)
            return new List<MemoryItem>();

        var embeddings = await embed(new[] { query }, cancellationToken);
        if (embeddings == null || embeddings.Count == 0)
            return new List<MemoryItem>();

        var queryVec = embeddings[0].Vector.ToArray();
        var matches = await _memoryVectorIndex.SearchAsync(
            queryVec,
            limit: maxResults,
            memoryId: memoryId,
            ct: cancellationToken);

        if (matches.Count == 0)
            return new List<MemoryItem>();

        var list = new List<MemoryItem>(capacity: Math.Min(maxResults, matches.Count));
        foreach (var m in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var r = m.Record;
            var content = r.Content ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(r.Role))
            {
                content = $"{r.Role}: {content}";
            }

            list.Add(new MemoryItem
            {
                Id = string.IsNullOrWhiteSpace(r.EntryId) ? Guid.NewGuid().ToString("N") : r.EntryId,
                Type = "memory_vector",
                Content = content,
                Timestamp = r.CreatedAt?.ToDateTime() ?? DateTime.UtcNow,
                Metadata = new Dictionary<string, object>
                {
                    ["source"] = "memory.vector_index",
                    ["ranking"] = "vector",
                    ["similarity"] = m.Similarity,
                    ["memoryId"] = r.MemoryId ?? string.Empty,
                    ["entryId"] = r.EntryId ?? string.Empty,
                    ["scopeType"] = r.Scope?.Type.ToString() ?? string.Empty,
                    ["scopeId"] = r.Scope?.ScopeId ?? string.Empty
                }
            });
        }

        return list;
    }

    private async Task<List<MemoryItem>> SearchMemoryStoreLexicalAsync(
        string query,
        int maxResults,
        string memoryId,
        CancellationToken cancellationToken)
    {
        if (_memoryStore == null)
            return new List<MemoryItem>();

        var entries = await _memoryStore.SearchAsync(query, limit: maxResults, memoryId: memoryId, ct: cancellationToken);
        if (entries.Count == 0)
            return new List<MemoryItem>();

        var list = new List<MemoryItem>(capacity: Math.Min(maxResults, entries.Count));
        foreach (var e in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var role = e.Role ?? string.Empty;
            var content = e.Content ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(role))
            {
                content = $"{role}: {content}";
            }

            list.Add(new MemoryItem
            {
                Id = string.IsNullOrWhiteSpace(e.EntryId) ? Guid.NewGuid().ToString("N") : e.EntryId,
                Type = "memory_store",
                Content = content,
                Timestamp = e.CreatedAt?.ToDateTime() ?? DateTime.UtcNow,
                Metadata = new Dictionary<string, object>
                {
                    ["source"] = "memory.store",
                    ["ranking"] = "lexical",
                    ["memoryId"] = e.MemoryId ?? string.Empty,
                    ["entryId"] = e.EntryId ?? string.Empty,
                    ["scopeType"] = e.Scope?.Type.ToString() ?? string.Empty,
                    ["scopeId"] = e.Scope?.ScopeId ?? string.Empty
                }
            });
        }

        return list;
    }

    // ============================================================
    //  CQRS (FTS-first) search
    // ============================================================

    private async Task<List<MemoryItem>> SearchCqrsAsync(
        string query,
        int maxResults,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        if (_stateQueryService == null)
            return new List<MemoryItem>();

        if (string.IsNullOrWhiteSpace(context.AgentType) || string.IsNullOrWhiteSpace(context.AgentId))
            return new List<MemoryItem>();

        // 1) Try FTS query: (agentId:"<id>") AND "<query>"
        // This works well for ES QueryStringQuery and stays safe (no leading wildcard).
        try
        {
            var agentId = context.AgentId.Trim();
            var q = query.Trim();
            var queryString = BuildAgentScopedLuceneQuery(agentId, q);

            var doc = await TryQuerySingleAgentDocAsync(
                context.AgentType,
                queryString,
                agentId,
                pageSize: 1,
                cancellationToken);

            // 1.1) If the CQRS backend doesn't understand Lucene syntax (e.g. demo in-memory),
            // try a plain query (best-effort), then filter by agentId.
            if (doc == null)
            {
                var plain = BuildPlainQueryString(q);
                doc = await TryQuerySingleAgentDocAsync(
                    context.AgentType,
                    plain,
                    agentId,
                    pageSize: 50,
                    cancellationToken);
            }

            if (doc?.Data != null)
            {
                var matches = ExtractFieldMatches(
                    doc.Data,
                    query,
                    maxResults,
                    context,
                    source: "cqrs.IStateQueryService.QueryAsync");

                if (matches.Count > 0)
                    return matches;

                // Semantic fallback (CQRS): can surface relevant fields even without substring match.
                var semantic = await SearchCqrsSemanticAsync(
                    query,
                    maxResults,
                    context,
                    doc,
                    source: "cqrs.semantic(QueryAsync)",
                    cancellationToken);

                if (semantic.Count > 0)
                    return semantic;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CQRS QueryAsync (FTS) failed (best-effort)");
        }

        // 2) Fallback: GetByIdAsync + substring match (old behavior)
        try
        {
            var stateDoc = await _stateQueryService.GetByIdAsync(
                context.AgentType,
                context.AgentId,
                cancellationToken);

            if (stateDoc?.Data == null)
                return new List<MemoryItem>();

            var lexical = ExtractFieldMatches(
                stateDoc.Data,
                query,
                maxResults,
                context,
                source: "cqrs.IStateQueryService.GetByIdAsync");

            if (lexical.Count > 0)
                return lexical;

            // Semantic fallback even when GetByIdAsync returns a doc but no lexical match.
            var semantic = await SearchCqrsSemanticAsync(
                query,
                maxResults,
                context,
                stateDoc,
                source: "cqrs.semantic(GetByIdAsync)",
                cancellationToken);

            return semantic;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CQRS GetByIdAsync fallback failed (best-effort)");
            return new List<MemoryItem>();
        }
    }

    // ============================================================
    //  Semantic search / rerank (best-effort)
    // ============================================================

    private async Task<List<MemoryItem>> SearchCqrsSemanticAsync(
        string query,
        int maxResults,
        ToolContext context,
        StateQueryResult doc,
        string source,
        CancellationToken cancellationToken)
    {
        var embed = context.GenerateEmbeddingsAsync;
        if (embed == null)
            return new List<MemoryItem>();

        if (doc.Data == null || doc.Data.Count == 0)
            return new List<MemoryItem>();

        // Build candidate fields (bounded).
        var candidates = SelectSemanticCandidatesFromCqrs(doc.Data)
            .Take(SemanticMaxCandidates)
            .ToList();

        if (candidates.Count == 0)
            return new List<MemoryItem>();

        var inputs = new List<string>(1 + candidates.Count)
        {
            NormalizeForEmbedding(query)
        };

        inputs.AddRange(candidates.Select(c => NormalizeForEmbedding(c.Text)));

        IReadOnlyList<Embedding<float>> embeddings;
        try
        {
            embeddings = await embed(inputs, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Embedding generation failed for CQRS semantic search (best-effort)");
            return new List<MemoryItem>();
        }

        if (embeddings.Count != inputs.Count)
            return new List<MemoryItem>();

        var queryEmbedding = embeddings[0];
        var scored = new List<(int Index, double Similarity)>(candidates.Count);
        for (var i = 0; i < candidates.Count; i++)
        {
            var sim = CosineSimilarity(queryEmbedding, embeddings[i + 1]);
            scored.Add((i, sim));
        }

        var top = scored
            .OrderByDescending(x => x.Similarity)
            .Take(Math.Clamp(maxResults, 1, 200))
            .ToList();

        var results = new List<MemoryItem>(top.Count);
        foreach (var (idx, sim) in top)
        {
            var c = candidates[idx];
            results.Add(new MemoryItem
            {
                Id = $"cqrs:{c.Field}",
                Type = "cqrs_state",
                Content = $"{c.Field}: {BuildSnippet(c.Text, query)}",
                Timestamp = DateTime.UtcNow,
                Metadata = new Dictionary<string, object>
                {
                    ["source"] = source,
                    ["ranking"] = "semantic",
                    ["similarity"] = sim,
                    ["agentType"] = context.AgentType ?? string.Empty,
                    ["agentId"] = context.AgentId ?? string.Empty,
                    ["field"] = c.Field
                }
            });
        }

        return results;
    }

    private async Task<List<MemoryItem>> SearchStateSemanticAsync(
        string query,
        int maxResults,
        ToolContext context,
        AevatarAIAgentState state,
        CancellationToken cancellationToken)
    {
        var embed = context.GenerateEmbeddingsAsync;
        if (embed == null)
            return new List<MemoryItem>();

        // Build candidate texts from summary + recent messages.
        var candidates = new List<(string Id, string Type, string Display, string EmbedText, Dictionary<string, object> Meta)>();

        // Summary first (if any)
        if (state.Context != null &&
            state.Context.TryGetValue(AIGAgentKeys.HistorySummary, out var summary) &&
            !string.IsNullOrWhiteSpace(summary))
        {
            candidates.Add((
                Id: AIGAgentKeys.HistorySummary,
                Type: "conversation_summary",
                Display: summary,
                EmbedText: summary,
                Meta: new Dictionary<string, object> { ["source"] = "state.context.history_summary", ["ranking"] = "semantic" }));
        }

        // Recent history window (tail only)
        if (state.History != null && state.History.Count > 0)
        {
            var start = Math.Max(0, state.History.Count - SemanticMaxCandidates);
            for (var i = start; i < state.History.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var msg = state.History[i];
                var content = msg.Content ?? string.Empty;
                if (string.IsNullOrWhiteSpace(content)) continue;

                var id = string.IsNullOrWhiteSpace(msg.Id) ? Guid.NewGuid().ToString("N") : msg.Id;
                var role = msg.Role.ToString();
                candidates.Add((
                    Id: id,
                    Type: "conversation",
                    Display: $"{role}: {content}",
                    EmbedText: content,
                    Meta: new Dictionary<string, object> { ["source"] = "state.history", ["role"] = role, ["ranking"] = "semantic" }));
            }
        }

        if (candidates.Count == 0)
            return new List<MemoryItem>();

        // Hard cap (avoid large cost if history is huge).
        if (candidates.Count > SemanticMaxCandidates)
        {
            candidates = candidates.Take(SemanticMaxCandidates).ToList();
        }

        var inputs = new List<string>(1 + candidates.Count)
        {
            NormalizeForEmbedding(query)
        };
        inputs.AddRange(candidates.Select(c => NormalizeForEmbedding(c.EmbedText)));

        IReadOnlyList<Embedding<float>> embeddings;
        try
        {
            embeddings = await embed(inputs, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Embedding generation failed for state semantic search (best-effort)");
            return new List<MemoryItem>();
        }

        if (embeddings.Count != inputs.Count)
            return new List<MemoryItem>();

        var queryEmbedding = embeddings[0];
        var scored = new List<(int Index, double Similarity)>(candidates.Count);
        for (var i = 0; i < candidates.Count; i++)
        {
            var sim = CosineSimilarity(queryEmbedding, embeddings[i + 1]);
            scored.Add((i, sim));
        }

        var top = scored
            .OrderByDescending(x => x.Similarity)
            .Take(Math.Clamp(maxResults, 1, 200))
            .ToList();

        var results = new List<MemoryItem>(top.Count);
        foreach (var (idx, sim) in top)
        {
            var c = candidates[idx];
            var meta = new Dictionary<string, object>(c.Meta)
            {
                ["similarity"] = sim
            };

            results.Add(new MemoryItem
            {
                Id = c.Id,
                Type = c.Type,
                Content = BuildSnippet(c.Display, query),
                Timestamp = DateTime.UtcNow,
                Metadata = meta
            });
        }

        return results;
    }

    private static IEnumerable<(string Field, string Text)> SelectSemanticCandidatesFromCqrs(
        Dictionary<string, object?> data)
    {
        // Avoid obvious system/meta fields.
        var ignore = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "agentId", "agentType", "version", "indexedAt"
        };

        foreach (var (field, value) in data)
        {
            if (ignore.Contains(field)) continue;
            if (value is not string s) continue;
            if (string.IsNullOrWhiteSpace(s)) continue;

            // Prefer explicitly generated search fields.
            if (field.EndsWith("Text", StringComparison.OrdinalIgnoreCase) ||
                field.Equals("historySummary", StringComparison.OrdinalIgnoreCase) ||
                field.Contains("summary", StringComparison.OrdinalIgnoreCase))
            {
                yield return (field, s);
                continue;
            }

            // Otherwise include only compact strings (avoid giant JSON blobs).
            if (s.Length <= 800)
            {
                yield return (field, s);
            }
        }
    }

    private static string NormalizeForEmbedding(string text)
    {
        var t = NormalizeWhitespace(text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(t))
            return string.Empty;

        if (t.Length <= SemanticMaxCharsPerCandidate)
            return t;

        // Keep tail (usually closer to "latest memory" semantics).
        return t[^SemanticMaxCharsPerCandidate..];
    }

    private static double CosineSimilarity(Embedding<float> left, Embedding<float> right)
    {
        var leftSpan = left.Vector.Span;
        var rightSpan = right.Vector.Span;

        if (leftSpan.Length != rightSpan.Length || leftSpan.Length == 0)
            return 0;

        double dot = 0;
        double magLeft = 0;
        double magRight = 0;

        for (var i = 0; i < leftSpan.Length; i++)
        {
            var l = leftSpan[i];
            var r = rightSpan[i];
            dot += l * r;
            magLeft += l * l;
            magRight += r * r;
        }

        if (magLeft == 0 || magRight == 0)
            return 0;

        return dot / (Math.Sqrt(magLeft) * Math.Sqrt(magRight));
    }

    private async Task<StateQueryResult?> TryQuerySingleAgentDocAsync(
        string agentType,
        string queryString,
        string agentId,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(agentType) || string.IsNullOrWhiteSpace(queryString))
            return null;

        var query = new StateQuery
        {
            AgentType = agentType,
            QueryString = queryString,
            PageIndex = 0,
            PageSize = Math.Clamp(pageSize, 1, 200)
        };

        var resp = await _stateQueryService!.QueryAsync(query, cancellationToken);
        if (resp?.Items == null || resp.Items.Count == 0)
            return null;

        // Prefer exact agent id match (QueryAsync may return multiple docs).
        return resp.Items.FirstOrDefault(i =>
            string.Equals(i.AgentId, agentId, StringComparison.Ordinal));
    }

    private static string BuildAgentScopedLuceneQuery(string agentId, string query)
    {
        var safeAgentId = EscapeLucenePhrase(agentId);
        var safeQuery = EscapeLucenePhrase(NormalizeWhitespace(query));
        return $"agentId:\"{safeAgentId}\" AND \"{safeQuery}\"";
    }

    private static string BuildPlainQueryString(string query)
    {
        // For non-Lucene implementations: treat query as a plain substring token.
        // Keep it short and whitespace-normalized to reduce noise.
        return NormalizeWhitespace(query);
    }

    private static string NormalizeWhitespace(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return string.Empty;

        // Collapse whitespace (including newlines) to single spaces.
        return string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string EscapeLucenePhrase(string s)
    {
        // We always wrap in quotes; only need to escape backslash and double quotes.
        return (s ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
    }

    private static List<MemoryItem> ExtractFieldMatches(
        Dictionary<string, object?> data,
        string query,
        int maxResults,
        ToolContext context,
        string source)
    {
        var q = query?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(q) || maxResults <= 0)
            return new List<MemoryItem>();

        var candidates = new List<(string Field, string Text, int Score, int FirstIdx)>();

        foreach (var (field, value) in data)
        {
            if (value is not string s) continue;
            if (string.IsNullOrWhiteSpace(s)) continue;

            var idx = s.IndexOf(q, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;

            var score = CountOccurrences(s, q);
            candidates.Add((field, s, score, idx));
        }

        // Rank: more hits first, then earlier occurrence, then shorter field value (usually more focused).
        var ordered = candidates
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.FirstIdx)
            .ThenBy(x => x.Text.Length)
            .Take(Math.Clamp(maxResults, 1, 200))
            .ToList();

        var results = new List<MemoryItem>(ordered.Count);
        foreach (var c in ordered)
        {
            results.Add(new MemoryItem
            {
                Id = $"cqrs:{c.Field}",
                Type = "cqrs_state",
                Content = $"{c.Field}: {BuildSnippet(c.Text, q)}",
                Timestamp = DateTime.UtcNow,
                Metadata = new Dictionary<string, object>
                {
                    ["source"] = source,
                    ["agentType"] = context.AgentType ?? string.Empty,
                    ["agentId"] = context.AgentId ?? string.Empty,
                    ["field"] = c.Field,
                    ["score"] = c.Score
                }
            });
        }

        return results;
    }

    private static int CountOccurrences(string text, string query)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query))
            return 0;

        var count = 0;
        var idx = 0;
        while (true)
        {
            idx = text.IndexOf(query, idx, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) break;
            count++;
            idx += Math.Max(1, query.Length);
        }
        return count;
    }

    private static string BuildSnippet(string content, string query, int maxChars = 400)
    {
        if (string.IsNullOrEmpty(content) || maxChars <= 0)
            return string.Empty;

        if (content.Length <= maxChars)
            return content;

        var idx = content.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return content[..maxChars] + "...";
        }

        var half = maxChars / 2;
        var start = Math.Max(0, idx - half);
        var len = Math.Min(maxChars, content.Length - start);
        var snippet = content.Substring(start, len);

        if (start > 0) snippet = "..." + snippet;
        if (start + len < content.Length) snippet += "...";

        return snippet;
    }
}

/// <summary>
/// Memory item
/// </summary>
public class MemoryItem
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}
