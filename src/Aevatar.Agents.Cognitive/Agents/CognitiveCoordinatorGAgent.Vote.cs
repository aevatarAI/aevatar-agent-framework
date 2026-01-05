using Aevatar.Agents.Cognitive.Messages;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Maker;
using Microsoft.Extensions.Logging;

using StepDefinition = Aevatar.Agents.Cognitive.Primitives.StepDefinition;
using VoteResult = Aevatar.Agents.Maker.VoteResult;

namespace Aevatar.Agents.Cognitive.Agents;

// ============================================================
//  CognitiveCoordinatorGAgent - Vote (consensus)
//
//  WHY:
//  - vote is a complex node combining "parallelism + streaming + semantic clustering + red-flag".
//  - Separate into file to avoid Coordinator main file continuing to expand.
// ============================================================

public partial class CognitiveCoordinatorGAgent
{
    private async Task<PrimitiveResult> ExecuteVoteAsync(StepDefinition step)
    {
        var k = ResolveIntParameter(step.Parameters, "k", 3);
        var maxRounds = ResolveIntParameter(step.Parameters, "max_rounds", 10);
        var similarity = ResolveFloatParameter(step.Parameters, "similarity", _semanticSimilarityThreshold);
        var generator = step.Generator;

        if (generator == null)
        {
            return PrimitiveResult.Fail("vote requires 'generator' definition");
        }

        // ─────────────────────────────────────────────
        //  Red-Flag strategy (pluggable)
        //  Priority: step configuration > Coordinator default configuration > null (disabled)
        // ─────────────────────────────────────────────
        var redFlagStrategy = ResolveRedFlagStrategy(step.Parameters);
        var redFlagCount = 0;
        var maxRedFlags = ResolveIntParameter(step.Parameters, "max_red_flags", maxRounds * 2);
        // Compatibility: allow max_red_flags to be nested under red_flag config (maker-v2.yaml style).
        if (!step.Parameters.ContainsKey("max_red_flags") &&
            step.Parameters.TryGetValue("red_flag", out var rfObj) &&
            rfObj is Dictionary<string, object?> rfConfig &&
            rfConfig.TryGetValue("max_red_flags", out var nestedMax) &&
            nestedMax != null)
        {
            maxRedFlags = ConvertToInt(nestedMax, maxRedFlags);
        }

        // ─────────────────────────────────────────────
        //  Use MAKER's semantic clustering VoteEngine
        //  If embedding generator not configured, automatically fall back to exact matching
        // ─────────────────────────────────────────────
        using var engine = new VoteEngine(
            k,
            _embeddingGenerator, // Automatically fall back to exact matching when null
            maxRounds,
            similarity);

        var useSemanticClustering = _embeddingGenerator != null;
        Logger.LogDebug(
            "Vote step {StepId}: K={K}, maxRounds={MaxRounds}, semantic={Semantic}, redFlag={RedFlag}",
            step.Id, k, maxRounds, useSemanticClustering, redFlagStrategy?.GetType().Name ?? "disabled");

        var totalTokens = 0;
        var totalCalls = 0;
        VoteResult? consensusResult = null;
        var round = 0;
        // Record round when consensus reached (for UI and log consistency)
        int? consensusReachedAtRound = null;

        // Parallel batch size: generate N proposals simultaneously per batch (limited by Worker count and K value)
        var batchSize = Math.Max(1, Math.Min(k + 1, _workerIds.Count > 0 ? _workerIds.Count : 3));
        Logger.LogDebug(
            "[VOTE] batchSize={BatchSize}, k={K}, workers={WorkerCount}",
            batchSize, k, _workerIds.Count);

        while (consensusResult == null && round < maxRounds)
        {
            // Terminate early when too many red flags
            if (redFlagCount >= maxRedFlags)
            {
                Logger.LogWarning(
                    "Vote step {StepId}: Too many red flags ({RedFlags}), terminating early",
                    step.Id, redFlagCount);
                break;
            }

            var batchRound = round / batchSize + 1;

            // Send voting progress event
            EmitStepEvent(step, StepStatus.Running,
                $"Voting batch {batchRound}, generating {batchSize} proposals in parallel",
                progress: (float)round / maxRounds,
                voteRound: round, voteMaxRounds: maxRounds, voteK: k,
                parallelTotal: batchSize, parallelCompleted: 0);

            // Pre-render prompt (shared by all parallel tasks)
            var genPrompt = generator.Parameters.GetValueOrDefault("prompt")?.ToString() ?? "";
            var genSystem = generator.Parameters.GetValueOrDefault("system")?.ToString();

            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug(
                    "[Vote {StepId}] generator.Type={GenType}, generator.Parameters=[{GenParams}], workflowVars=[{Vars}]",
                    step.Id,
                    generator.Type,
                    string.Join(", ", generator.Parameters.Keys),
                    string.Join(", ", _workflowVariables.Keys));
                Logger.LogDebug("[Vote {StepId}] genPrompt length={Len}", step.Id, genPrompt.Length);
            }

            // ============================================================
            //  Critical diagnosis: When compose hangs, most common causes are "template rendering" or "LLM first token" hanging
            //  Output template rendering time here first to help locate if stuck at Render()
            // ============================================================
            var renderSw = System.Diagnostics.Stopwatch.StartNew();
            Logger.LogInformation("[VOTE] {StepId}: Rendering generator template...", step.Id);
            genPrompt = _templateEngine.Render(genPrompt, _workflowVariables);
            if (genSystem != null) genSystem = _templateEngine.Render(genSystem, _workflowVariables);
            renderSw.Stop();
            Logger.LogInformation("[VOTE] {StepId}: Template rendered in {Ms}ms, promptLen={Len}",
                step.Id, renderSw.ElapsedMilliseconds, genPrompt.Length);

            // ─────────────────────────────────────────────
            //  Parallel proposal generation: Start multiple LLM calls simultaneously
            //  ⚡ Key: Send all start events first to achieve visual parallelism
            // ─────────────────────────────────────────────
            var batchTasks = new List<(int index, StepDefinition step, Task<PrimitiveResult> task)>();

            Logger.LogDebug(
                "[VOTE] Generating batch: round={Round}, batchSize={BatchSize}, maxRounds={MaxRounds}",
                round, batchSize, maxRounds);
            for (int i = 0; i < batchSize && round + i < maxRounds; i++)
            {
                var proposalIndex = round + i + 1;
                var genStepId = $"{step.Id}.gen[{proposalIndex}]";
                var genStep = new StepDefinition { Id = genStepId, Type = "llm_call" };

                // Send start event (all cards appear simultaneously)
                // parallelTotal = batchSize, ensure frontend can correctly calculate workerCount
                EmitStepEvent(genStep, StepStatus.Running,
                    $"Generating proposal #{proposalIndex}",
                    progress: 0,
                    parentStepId: step.Id,
                    parallelTotal: batchSize,
                    systemPrompt: genSystem,
                    userPrompt: genPrompt);

                // Start LLM call (don't await)
                var task = ExecuteLlmCallWithStreamingAsync(generator, genStep, genSystem, genPrompt);
                batchTasks.Add((proposalIndex, genStep, task));
            }

            // Wait for all parallel tasks to complete
            var results = await Task.WhenAll(batchTasks.Select(t => t.task));

            // Process results
            for (int i = 0; i < results.Length; i++)
            {
                round++;
                var (proposalIndex, genStep, _) = batchTasks[i];
                var result = results[i];

                totalTokens += result.TokensUsed;
                totalCalls += result.LlmCalls;

                // Send completion event
                EmitStepEvent(genStep, result.Success ? StepStatus.Completed : StepStatus.Failed,
                    result.Success ? $"Proposal #{proposalIndex} generated" : $"Generation failed: {result.Error}",
                    progress: 1,
                    parentStepId: step.Id,
                    parallelTotal: batchSize,
                    systemPrompt: result.SystemPrompt,
                    userPrompt: result.UserPrompt,
                    assistantResponse: result.AssistantResponse);

                if (!result.Success) continue;

                // ✅ Consensus reached: Still send completion events for remaining proposals in this batch (otherwise UI will show some worker always empty/stuck),
                // but no longer continue voting consumption.
                if (consensusResult != null) continue;

                // Use raw LLM response for voting (not parsed object)
                // VoteEngine needs raw string for semantic clustering
                var proposal = result.AssistantResponse ?? result.Value?.ToString() ?? "";

                // Red-Flag validation
                if (redFlagStrategy != null)
                {
                    var proposalId = $"{step.Id}.round{proposalIndex}";
                    if (!redFlagStrategy.Validate(proposal, proposalId, out var reason))
                    {
                        redFlagCount++;
                        Logger.LogWarning("🚩 Red flag in {StepId} round {Round}: {Reason}",
                            step.Id, proposalIndex, reason);

                        EmitStepEvent(step, StepStatus.Running,
                            $"🚩 Round {proposalIndex}: {reason}",
                            progress: (float)round / maxRounds,
                            voteRound: proposalIndex, voteMaxRounds: maxRounds, voteK: k,
                            redFlagReason: reason);
                        continue;
                    }
                }

                // Submit vote
                consensusResult = await engine.SubmitVoteAsync(proposal);
                if (consensusResult != null && consensusReachedAtRound == null)
                {
                    // Preserve "first consensus reached" round, not affected by subsequent (unvoted) proposals
                    consensusReachedAtRound = proposalIndex;
                }
            }

            // Send current voting status
            var currentVotes = consensusResult?.LeaderVotes ?? 0;
            var displayRound = consensusReachedAtRound ?? round;
            EmitStepEvent(step, StepStatus.Running,
                $"Round {displayRound}: {currentVotes}/{k} votes" + (redFlagCount > 0 ? $" (🚩{redFlagCount})" : ""),
                progress: (float)round / maxRounds,
                voteRound: displayRound, voteMaxRounds: maxRounds, voteK: k,
                voteCurrentVotes: currentVotes);

            if (consensusResult != null)
            {
                Logger.LogInformation(
                    "✓ Vote consensus reached at round {Round}: {LeaderVotes}/{K} votes (🚩{RedFlags})",
                    displayRound, consensusResult.LeaderVotes, k, redFlagCount);
            }
        }

        // Add embedding call count
        var embeddingCalls = engine.EmbeddingCallCount;

        // Get raw content
        string rawContent;
        if (consensusResult != null && consensusResult.Success)
        {
            // WinningContent is nullable in some implementations
            rawContent = consensusResult.WinningContent ?? "";
        }
        else
        {
            // No consensus reached, return most voted
            var bestCandidate = engine.GetBestCandidate();
            rawContent = bestCandidate?.Content ?? "";

            Logger.LogWarning(
                "Vote step {StepId}: No consensus after {Rounds} rounds (🚩{RedFlags}), using best candidate ({Votes} votes)",
                step.Id, maxRounds, redFlagCount, bestCandidate?.Votes ?? 0);
        }

        // ─────────────────────────────────────────────
        //  Critical fix: Parse result according to generator.output
        //  For example output: json_array should return List<object>
        // ─────────────────────────────────────────────
        var outputType = generator.Parameters.GetValueOrDefault("output")?.ToString() ?? "text";

        // DEBUG: Check rawContent
        Logger.LogInformation("[DEBUG][Vote] rawContent length: {Len}", rawContent.Length);
        Logger.LogInformation("[DEBUG][Vote] rawContent preview: {Preview}",
            rawContent.Length > 500 ? rawContent[..500] : rawContent);
        Logger.LogInformation("[DEBUG][Vote] outputType: {Type}", outputType);

        var parsedResult = ParseOutput(rawContent, outputType);
        if (parsedResult == null)
        {
            return PrimitiveResult.Fail("redflag-parse-null");
        }

        // DEBUG: Check parsed result
        Logger.LogInformation("[DEBUG][Vote] parsedResult is null: {IsNull}", parsedResult == null);
        if (parsedResult is System.Collections.IList list)
        {
            Logger.LogInformation("[DEBUG][Vote] parsedResult is List with {Count} items", list.Count);
        }

        // ============================================================
        //  Expose "final consensus content" to step event (AssistantResponse)
        //
        //  WHY:
        //  - vote step itself is not llm_call, its PrimitiveResult doesn't carry AssistantResponse by default
        //  - This causes upper UI to only see proposals, but not know which one was finally chosen by consensus
        //  - PaperReview needs to bind consensus conclusion to atomic point for visualization
        // ============================================================
        return new PrimitiveResult
        {
            Success = true,
            Value = parsedResult,
            TokensUsed = totalTokens,
            LlmCalls = totalCalls + embeddingCalls,
            AssistantResponse = rawContent
        };
    }
}

