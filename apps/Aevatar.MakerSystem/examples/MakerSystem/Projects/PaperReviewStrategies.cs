using Aevatar.Agents.Maker;
using System.Text.RegularExpressions;

namespace MakerSystem.Projects;

// ============================================================
//  Paper Revision Strategies
//  Multi-Agent collaborative paper improvement
//  LLMs directly revise the paper to publication quality
// ============================================================

/// <summary>
/// Paper content loader for markdown papers.
/// Configure the path to your paper here.
/// </summary>
public static class ReviewablePaper
{
    // ============================================================
    //  ⚙️ CONFIGURATION - Set your paper path here!
    // ============================================================
    
    /// <summary>
    /// Path to the markdown paper to be revised.
    /// Can be absolute or relative to the project root.
    /// </summary>
    public static string PaperPath { get; set; } = "papers/my_paper.md";
    
    /// <summary>
    /// Target venue/journal for publication (affects revision criteria).
    /// Examples: "NeurIPS", "ICML", "Nature", "ACL", "IEEE TPAMI"
    /// </summary>
    public static string TargetVenue { get; set; } = "Top-tier AI Conference";
    
    /// <summary>
    /// Language of the paper.
    /// </summary>
    public static string Language { get; set; } = "English";

    // ============================================================
    //  Internal Implementation
    // ============================================================
    
    private static readonly Lazy<string> LazyContent = new(LoadPaper, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<string> LazyTitle = new(ExtractTitle, LazyThreadSafetyMode.ExecutionAndPublication);
    
    public static string Content => LazyContent.Value;
    public static string Title => LazyTitle.Value;

    private static string LoadPaper()
    {
        var path = FindPaperPath();
        if (path != null && File.Exists(path))
        {
            return File.ReadAllText(path);
        }
        return $"[Paper not found at: {PaperPath}. Please set ReviewablePaper.PaperPath before running.]";
    }

    private static string? FindPaperPath()
    {
        // Try absolute path first
        if (Path.IsPathRooted(PaperPath) && File.Exists(PaperPath))
            return PaperPath;
        
        // Search from current directory upwards
        var current = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !string.IsNullOrEmpty(current); i++)
        {
            var candidate = Path.Combine(current, PaperPath);
            if (File.Exists(candidate)) return candidate;
            current = Directory.GetParent(current)?.FullName;
        }
        
        // Also try from working directory
        var workingDir = Directory.GetCurrentDirectory();
        var fromWorking = Path.Combine(workingDir, PaperPath);
        if (File.Exists(fromWorking)) return fromWorking;
        
        return null;
    }

    private static string ExtractTitle()
    {
        var content = Content;
        // Try to extract title from first # heading
        var lines = content.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("# ") && !trimmed.StartsWith("## "))
            {
                return trimmed[2..].Trim();
            }
        }
        // Fallback to filename
        return Path.GetFileNameWithoutExtension(PaperPath);
    }
}

// ============================================================
//  Revision Decomposition Strategy
// ============================================================

/// <summary>
/// Decomposes paper revision hierarchically:
/// - Depth 0: Paper → Sections
/// - Depth 1: Section → Paragraphs (if section is long)
/// - Depth 2+: Atomic (paragraph-level revision)
/// </summary>
public sealed class PaperRevisionDecomposer : IDecompositionStrategy
{
    /// <summary>
    /// Suggested decomposition depth (used as guidance in prompts).
    /// In Academic mode, this is NOT a hard limit - LLM decides when to stop.
    /// Depth 0: Paper → Sections
    /// Depth 1: Section → Paragraphs/Aspects  
    /// Depth 2+: Usually atomic, LLM will return empty array
    /// </summary>
    public int SuggestedMaxDepth { get; init; } = 2;
    
    /// <summary>
    /// Minimum task description length for further decomposition.
    /// Only used in Production mode's IsAtomic check.
    /// </summary>
    public int MinLengthForDecomposition { get; init; } = 200;
    
    public string BuildDecompositionPrompt(string task, IReadOnlyDictionary<string, string> ctx)
    {
        var currentDepth = int.TryParse(ctx.GetValueOrDefault("current_depth", "0"), out var d) ? d : 0;
        var solveFailed = ctx.GetValueOrDefault("solve_failed", "false") == "true";
        var bestCandidatePreview = ctx.GetValueOrDefault("best_candidate_preview", "");
        var jsonFormat = """[{"step_id":"S1","description":"..."}]""";
        
        // ============================================================
        // SOLVE FAILED → Force finer decomposition
        // This happens when multiple LLMs couldn't agree on a revision
        // ============================================================
        if (solveFailed)
        {
            var failureContext = string.IsNullOrEmpty(bestCandidatePreview) 
                ? ""
                : $"""
                
                [Previous Best Attempt - LLMs disagreed on this]
                {bestCandidatePreview}
                """;
            
            return $"""
                You are a Senior Editor at "{ReviewablePaper.TargetVenue}".
                
                ⚠️ IMPORTANT: Multiple reviewers DISAGREED on how to revise this section.
                This means the task is TOO BROAD and needs FINER decomposition.
                
                Your job: Break this task into 3-5 SMALLER, INDEPENDENT sub-tasks.
                Each sub-task should be specific enough that different writers would produce similar outputs.
                
                Good decomposition examples:
                - "Rewrite ONLY the first paragraph to clarify the main contribution"
                - "Add exactly 2 citations to support the claim in sentence 3"
                - "Fix the grammar issues in paragraph 2 (lines 5-8)"
                - "Reword the methodology description to be more precise"
                
                Bad decomposition (too broad):
                - "Improve the writing" (too vague)
                - "Revise the section" (not specific)
                - "Make it better" (no actionable target)
                
                Output: JSON array {jsonFormat}
                ⚠️ You MUST output at least 2 sub-tasks. This task cannot be solved as-is.
                Output ONLY valid JSON.
                
                [Task that caused disagreement]
                {task}
                {failureContext}
                [Full Paper Context]
                ```markdown
                {ReviewablePaper.Content}
                ```
                """;
        }
        
        // ============================================================
        // Depth 0: Paper → Sections decomposition
        // ============================================================
        if (currentDepth == 0)
        {
            return $"""
                You are a Senior Editor at "{ReviewablePaper.TargetVenue}".
                
                Analyze this paper and decompose the revision task into SECTIONS.
                
                For each section, identify what needs to be revised/improved.
                
                Common sections:
                - Abstract
                - Introduction  
                - Related Work / Background
                - Methods / Methodology
                - Experiments / Results
                - Discussion
                - Conclusion
                
                Output: JSON array {jsonFormat}
                Output ONLY valid JSON, no markdown code blocks.
                
                [Original Paper]
                ```markdown
                {ReviewablePaper.Content}
                ```
                
                [Task]
                {task}
                """;
        }
        
        // ============================================================
        // Depth 1: Section → Aspects/Paragraphs decomposition
        // ============================================================
        if (currentDepth == 1)
        {
            return $"""
                You are a detail-oriented Editor for "{ReviewablePaper.TargetVenue}".
                
                The following SECTION needs fine-grained revision.
                Break it down into 3-6 SPECIFIC sub-tasks covering different aspects:
                
                Examples of good sub-tasks:
                - "Rewrite the motivation paragraph (lines 1-3) to be more compelling"
                - "Add concrete numbers to support the claim about performance"
                - "Improve the transition between method description and experiments"
                - "Clarify the notation in equation (2)"
                - "Strengthen the comparison with related work"
                - "Fix passive voice issues in paragraph 3"
                
                Each sub-task should target a SPECIFIC part of the section.
                
                Output: JSON array {jsonFormat}
                Each sub-task should be SPECIFIC and ACTIONABLE.
                Output ONLY valid JSON.
                
                [Section to decompose]
                {task}
                
                [Full Paper Context]
                ```markdown
                {ReviewablePaper.Content}
                ```
                """;
        }
        
        // ============================================================
        // Depth 2+: Fine-grained task - check if further decomposition needed
        // ============================================================
        return $"""
            You are a meticulous Editor for "{ReviewablePaper.TargetVenue}".
            
            Analyze this specific revision task. Decide:
            
            **OPTION A - Task is ATOMIC (directly solvable):**
            If the task is already specific enough to execute directly (e.g., "rewrite this paragraph", 
            "fix this equation", "add a citation"), output an EMPTY array:
            []
            
            **OPTION B - Task needs further breakdown:**
            If the task is still too broad, break it into 2-3 very specific sub-tasks.
            
            ⚠️ IMPORTANT: Only decompose if absolutely necessary. 
            Most tasks at this depth should be ATOMIC (return []).
            
            Output: JSON array (empty [] or with sub-tasks)
            Output ONLY valid JSON.
            
            [Task to analyze]
            {task}
            
            [Paper Context]
            ```markdown
            {ReviewablePaper.Content}
            ```
            """;
    }

    /// <summary>
    /// Determine if a task is atomic (Production mode only).
    /// 
    /// In Academic mode, this method is NOT called - the LLM decides
    /// whether to decompose by returning an empty array from BuildDecompositionPrompt.
    /// 
    /// Rules:
    /// 1. Depth 0 (root): NEVER atomic - must decompose into sections
    /// 2. Depth 1 (sections): Atomic only if very short task description
    /// 3. Depth 2+ (fine-grained): Usually atomic, except for complex multi-aspect tasks
    /// </summary>
    public bool IsAtomic(string task, int depth)
    {
        // Root task (depth=0): Must decompose into sections
        if (depth == 0)
            return false;
        
        // Section level (depth=1): Decompose if task is substantial
        if (depth == 1)
            return task.Length < MinLengthForDecomposition;
        
        // Depth 2: Check for atomic keywords
        var lower = task.ToLowerInvariant();
        var atomicKeywords = new[] { 
            "paragraph", "sentence", "line", "equation", "figure", "table", 
            "citation", "rewrite", "fix", "clarify", "add", "remove", "improve"
        };
        if (atomicKeywords.Any(k => lower.Contains(k)))
            return true;
        
        // Default: short tasks are atomic, long tasks may need decomposition
        return task.Length < MinLengthForDecomposition * 2;
    }

    public IReadOnlyList<(string, string)> ParseDecomposition(string output)
        => new DefaultDecomposer().ParseDecomposition(output);
}

// ============================================================
//  Revision Solution Strategy
// ============================================================

/// <summary>
/// Directly revises a specific section of the paper.
/// ULTRA-STRICT revision for top-tier publication.
/// </summary>
public sealed class PaperRevisionSolver : ISolutionStrategy
{
    public string BuildSolvePrompt(string task, IReadOnlyDictionary<string, string> ctx)
    {
        var previousFeedback = ctx.GetValueOrDefault("previous_feedback", "");
        var revisionRound = ctx.GetValueOrDefault("revision_round", "1");
        
        var feedbackSection = string.IsNullOrEmpty(previousFeedback) 
            ? "" 
            : $"""
            
            ## ⚠️ CRITICAL: Address Previous Round Feedback
            
            The following issues were identified in the previous revision round.
            You MUST address these in your revision:
            
            {previousFeedback}
            
            """;
        
        return $"""
            You are a **Senior Technical Writer and Domain Expert** for "{ReviewablePaper.TargetVenue}".
            Revision Round: {revisionRound}
            
            ⚠️ **QUALITY MANDATE** ⚠️
            - This is for a TOP-TIER venue with <10% acceptance rate
            - Every sentence must be CRISP, CLEAR, and NECESSARY
            - Every claim must be PRECISE and SUPPORTED
            - The writing must be IMPECCABLE - no reviewer should find fault
            {feedbackSection}
            ## Your Task
            
            REWRITE the specified section to meet {ReviewablePaper.TargetVenue} publication standards.
            
            ## Revision Checklist (Apply ALL)
            
            ### 1. Technical Precision
            - [ ] All claims are precise and well-qualified (avoid "significantly", "dramatically" without numbers)
            - [ ] Technical terms are defined on first use
            - [ ] Methodology is reproducible from description alone
            - [ ] All assumptions are explicitly stated
            - [ ] Limitations are acknowledged honestly
            
            ### 2. Logical Flow
            - [ ] Clear topic sentence for each paragraph
            - [ ] Smooth transitions between paragraphs
            - [ ] Arguments build logically without gaps
            - [ ] No circular reasoning or unsupported leaps
            
            ### 3. Language Quality
            - [ ] Native-level English (or specified language)
            - [ ] No passive voice where active is clearer
            - [ ] Concise sentences (aim for <25 words each)
            - [ ] No redundancy or filler words
            - [ ] Consistent terminology throughout
            
            ### 4. Academic Standards
            - [ ] Appropriate citations where needed
            - [ ] Fair comparison with prior work
            - [ ] Contributions clearly stated
            - [ ] Figures/tables referenced properly
            
            ## Output Format
            
            [REVISED SECTION - Output the complete rewritten section in Markdown]
            
            ---
            
            ### 📋 Revision Report
            
            **Major Changes:**
            1. [Significant change with reasoning]
            2. [Significant change with reasoning]
            
            **Minor Fixes:**
            - [Grammar/style fixes]
            - [Clarity improvements]
            
            **Quality Self-Assessment:**
            - Technical Precision: [1-10]
            - Logical Flow: [1-10]  
            - Language Quality: [1-10]
            - Overall: [1-10]
            
            **Remaining Concerns (be honest):**
            - [Any issues you couldn't fully resolve]
            
            ---
            
            ## Original Paper ({ReviewablePaper.Language})
            
            ```markdown
            {ReviewablePaper.Content}
            ```
            
            ---
            
            [Task: Revise this section]
            {task}
            
            [Now provide your PUBLICATION-READY revision]
            """;
    }
}

// ============================================================
//  Revision Composition Strategy
// ============================================================

/// <summary>
/// Composes all revised sections into the final paper.
/// Includes publication readiness assessment for iteration control.
/// </summary>
public sealed class PaperRevisionComposer : ICompositionStrategy
{
    /// <summary>
    /// Returns null to use LLM-based composition.
    /// We need LLM to properly merge sections and ensure consistency.
    /// </summary>
    public string? Compose(
        string originalTask,
        IReadOnlyDictionary<string, string> subtaskResults,
        IReadOnlyDictionary<string, string> context)
    {
        return null; // Use LLM synthesis
    }

    /// <summary>
    /// Build synthesis prompt to merge all revised sections.
    /// Includes structured verdict for iteration control.
    /// ULTRA-STRICT evaluation for top-tier venues.
    /// </summary>
    public string BuildSynthesisPrompt(
        string originalTask,
        IReadOnlyDictionary<string, string> subtaskResults,
        IReadOnlyDictionary<string, string> context)
    {
        var revisedSections = string.Join("\n\n---\n\n", 
            subtaskResults.Select((kv, i) => $"## Revised Section #{i + 1}: {kv.Key}\n\n{kv.Value}"));
        
        var venue = context.GetValueOrDefault("venue", ReviewablePaper.TargetVenue);
        var paperTitle = context.GetValueOrDefault("paper_title", ReviewablePaper.Title);
        var currentRound = context.GetValueOrDefault("revision_round", "1");
        var strictness = context.GetValueOrDefault("review_strictness", "STRICT");
        
        return $"""
            You are the **MOST CRITICAL Senior Area Chair** for "{venue}".
            This is revision round #{currentRound}. Review strictness: {strictness}.
            
            ⚠️ **CRITICAL MINDSET** ⚠️
            - You are reviewing for the TOP 1% of submissions
            - Your reputation depends on NOT letting mediocre papers through
            - Assume the paper is NOT ready unless proven otherwise
            - Be HARSH but CONSTRUCTIVE - identify every weakness
            - Score conservatively: 8+ means "exceptional", 6-7 means "good but not great"
            
            ## Your Tasks
            
            1. MERGE all revised sections into a complete paper
            2. RUTHLESSLY EVALUATE against {venue} acceptance standards
            3. OUTPUT structured verdict with DETAILED justification
            
            ## Evaluation Standards for {venue}
            
            To receive APPROVED, the paper MUST satisfy ALL of these:
            
            ✅ **Novelty (Score ≥ 8)**: Significant contribution beyond incremental improvement
            ✅ **Technical Rigor (Score ≥ 8)**: Proofs/experiments are bulletproof, no logical gaps
            ✅ **Clarity (Score ≥ 8)**: Crystal clear writing, any expert can understand
            ✅ **Completeness (Score ≥ 8)**: All claims supported, no missing experiments
            ✅ **Language (Score ≥ 8)**: Publication-ready English, no grammatical issues
            ✅ **Presentation (Score ≥ 7)**: Figures, tables, formatting are professional
            ✅ **Impact (Score ≥ 7)**: Will influence the field meaningfully
            
            🚫 **AUTOMATIC REJECTION if any of these exist:**
            - Any logical flaw or unsupported claim
            - Missing related work or unfair comparisons  
            - Unclear methodology that cannot be reproduced
            - Overstated contributions or misleading results
            - Poor writing that impedes understanding
            
            ## Output Format (STRICTLY FOLLOW!)
            
            # {paperTitle}
            
            [Complete merged paper in Markdown...]
            
            ---
            
            ## 📊 PUBLICATION VERDICT
            
            <!-- Parsed by system - be HONEST, not optimistic -->
            ```verdict
            VERDICT: [APPROVED|NEEDS_REVISION]
            CONFIDENCE: [0-100]%
            ```
            
            ### Detailed Assessment
            
            | Criterion | Score (1-10) | Justification |
            |-----------|--------------|---------------|
            | Novelty | X | [Specific reasoning] |
            | Technical Rigor | X | [Specific reasoning] |
            | Clarity | X | [Specific reasoning] |
            | Completeness | X | [Specific reasoning] |
            | Language Quality | X | [Specific reasoning] |
            | Presentation | X | [Specific reasoning] |
            | Impact | X | [Specific reasoning] |
            
            **Overall Score: X/70** (APPROVED requires ≥ 54/70, all individual ≥ 7)
            
            ### Critical Weaknesses (if any)
            
            1. **[Weakness Type]**: [Detailed description and evidence]
            2. **[Weakness Type]**: [Detailed description and evidence]
            ...
            
            ### Required Improvements (if NEEDS_REVISION)
            
            1. [Specific actionable fix with location]
            2. [Specific actionable fix with location]
            ...
            
            ### Strengths (acknowledge good aspects)
            
            1. [Strength]
            2. [Strength]
            ...
            
            ---
            
            ## 📝 Revision Summary (Round {currentRound})
            
            [Summarize what was changed this round]
            
            ---
            
            ## Revised Sections to Merge
            
            {revisedSections}
            
            ---
            
            ## Original Paper
            
            ```markdown
            {ReviewablePaper.Content}
            ```
            
            ---
            
            [Now merge, evaluate RUTHLESSLY, and output verdict. Remember: Your job is to PROTECT the venue's reputation by only approving truly excellent papers.]
            """;
    }
    
    /// <summary>
    /// Parse the verdict from composition result.
    /// </summary>
    public static (bool IsApproved, int Confidence, string? Feedback) ParseVerdict(string compositionResult)
    {
        // Match verdict block
        var verdictMatch = Regex.Match(
            compositionResult, 
            @"```verdict\s*VERDICT:\s*(APPROVED|NEEDS_REVISION)\s*CONFIDENCE:\s*(\d+)%?\s*```",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        
        if (verdictMatch.Success)
        {
            var verdict = verdictMatch.Groups[1].Value.Trim().ToUpperInvariant();
            var confidence = int.TryParse(verdictMatch.Groups[2].Value, out var c) ? c : 50;
            
            // Extract improvement suggestions if NEEDS_REVISION
            string? feedback = null;
            if (verdict == "NEEDS_REVISION")
            {
                var feedbackMatch = Regex.Match(
                    compositionResult,
                    @"### If NEEDS_REVISION.*?:\s*\n((?:\d+\..*?\n)+)",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (feedbackMatch.Success)
                {
                    feedback = feedbackMatch.Groups[1].Value.Trim();
                }
            }
            
            return (verdict == "APPROVED", confidence, feedback);
        }
        
        // Fallback: look for keywords
        var hasApproved = compositionResult.Contains("APPROVED", StringComparison.OrdinalIgnoreCase);
        var hasNeedsRevision = compositionResult.Contains("NEEDS_REVISION", StringComparison.OrdinalIgnoreCase);
        
        if (hasApproved && !hasNeedsRevision) return (true, 70, null);
        if (hasNeedsRevision) return (false, 50, "See revision suggestions in output");
        
        // Default: assume not ready
        return (false, 30, "Unable to parse verdict, assuming revision needed");
    }
}

// ============================================================
//  Helper: Project Registration
// ============================================================

/// <summary>
/// Helper to create the paper revision project definition.
/// Supports iterative revision until consensus is reached.
/// </summary>
public static class PaperReviewProject
{
    /// <summary>
    /// Maximum revision rounds before forced termination.
    /// High-priority task: allow extensive iteration.
    /// </summary>
    public const int MaxRevisionRounds = 10;
    
    /// <summary>
    /// Minimum confidence score to accept as "publication ready".
    /// STRICT: 90% confidence required for top-tier venues.
    /// </summary>
    public const int MinConfidenceThreshold = 90;
    
    /// <summary>
    /// Maximum TOTAL time for ALL rounds combined.
    /// This is the hard limit for the entire iterative process.
    /// </summary>
    public static readonly TimeSpan MaxTotalDuration = TimeSpan.FromHours(6);
    
    /// <summary>
    /// Time budget per round (total / max rounds).
    /// Decreases as more rounds are used to ensure we don't run out of time.
    /// </summary>
    public static TimeSpan GetRoundBudget(int currentRound, TimeSpan elapsed)
    {
        var remaining = MaxTotalDuration - elapsed;
        var roundsLeft = MaxRevisionRounds - currentRound + 1;
        
        // Allocate remaining time evenly, with 10% buffer
        var perRound = TimeSpan.FromTicks((long)(remaining.Ticks * 0.9 / roundsLeft));
        
        // Minimum 10 minutes per round
        return perRound < TimeSpan.FromMinutes(10) 
            ? TimeSpan.FromMinutes(10) 
            : perRound;
    }
    
    /// <summary>
    /// Create a paper revision project with the given paper path.
    /// </summary>
    public static (string Task, Func<MakerOptions> BuildOptions) Create(
        string paperPath,
        string targetVenue = "Top-tier AI Conference",
        string language = "English",
        int revisionRound = 1,
        string? previousFeedback = null)
    {
        // Configure the paper
        ReviewablePaper.PaperPath = paperPath;
        ReviewablePaper.TargetVenue = targetVenue;
        ReviewablePaper.Language = language;
        
        var task = revisionRound == 1
            ? $"Revise the paper '{ReviewablePaper.Title}' to publication quality for {targetVenue}. Output the complete revised paper with a publication verdict."
            : $"Continue revising the paper '{ReviewablePaper.Title}' (Round {revisionRound}). Address the feedback from previous round and improve further. Output the revised paper with updated publication verdict.";
        
        // Add previous feedback to task if available
        if (!string.IsNullOrEmpty(previousFeedback))
        {
            task += $"\n\n## Previous Round Feedback:\n{previousFeedback}";
        }

        Func<MakerOptions> buildOptions = () => new MakerOptions
        {
            // ================================================================
            // ULTRA-HIGH BUDGET & RELIABILITY for top-priority task
            // ================================================================

            // K=7, N=13 - Ultra-high reliability voting
            // ============================================================
            //  OPTIMIZED: Reduced K to minimize request backlog
            //  Problem: High K (4-7) causes too many concurrent requests
            //  When consensus is reached, remaining requests block new tasks
            // ============================================================
            // K=2, N=3 - Quick consensus, minimal waste
            // Trade-off: Lower reliability for faster iteration
            Reliability = ReliabilityLevel.Medium,

            // High budget per round (will be dynamically adjusted based on remaining time)
            MaxTotalLlmCalls = 2000, // 2000 calls per round
            MaxTotalTokens = 10_000_000, // 10M tokens per round
            MaxDuration = TimeSpan.FromMinutes(30), // Default per round, overridden by GetRoundBudget()

            // Academic mode: maximum decomposition for correctness
            Mode = ExecutionMode.Academic,
            Granularity = DecompositionGranularity.Balanced,

            // Higher temperature variance for diverse perspectives
            BaseTemperature = 0.4f,
            TemperatureVariance = 0.15f,

            // Multi-provider: maximize decorrelation via different models
            UseMultipleProviders = true,

            // Looser semantic clustering for long-text voting
            // Papers need lower threshold because long revisions have more variance
            SemanticSimilarityThreshold = 0.75f, // Lower = more lenient clustering

            // Red flag handling
            RedFlagThreshold = 5, // More tolerance before escalation
            DepthWarningThreshold = 15, // Allow deeper decomposition

            // Red Flag Options for long papers - papers often exceed 8000 chars
            RedFlagOptions = new RedFlagOptions
            {
                MaxContentLength = 200_000, // 200K chars for full paper revisions
                MinContentLength = 100, // Papers should be substantial
            },

            // Strategies
            Decomposer = new PaperRevisionDecomposer(),
            Solver = new PaperRevisionSolver(),
            Composer = new PaperRevisionComposer(),

            Context = new Dictionary<string, string>
            {
                ["venue"] = targetVenue,
                ["language"] = language,
                ["paper_title"] = ReviewablePaper.Title,
                ["revision_round"] = revisionRound.ToString(),
                ["previous_feedback"] = previousFeedback ?? "",
                ["review_strictness"] = "ULTRA_STRICT" // Signal to prompts
            },

            // CoordinatorProviderName = "deepseek"
        };
        
        return (task, buildOptions);
    }
    
    /// <summary>
    /// Iterative revision executor.
    /// Runs revision rounds until approved or max rounds reached.
    /// On failure/budget exhaustion, synthesizes a "best effort" result from all collected data.
    /// </summary>
    /// <param name="executor">The MAKER executor</param>
    /// <param name="paperPath">Path to the paper</param>
    /// <param name="targetVenue">Target publication venue</param>
    /// <param name="language">Paper language</param>
    /// <param name="onRoundComplete">Callback after each round (round, result, isApproved, confidence)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Final revision result with all rounds</returns>
    public static async Task<IterativeRevisionResult> RunIterativeRevisionAsync(
        IMakerExecutor executor,
        string paperPath,
        string targetVenue = "Top-tier AI Conference",
        string language = "English",
        Action<int, MakerResult, bool, int>? onRoundComplete = null,
        CancellationToken ct = default)
    {
        var result = new IterativeRevisionResult
        {
            PaperPath = paperPath,
            TargetVenue = targetVenue
        };
        
        string? previousFeedback = null;
        string? latestValidContent = null;  // Track best content so far
        var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        for (var round = 1; round <= MaxRevisionRounds; round++)
        {
            ct.ThrowIfCancellationRequested();
            
            // Check total time budget
            if (totalStopwatch.Elapsed >= MaxTotalDuration)
            {
                result.FinalStatus = RevisionStatus.BudgetExhausted;
                result.TotalRounds = round - 1;
                result.TotalDuration = totalStopwatch.Elapsed;
                result.FailureReason = $"Total time budget exhausted ({totalStopwatch.Elapsed.TotalHours:F1}h / {MaxTotalDuration.TotalHours}h)";
                
                result.BestEffortPaper = await SynthesizeBestEffortAsync(
                    executor, result.Rounds, latestValidContent, targetVenue, language, ct);
                result.FinalPaper = result.BestEffortPaper ?? latestValidContent;
                
                return result;
            }
            
            // Create task for this round with dynamic time budget
            var (task, buildOptions) = Create(paperPath, targetVenue, language, round, previousFeedback);
            var options = buildOptions() with 
            {
                // Override MaxDuration with remaining time budget
                MaxDuration = GetRoundBudget(round, totalStopwatch.Elapsed)
            };
            
            MakerResult roundResult;
            try
            {
                // Execute revision round
                roundResult = await executor.ExecuteAsync(task, options, ct);
            }
            catch (Exception ex)
            {
                // Execution failed - trigger best effort synthesis
                result.FinalStatus = RevisionStatus.Failed;
                result.TotalRounds = round;
                result.TotalDuration = totalStopwatch.Elapsed;
                result.FailureReason = ex.Message;
                
                // Synthesize best effort from what we have
                result.BestEffortPaper = await SynthesizeBestEffortAsync(
                    executor, result.Rounds, latestValidContent, targetVenue, language, ct);
                result.FinalPaper = result.BestEffortPaper;
                
                return result;
            }
            
            // Track the latest content that has something
            if (!string.IsNullOrWhiteSpace(roundResult.Content))
            {
                latestValidContent = roundResult.Content;
            }
            
            result.Rounds.Add(new RevisionRound
            {
                RoundNumber = round,
                Result = roundResult,
                Feedback = previousFeedback
            });
            
            // Check if execution failed (budget exhausted, etc.)
            if (!roundResult.Success)
            {
                result.FinalStatus = RevisionStatus.BudgetExhausted;
                result.TotalRounds = round;
                result.TotalDuration = totalStopwatch.Elapsed;
                result.FailureReason = roundResult.Error ?? "Unknown error";
                
                // Synthesize best effort
                result.BestEffortPaper = await SynthesizeBestEffortAsync(
                    executor, result.Rounds, latestValidContent, targetVenue, language, ct);
                result.FinalPaper = result.BestEffortPaper ?? latestValidContent;
                
                return result;
            }
            
            // Parse verdict
            var (isApproved, confidence, feedback) = PaperRevisionComposer.ParseVerdict(
                roundResult.Content);
            
            result.Rounds[^1].IsApproved = isApproved;
            result.Rounds[^1].Confidence = confidence;
            
            // Notify callback
            onRoundComplete?.Invoke(round, roundResult, isApproved, confidence);
            
            // Check termination conditions
            if (isApproved && confidence >= MinConfidenceThreshold)
            {
                result.FinalStatus = RevisionStatus.Approved;
                result.FinalPaper = roundResult.Content;
                result.TotalRounds = round;
                result.TotalDuration = totalStopwatch.Elapsed;
                return result;
            }
            
            if (round >= MaxRevisionRounds)
            {
                result.FinalStatus = RevisionStatus.MaxRoundsReached;
                result.TotalRounds = round;
                result.TotalDuration = totalStopwatch.Elapsed;
                
                // Final synthesis: combine best from all rounds
                result.BestEffortPaper = await SynthesizeBestEffortAsync(
                    executor, result.Rounds, latestValidContent, targetVenue, language, ct);
                result.FinalPaper = result.BestEffortPaper ?? latestValidContent;
                
                return result;
            }
            
            // Prepare for next round
            previousFeedback = feedback ?? "Continue improving the paper quality";
        }
        
        // Should not reach here, but set duration just in case
        result.TotalDuration = totalStopwatch.Elapsed;
        return result;
    }
    
    /// <summary>
    /// Synthesize a "best effort" paper from all collected revision attempts.
    /// This is called when:
    /// 1. Budget is exhausted before consensus
    /// 2. Max rounds reached without approval
    /// 3. Unexpected errors occur
    /// </summary>
    private static async Task<string?> SynthesizeBestEffortAsync(
        IMakerExecutor executor,
        List<RevisionRound> rounds,
        string? latestContent,
        string targetVenue,
        string language,
        CancellationToken ct)
    {
        if (rounds.Count == 0 && string.IsNullOrWhiteSpace(latestContent))
            return null;
        
        // Collect all revision attempts and their confidence scores
        var roundSummaries = rounds
            .Where(r => !string.IsNullOrWhiteSpace(r.Result.Content))
            .Select(r => new
            {
                r.RoundNumber,
                r.Confidence,
                r.IsApproved,
                ContentPreview = r.Result.Content.Length > 500 
                    ? r.Result.Content[..500] + "..." 
                    : r.Result.Content,
                FullContent = r.Result.Content
            })
            .ToList();
        
        if (roundSummaries.Count == 0)
            return latestContent;
        
        // Find the best round (highest confidence)
        var bestRound = roundSummaries.OrderByDescending(r => r.Confidence).First();
        
        // If only one round, return it directly
        if (roundSummaries.Count == 1)
            return bestRound.FullContent;
        
        // Build synthesis task - lightweight, single LLM call
        var synthesisTask = $"""
            You are synthesizing a BEST EFFORT revision of an academic paper.
            
            CONTEXT:
            - Target venue: {targetVenue}
            - Language: {language}
            - Revision rounds completed: {rounds.Count}
            - Reason for early termination: Budget exhausted or max rounds reached
            
            I have collected {roundSummaries.Count} revision attempts. Here's a summary:
            
            {string.Join("\n\n", roundSummaries.Select(r => 
                $"### Round {r.RoundNumber} (Confidence: {r.Confidence}%, Approved: {r.IsApproved})\n{r.ContentPreview}"))}
            
            The HIGHEST CONFIDENCE revision (Round {bestRound.RoundNumber}, {bestRound.Confidence}%):
            
            ```markdown
            {bestRound.FullContent}
            ```
            
            YOUR TASK:
            Output the BEST VERSION of this paper based on the highest confidence revision.
            
            If possible, incorporate any clearly beneficial changes from other rounds that don't conflict.
            
            Output ONLY the revised paper in Markdown format.
            At the end, add a brief note about which rounds' improvements were incorporated.
            
            ---
            
            ⚠️ BEST EFFORT NOTE: This is a synthesized version due to resource constraints.
            Human review is recommended before submission.
            """;
        
        try
        {
            // Use minimal budget for synthesis
            var synthesisOptions = new MakerOptions
            {
                Reliability = ReliabilityLevel.Low,  // K=1, just one shot
                MaxTotalLlmCalls = 5,
                MaxTotalTokens = 500_000,
                MaxDuration = TimeSpan.FromMinutes(5),
                Mode = ExecutionMode.Production,
                BaseTemperature = 0.3f,  // Lower temperature for coherent output
            };
            
            var synthesisResult = await executor.ExecuteAsync(synthesisTask, synthesisOptions, ct);
            
            if (synthesisResult.Success && !string.IsNullOrWhiteSpace(synthesisResult.Content))
            {
                return synthesisResult.Content;
            }
        }
        catch
        {
            // Synthesis failed - fall back to best round's content
        }
        
        // Ultimate fallback: return the highest confidence round's content
        return bestRound.FullContent;
    }
}

// ============================================================
//  Iterative Revision Result Types
// ============================================================

/// <summary>
/// Status of the iterative revision process.
/// </summary>
public enum RevisionStatus
{
    /// <summary>In progress</summary>
    InProgress,
    /// <summary>Paper approved for publication</summary>
    Approved,
    /// <summary>Max revision rounds reached without approval</summary>
    MaxRoundsReached,
    /// <summary>Process was cancelled</summary>
    Cancelled,
    /// <summary>Process failed with error</summary>
    Failed,
    /// <summary>Budget exhausted before completion</summary>
    BudgetExhausted
}

/// <summary>
/// Result of a single revision round.
/// </summary>
public sealed class RevisionRound
{
    public int RoundNumber { get; init; }
    public required MakerResult Result { get; init; }
    public string? Feedback { get; init; }
    public bool IsApproved { get; set; }
    public int Confidence { get; set; }
}

/// <summary>
/// Complete result of iterative revision process.
/// </summary>
public sealed class IterativeRevisionResult
{
    public required string PaperPath { get; init; }
    public required string TargetVenue { get; init; }
    public RevisionStatus FinalStatus { get; set; } = RevisionStatus.InProgress;
    
    /// <summary>
    /// The final paper content (approved version or best effort).
    /// </summary>
    public string? FinalPaper { get; set; }
    
    /// <summary>
    /// Best effort paper synthesized when consensus/approval couldn't be reached.
    /// This is populated when:
    /// - Budget exhausted
    /// - Max rounds reached
    /// - Unexpected failures
    /// </summary>
    public string? BestEffortPaper { get; set; }
    
    /// <summary>
    /// Reason for failure (if FinalStatus is Failed/BudgetExhausted).
    /// </summary>
    public string? FailureReason { get; set; }
    
    public int TotalRounds { get; set; }
    
    /// <summary>
    /// Total elapsed time for all rounds.
    /// </summary>
    public TimeSpan TotalDuration { get; set; }
    
    public List<RevisionRound> Rounds { get; } = [];
    
    /// <summary>
    /// Whether the final result is a best-effort synthesis (not consensus-approved).
    /// </summary>
    public bool IsBestEffort => FinalStatus is RevisionStatus.MaxRoundsReached 
                                             or RevisionStatus.BudgetExhausted 
                                             or RevisionStatus.Failed;
    
    /// <summary>
    /// Highest confidence score achieved across all rounds.
    /// </summary>
    public int HighestConfidence => Rounds.Count > 0 ? Rounds.Max(r => r.Confidence) : 0;
    
    /// <summary>
    /// Total LLM calls across all rounds.
    /// </summary>
    public int TotalLlmCalls => Rounds.Sum(r => r.Result.TotalLLMCalls);
    
    /// <summary>
    /// Total tokens used across all rounds.
    /// </summary>
    public long TotalTokensUsed => Rounds.Sum(r => r.Result.TotalTokens);
    
    /// <summary>
    /// Human-readable summary of the revision process.
    /// </summary>
    public string GetSummary()
    {
        var statusEmoji = FinalStatus switch
        {
            RevisionStatus.Approved => "✅",
            RevisionStatus.MaxRoundsReached => "⚠️",
            RevisionStatus.BudgetExhausted => "💰",
            RevisionStatus.Failed => "❌",
            RevisionStatus.Cancelled => "🚫",
            _ => "🔄"
        };
        
        var summary = $"""
            {statusEmoji} Revision Result: {FinalStatus}
            
            📊 Statistics:
            - Rounds completed: {TotalRounds}
            - Highest confidence: {HighestConfidence}%
            - Total duration: {TotalDuration.TotalMinutes:F1} minutes ({TotalDuration.TotalHours:F2} hours)
            - Total LLM calls: {TotalLlmCalls:N0}
            - Total tokens: {TotalTokensUsed:N0}
            
            """;
        
        if (IsBestEffort)
        {
            summary += $"""
            ⚠️ BEST EFFORT OUTPUT
            This paper was synthesized from partial results because:
            - {FailureReason ?? FinalStatus.ToString()}
            
            The highest confidence revision achieved {HighestConfidence}% (target: {PaperReviewProject.MinConfidenceThreshold}%).
            Human review is strongly recommended before submission.
            """;
        }
        
        return summary;
    }
}
