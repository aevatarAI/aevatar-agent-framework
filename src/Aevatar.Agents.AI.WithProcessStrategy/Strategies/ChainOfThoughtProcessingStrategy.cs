using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.WithProcessStrategy.Messages;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.WithProcessStrategy.Strategies;

/// <summary>
/// Chain of Thought AI processing strategy
/// Solves complex problems through step-by-step reasoning
/// </summary>
public class ChainOfThoughtProcessingStrategy : IAevatarAIProcessingStrategy
{
    public string Name => "Chain of Thought Processing";
    
    public string Description => "Chain of Thought strategy - Solves complex problems through step-by-step reasoning, suitable for scenarios requiring deep analysis";
    
    public AevatarAIProcessingMode Mode => AevatarAIProcessingMode.ChainOfThought;
    
    public bool CanHandle(AevatarAIContext context)
    {
        // Suitable for handling complex reasoning problems
        if (context.Metadata?.ContainsKey("PreferredStrategy") == true)
        {
            var preferred = context.Metadata["PreferredStrategy"]?.ToString();
            return string.Equals(preferred, "ChainOfThought", StringComparison.OrdinalIgnoreCase);
        }
        
        // Check if question requires reasoning
        var question = context.Question?.ToLower() ?? string.Empty;
        return question.Contains("why") || question.Contains("how") || 
               question.Contains("analyze") || question.Contains("explain");
    }
    
    public double EstimateComplexity(AevatarAIContext context)
    {
        // Chain of Thought suitable for medium-high complexity problems
        return 0.6;
    }
    
    public bool ValidateRequirements(AevatarAIStrategyDependencies dependencies)
    {
        return dependencies?.LLMProvider != null && dependencies.Configuration != null;
    }
    
    public async Task<string> ProcessAsync(
        AevatarAIContext context,
        AevatarAIStrategyDependencies dependencies,
        CancellationToken cancellationToken = default)
    {
        dependencies.Logger?.LogDebug("Processing with Chain of Thought strategy");
        
        var thoughts = new List<AevatarThoughtStep>();
        var stepNumber = 1;
        var maxSteps = dependencies.Configuration.MaxChainOfAevatarThoughtSteps ?? 5;
        
        while (stepNumber <= maxSteps)
        {
            // Build thought prompt - simplified implementation
            var thoughtSteps = thoughts.Count > 0 
                ? string.Join("\n", thoughts.Select((t, i) => $"Step {i + 1}: {t}"))
                : "Let's think step by step.";
            var prompt = $"{thoughtSteps}\n\nQuestion: {context.Question}";
            
            // Generate thought step
            var response = await dependencies.LLMProvider.GenerateAsync(new AevatarLLMRequest
            {
                SystemPrompt = "You are an AI that thinks step by step to solve problems. Break down your reasoning into clear steps.",
                UserPrompt = prompt,
                Settings = new AevatarLLMSettings
                {
                    ModelId = dependencies.Configuration.Model,
                    Temperature = 0.3 // Lower temperature for more deterministic reasoning
                }
            }, cancellationToken);
            
            // Parse thought step
            var thought = ParseThoughtStep(response.Content, stepNumber);
            thoughts.Add(thought);
            
            // Publish thought step event
            if (dependencies.PublishEventCallback != null)
            {
                await dependencies.PublishEventCallback(new AevatarThoughtStepEvent
                {
                    AgentId = dependencies.AgentId,
                    ThoughtId = Guid.NewGuid().ToString(),
                    StepNumber = stepNumber,
                    ThoughtContent = thought.Thought,
                    Reasoning = thought.Reasoning ?? string.Empty,
                    Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
                });
            }
            
            // Check if reached conclusion
            if (!string.IsNullOrEmpty(thought.Conclusion) && thought.Confidence > 0.8)
            {
                dependencies.Logger?.LogInformation("Chain of thought reached conclusion at step {Step} with confidence {Confidence}", 
                    stepNumber, thought.Confidence);
                return thought.Conclusion;
            }
            
            stepNumber++;
        }
        
        // Summarize all thought steps
        return await SummarizeThoughtsAsync(thoughts, dependencies, cancellationToken);
    }
    
    /// <summary>
    /// Parse thought step
    /// </summary>
    private AevatarThoughtStep ParseThoughtStep(string content, int stepNumber)
    {
        var thought = new AevatarThoughtStep
        {
            StepNumber = stepNumber,
            Thought = content,
            Confidence = 0.5
        };
        
        // Try to extract structured information from content
        // Look for keywords to identify reasoning, conclusion, etc.
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var line in lines)
        {
            var lowerLine = line.ToLower();
            
            if (lowerLine.Contains("reasoning:") || lowerLine.Contains("because:"))
            {
                thought.Reasoning = line.Substring(line.IndexOf(':') + 1).Trim();
            }
            else if (lowerLine.Contains("conclusion:") || lowerLine.Contains("therefore:") || lowerLine.Contains("answer:"))
            {
                thought.Conclusion = line.Substring(line.IndexOf(':') + 1).Trim();
                thought.Confidence = 0.9; // If explicit conclusion, increase confidence
            }
            else if (lowerLine.Contains("confidence:"))
            {
                if (double.TryParse(line.Substring(line.IndexOf(':') + 1).Trim().TrimEnd('%'), out var conf))
                {
                    thought.Confidence = conf > 1 ? conf / 100 : conf;
                }
            }
        }
        
        // If no separate reasoning, use entire content
        if (string.IsNullOrEmpty(thought.Reasoning))
        {
            thought.Reasoning = content;
        }
        
        return thought;
    }
    
    /// <summary>
    /// Summarize thought steps
    /// </summary>
    private async Task<string> SummarizeThoughtsAsync(
        List<AevatarThoughtStep> thoughts,
        AevatarAIStrategyDependencies dependencies,
        CancellationToken cancellationToken)
    {
        if (!thoughts.Any())
        {
            return "No thoughts generated.";
        }
        
        // Build summary prompt
        var thoughtsSummary = string.Join("\n\n", thoughts.Select((t, i) =>
            $"Step {t.StepNumber}: {t.Thought}\n" +
            (string.IsNullOrEmpty(t.Reasoning) ? "" : $"Reasoning: {t.Reasoning}\n") +
            (string.IsNullOrEmpty(t.Conclusion) ? "" : $"Partial conclusion: {t.Conclusion}")));
        
        var prompt = $"Based on the following chain of thought:\n\n{thoughtsSummary}\n\n" +
                    "Provide a comprehensive final answer that synthesizes all the reasoning steps.";
        
        var response = await dependencies.LLMProvider.GenerateAsync(new AevatarLLMRequest
        {
            SystemPrompt = "You are summarizing a chain of thought reasoning process.",
            UserPrompt = prompt,
            Settings = new AevatarLLMSettings
            {
                ModelId = dependencies.Configuration.Model,
                Temperature = 0.3
            }
        }, cancellationToken);
        
        return response.Content;
    }
}
