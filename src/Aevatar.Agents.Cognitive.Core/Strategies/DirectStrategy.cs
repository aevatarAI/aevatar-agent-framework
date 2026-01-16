using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.Cognitive.Core;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Cognitive.Core.Strategies;

// ============================================================
//  DIRECT STRATEGY
//  直接调用 AI 策略（最简单的策略）
// ============================================================

/// <summary>
/// 直接调用 AI 策略。
/// 最简单的策略：传入 prompt，直接获取 AI 回复。
/// </summary>
public sealed class DirectStrategy : IReasoningStrategy
{
    private readonly ILLMProviderFactory _llmFactory;
    private readonly ILogger<DirectStrategy> _logger;

    public DirectStrategy(ILLMProviderFactory llmFactory, ILogger<DirectStrategy> logger)
    {
        _llmFactory = llmFactory;
        _logger = logger;
    }

    public StrategyKind Kind => StrategyKind.Direct;
    public string DisplayName => "Direct AI";
    public string Description => "直接调用 AI，最简单快速的策略";

    public ValidationResult ValidateOptions(ReasoningOptions options)
    {
        // Direct 策略几乎没有限制，总是有效
        return ValidationResult.Success();
    }

    public async Task<ReasoningResult> ExecuteAsync(
        string task,
        ReasoningOptions options,
        IProgress<ReasoningProgress>? progress = null,
        CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        long promptTokens = 0;
        long completionTokens = 0;

        try
        {
            // ─── 阶段 1：准备 ───
            progress?.Report(new ReasoningProgress
            {
                Phase = "PREPARE",
                Message = "Preparing request...",
                ProgressPercent = 0.1f
            });

            // 构建系统提示
            var systemPrompt = "You are a helpful AI assistant. Respond in the same language as the user's input.";
            
            // 如果有注入的内容，添加到任务中
            var fullTask = task;
            if (!string.IsNullOrEmpty(options.DirectSystemPrompt))
            {
                systemPrompt = options.DirectSystemPrompt;
            }

            // ─── 阶段 2：调用 AI ───
            progress?.Report(new ReasoningProgress
            {
                Phase = "CALLING_AI",
                Message = "Calling AI model...",
                ProgressPercent = 0.3f
            });

            _logger.LogInformation("Direct AI call: task length = {Length} chars", fullTask.Length);

            // 获取 LLM Provider
            var providerName = options.ProviderName ?? AevatarAgentsConstants.DefaultProviderName;
            var provider = _llmFactory.GetProvider(providerName);
            
            // 调用 LLM
            var request = new Aevatar.Agents.AI.Abstractions.AevatarLLMRequest
            {
                SystemPrompt = systemPrompt,
                UserPrompt = fullTask
            };
            
            var response = await provider.GenerateAsync(request, ct);

            var content = response.Content ?? "";
            
            // 统计 token
            if (response.Usage != null)
            {
                promptTokens = response.Usage.PromptTokens;
                completionTokens = response.Usage.CompletionTokens;
            }

            // ─── 阶段 3：完成 ───
            progress?.Report(new ReasoningProgress
            {
                Phase = "COMPLETE",
                Message = "Done",
                ProgressPercent = 1.0f,
                TotalPromptTokens = promptTokens,
                TotalCompletionTokens = completionTokens,
                TotalLlmCalls = 1
            });

            _logger.LogInformation("Direct AI completed: {Tokens} tokens, {Length} chars output",
                promptTokens + completionTokens, content.Length);

            return ReasoningResult.Succeeded(
                content,
                DateTime.UtcNow - startTime,
                llmCalls: 1,
                promptTokens: promptTokens,
                completionTokens: completionTokens);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Direct AI call cancelled");
            return ReasoningResult.Failed("Cancelled by user", DateTime.UtcNow - startTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Direct AI call failed");
            return ReasoningResult.Failed(ex.Message, DateTime.UtcNow - startTime);
        }
    }
}
