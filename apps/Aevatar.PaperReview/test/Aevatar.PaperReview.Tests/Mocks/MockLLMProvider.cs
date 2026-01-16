using System.Runtime.CompilerServices;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;

namespace Aevatar.PaperReview.Tests.Mocks;

// ============================================================
//  MOCK LLM PROVIDER
//  Simulates LLM responses for testing Paper Review workflow
// ============================================================

/// <summary>
/// Mock LLM Provider - Returns preset responses based on prompt content.
/// Supports all stages of the MAKER workflow.
/// </summary>
public sealed class MockLLMProvider : IAevatarLLMProvider
{
    private readonly Dictionary<string, string> _responses = new();
    private readonly List<AevatarLLMRequest> _requestHistory = [];
    private int _callCount;

    /// <summary>
    /// History of all received requests.
    /// </summary>
    public IReadOnlyList<AevatarLLMRequest> RequestHistory => _requestHistory;

    /// <summary>
    /// Number of LLM calls made.
    /// </summary>
    public int CallCount => _callCount;

    /// <summary>
    /// Adds a keyword-based response rule.
    /// </summary>
    public MockLLMProvider WithResponse(string promptKeyword, string response)
    {
        _responses[promptKeyword.ToLowerInvariant()] = response;
        return this;
    }

    /// <summary>
    /// Configures standard responses for MAKER workflow.
    /// </summary>
    public MockLLMProvider WithMakerWorkflowResponses()
    {
        // check_atomic: Determine if task is atomic
        _responses["atomic"] = """
            {
                "is_atomic": false,
                "reason": "The paper review task requires multiple dimensions of analysis",
                "suggested_subtasks": ["technical_analysis", "novelty_assessment", "presentation_quality"]
            }
            """;

        // decompose: Break down task
        // Note: Real prompts may not contain "decompose" keyword, use more stable phrase matching.
        _responses["break down the following complex task"] = """
            [
                {"id": "tech", "description": "Analyze technical soundness and methodology"},
                {"id": "novelty", "description": "Assess novelty and contribution"},
                {"id": "presentation", "description": "Evaluate clarity and presentation"}
            ]
            """;

        // solve: Solve atomic task
        _responses["solve"] = """
            ## Analysis Result
            
            The paper presents a well-structured approach with solid methodology.
            
            **Strengths:**
            - Clear problem formulation
            - Sound experimental design
            - Comprehensive evaluation
            
            **Weaknesses:**
            - Limited discussion of limitations
            - Could benefit from more baselines
            
            **Score: 7/10**
            """;

        // compose: Compose results
        _responses["compose"] = """
            # Paper Review Summary
            
            ## Overall Assessment
            This paper makes a meaningful contribution to the field.
            
            ## Technical Soundness: 7/10
            The methodology is sound with minor concerns.
            
            ## Novelty: 6/10
            Incremental but solid contribution.
            
            ## Presentation: 8/10
            Well-written and clear.
            
            ## Recommendation: Weak Accept
            """;

        // vote: Voting
        _responses["vote"] = """
            {
                "selected": 0,
                "reason": "This proposal provides the most comprehensive analysis"
            }
            """;

        // Generic review response
        _responses["review"] = """
            ## Paper Review
            
            **Summary:** This paper addresses an important problem with a novel approach.
            
            **Strengths:**
            1. Clear motivation and problem statement
            2. Sound technical approach
            3. Comprehensive experiments
            
            **Weaknesses:**
            1. Limited theoretical analysis
            2. Missing ablation studies
            
            **Questions for Authors:**
            1. How does the method scale to larger datasets?
            2. What are the computational requirements?
            
            **Overall Score: 6/10**
            **Confidence: 4/5**
            **Recommendation: Borderline Accept**
            """;

        return this;
    }

    public Task<AevatarLLMResponse> GenerateAsync(
        AevatarLLMRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        _requestHistory.Add(request);

        // ============================================================
        //  IMPORTANT: Extract actual prompt from Messages
        //
        //  WHY:
        //  - AIGAgentBase.BuildLLMRequest() primarily uses request.Messages to carry conversation content,
        //    request.UserPrompt is often empty (legacy compatibility field for some providers).
        //  - If we only read UserPrompt, this Mock will only "see" generic words from system prompt (compose/vote/...),
        //    causing incorrect response format matching, triggering maker-v2's strict_parse -> redflag-parse-null.
        // ============================================================
        var promptText = ExtractPromptText(request);
        var stageKey = NormalizeStageHint(TryGetStageHint(request));
        var prompt = (promptText + " " + (request.SystemPrompt ?? "")).ToLowerInvariant();

        // ============================================================
        //  Structured prompts MUST win over generic keywords
        //
        //  NOTE:
        //  - Maker-v2's generator prompt embeds original task (contains generic words like "review")
        //  - If we rely on Dictionary iteration's "accidental order" for Contains matching, generic words easily match first
        //  - Explicitly prioritize: match structured output (json/json_array) first, then fall back to keyword table
        // ============================================================

        AevatarLLMResponse Build(string content) => new()
        {
            Content = content,
            AevatarStopReason = AevatarStopReason.Complete,
            Usage = new AevatarTokenUsage
            {
                PromptTokens = prompt.Length / 4,
                CompletionTokens = content.Length / 4,
                TotalTokens = (prompt.Length + content.Length) / 4
            },
            ModelName = "mock-model"
        };

        // ============================================================
        //  Deterministic routing via stage_hint (preferred)
        //
        //  WHY:
        //  - Contains-based routing is inherently brittle:
        //    system prompt often contains generic words (review/compose/vote),
        //    and dictionary iteration order can accidentally pick the wrong response.
        //  - stage_hint is injected by Cognitive agents per step id, so it's the single source of truth.
        // ============================================================
        if (stageKey != null)
        {
            switch (stageKey)
            {
                case "check_atomic":
                    return Task.FromResult(Build(BuildCheckAtomicJson(promptText)));
                case "decompose":
                    return Task.FromResult(Build(_responses["break down the following complex task"]));
                case "solve_atomic":
                    return Task.FromResult(Build(_responses["solve"]));
                case "compose":
                    return Task.FromResult(Build(_responses["compose"]));
            }
        }

        // ─────────────────────────────────────────────────────────
        // check_atomic: Requires "terminable recursion" behavior
        // - Top-level paper review: Mark as COMPLEX (triggers decomposition)
        // - Subtasks (e.g., "Assess novelty ..."): Mark as ATOMIC (triggers solve), avoid infinite recursion
        //
        // Distinguish from other step prompts by detecting check_atomic-specific field is_atomic.
        // ─────────────────────────────────────────────────────────
        if (prompt.Contains("\"is_atomic\"") || prompt.Contains("is_atomic"))
        {
            return Task.FromResult(Build(BuildCheckAtomicJson(promptText)));
        }

        // decompose generator (json_array)
        if (prompt.Contains("break down the following complex task") &&
            (prompt.Contains("json array") || prompt.Contains("format your response as a json array")))
        {
            return Task.FromResult(Build(_responses["break down the following complex task"]));
        }

        // solve_atomic generator (text)
        if (prompt.Contains("solve the following task directly"))
        {
            return Task.FromResult(Build(_responses["solve"]));
        }

        // compose generator (text)
        if (prompt.Contains("compose the following subtask solutions"))
        {
            return Task.FromResult(Build(_responses["compose"]));
        }

        // vote selection (json)
        if (prompt.Contains("\"selected\"") && prompt.Contains("\"reason\""))
        {
            return Task.FromResult(Build(_responses["vote"]));
        }

        // Match response based on prompt content
        foreach (var (keyword, response) in _responses)
        {
            if (prompt.Contains(keyword))
            {
                return Task.FromResult(Build(response));
            }
        }

        // Default response
        return Task.FromResult(Build("This is a mock response for testing purposes."));
    }

    public async IAsyncEnumerable<AevatarLLMToken> GenerateStreamAsync(
        AevatarLLMRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GenerateAsync(request, cancellationToken);
        var content = response.Content;

        // Simulate streaming output: output 20 characters at a time
        var chunkSize = 20;
        var index = 0;

        for (var i = 0; i < content.Length; i += chunkSize)
        {
            var chunk = content.Substring(i, Math.Min(chunkSize, content.Length - i));
            yield return new AevatarLLMToken
            {
                Content = chunk,
                Index = index++,
                IsComplete = i + chunkSize >= content.Length
            };

            // Simulate network delay
            await Task.Delay(10, cancellationToken);
        }
    }

    // ============================================================
    //  Prompt extraction helpers
    // ============================================================

    private static string ExtractPromptText(AevatarLLMRequest request)
    {
        // Messages is the canonical place where AIGAgentBase stores user/assistant messages.
        if (request.Messages == null || request.Messages.Count == 0)
            return request.UserPrompt ?? string.Empty;

        var sb = new System.Text.StringBuilder(capacity: 256);

        foreach (var msg in request.Messages)
        {
            if (msg == null) continue;
            if (string.IsNullOrWhiteSpace(msg.Content)) continue;

            sb.Append(msg.Content);
            sb.Append(' ');
        }

        // Fallback: some providers may still set UserPrompt explicitly.
        if (!string.IsNullOrWhiteSpace(request.UserPrompt))
        {
            sb.Append(request.UserPrompt);
            sb.Append(' ');
        }

        return sb.ToString();
    }

    private static string? TryGetStageHint(AevatarLLMRequest request)
    {
        if (request.Context == null)
            return null;

        if (!request.Context.TryGetValue("stage_hint", out var raw) || raw == null)
            return null;

        var s = raw.ToString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    private static string? NormalizeStageHint(string? stageHint)
    {
        if (string.IsNullOrWhiteSpace(stageHint))
            return null;

        // vote proposals are emitted as "{stepId}.gen[N]" (see CognitiveCoordinatorGAgent.Vote.cs)
        var s = stageHint.Trim();
        var genPos = s.IndexOf(".gen[", StringComparison.OrdinalIgnoreCase);
        if (genPos > 0)
        {
            s = s[..genPos];
        }

        return s.ToLowerInvariant();
    }

    private static string BuildCheckAtomicJson(string promptText)
    {
        // Only judge if it's "top-level paper review" based on the TASK TO ANALYZE section.
        // Note: Subtask workflow_call will put original paper content into CONTEXT (which also contains "please review..."),
        // if we directly use global Contains, subtasks will always be judged as COMPLEX → infinite recursion until max_depth.
        var taskToAnalyze = ExtractTaskToAnalyzeSection(promptText);
        var isTopLevelPaperReview = taskToAnalyze.Contains(
            "please review the following paper",
            StringComparison.OrdinalIgnoreCase);

        return isTopLevelPaperReview
            ? """
              {
                "is_atomic": false,
                "reasoning": "A full paper review requires multiple dimensions of analysis; decompose first."
              }
              """
            : """
              {
                "is_atomic": true,
                "reasoning": "This is a focused single-aspect subtask; solve directly."
              }
              """;
    }

    private static string ExtractTaskToAnalyzeSection(string promptText)
    {
        if (string.IsNullOrWhiteSpace(promptText))
            return string.Empty;

        var start = promptText.IndexOf("task to analyze:", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return promptText;
        start = promptText.IndexOf('\n', start);
        if (start < 0) return promptText;

        var end = promptText.IndexOf("context:", start, StringComparison.OrdinalIgnoreCase);
        if (end < 0)
            end = promptText.IndexOf("response format", start, StringComparison.OrdinalIgnoreCase);
        if (end < 0) end = promptText.Length;

        return promptText.Substring(start, end - start);
    }
}
