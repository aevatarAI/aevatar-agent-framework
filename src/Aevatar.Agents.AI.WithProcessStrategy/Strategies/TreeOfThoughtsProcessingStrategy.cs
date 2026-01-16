using System.Collections.Concurrent;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.AI.WithProcessStrategy.Messages;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.WithProcessStrategy.Strategies;

/// <summary>
/// Tree of Thoughts AI processing strategy
/// Explores multiple thought branches, evaluates and selects optimal path
/// </summary>
public class TreeOfThoughtsProcessingStrategy : IAevatarAIProcessingStrategy
{
    public string Name => "Tree of Thoughts Processing";
    
    public string Description => "Tree of Thoughts strategy - Solves complex problems by exploring multiple thought paths, suitable for scenarios requiring comprehensive exploration";
    
    public AevatarAIProcessingMode Mode => AevatarAIProcessingMode.TreeOfThoughts;
    
    public bool CanHandle(AevatarAIContext context)
    {
        // Suitable for extremely complex problems requiring multi-path exploration
        if (context.Metadata?.ContainsKey("PreferredStrategy") == true)
        {
            var preferred = context.Metadata["PreferredStrategy"]?.ToString();
            return string.Equals(preferred, "TreeOfThoughts", StringComparison.OrdinalIgnoreCase);
        }
        
        // Suitable for creative problems or problems with multiple solutions
        var question = context.Question?.ToLower() ?? string.Empty;
        return question.Contains("explore") || question.Contains("solutions") ||
               question.Contains("possibilities") || question.Contains("options");
    }
    
    public double EstimateComplexity(AevatarAIContext context)
    {
        // Tree of Thoughts suitable for high complexity problems
        return 0.9;
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
        dependencies.Logger?.LogDebug("Processing with Tree of Thoughts strategy");
        
        var maxDepth = dependencies.Configuration.MaxTreeDepth ?? 3;
        var branchingFactor = dependencies.Configuration.TreeBranchingFactor ?? 3;
        var maxNodes = dependencies.Configuration.MaxTreeNodes ?? 20;
        
        // Initialize root node
        var root = new ThoughtNode
        {
            Id = Guid.NewGuid().ToString(),
            Content = context.Question ?? "Process the event",
            Depth = 0,
            Score = 1.0
        };
        
        // Use priority queue to manage nodes to explore (based on score)
        var frontier = new PriorityQueue<ThoughtNode, double>();
        frontier.Enqueue(root, -root.Score); // Negative score for descending order
        
        var exploredNodes = new List<ThoughtNode> { root };
        var solutions = new List<ThoughtNode>();
        
        while (frontier.Count > 0 && exploredNodes.Count < maxNodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            // Get most promising node
            var currentNode = frontier.Dequeue();
            
            dependencies.Logger?.LogDebug("ToT: Exploring node at depth {Depth} with score {Score}", 
                currentNode.Depth, currentNode.Score);
            
            // Check if is solution
            if (await IsSolutionAsync(currentNode, context, dependencies, cancellationToken))
            {
                solutions.Add(currentNode);
                dependencies.Logger?.LogInformation("ToT: Found solution with score {Score}", currentNode.Score);
                
                // If high-quality solution found, can end early
                if (currentNode.Score > 0.9)
                {
                    break;
                }
                continue;
            }
            
            // If not reached max depth, generate child nodes
            if (currentNode.Depth < maxDepth)
            {
                var children = await GenerateChildrenAsync(
                    currentNode, 
                    branchingFactor, 
                    context,
                    dependencies, 
                    cancellationToken);
                
                foreach (var child in children)
                {
                    exploredNodes.Add(child);
                    
                    // Evaluate child node
                    child.Score = await EvaluateNodeAsync(child, context, dependencies, cancellationToken);
                    
                    // If score high enough, add to exploration queue
                    if (child.Score > 0.3) // Threshold filters low-quality branches
                    {
                        frontier.Enqueue(child, -child.Score);
                    }
                }
            }
        }
        
        // Select best solution or path
        if (solutions.Any())
        {
            var bestSolution = solutions.OrderByDescending(s => s.Score).First();
            return await GenerateFinalAnswerFromNodeAsync(bestSolution, dependencies, cancellationToken);
        }
        
        // If no clear solution found, select most promising leaf node
        var bestLeaf = exploredNodes
            .Where(n => n.Children.Count == 0)
            .OrderByDescending(n => n.Score)
            .FirstOrDefault();
        
        if (bestLeaf != null)
        {
            return await GenerateFinalAnswerFromNodeAsync(bestLeaf, dependencies, cancellationToken);
        }
        
        dependencies.Logger?.LogWarning("ToT: No viable solution found");
        return "Unable to find a satisfactory solution through tree exploration.";
    }
    
    /// <summary>
    /// Generate child nodes (thought branches)
    /// </summary>
    private async Task<List<ThoughtNode>> GenerateChildrenAsync(
        ThoughtNode parent,
        int count,
        AevatarAIContext context,
        AevatarAIStrategyDependencies dependencies,
        CancellationToken cancellationToken)
    {
        var children = new List<ThoughtNode>();
        
        // Build generation prompt
        var pathToRoot = GetPathToRoot(parent);
        var thoughtChain = string.Join(" -> ", pathToRoot.Select(n => n.Content));
        
        var prompt = $"Original question: {context.Question}\n\n" +
                    $"Current thought path:\n{thoughtChain}\n\n" +
                    $"Generate {count} different next steps or approaches to explore. " +
                    "Each should be distinct and explore a different aspect or approach. " +
                    "Format each as a separate line starting with a number.";
        
        var response = await dependencies.LLMProvider.GenerateAsync(new AevatarLLMRequest
        {
            SystemPrompt = "You are exploring different thought paths to solve a problem. Be creative and thorough.",
            UserPrompt = prompt,
            Settings = new AevatarLLMSettings
            {
                ModelId = dependencies.Configuration.Model,
                Temperature = 0.7 // Higher temperature for diversity
            }
        }, cancellationToken);
        
        // Parse response into multiple thoughts
        var thoughts = ParseMultipleThoughts(response.Content, count);
        
        foreach (var thought in thoughts)
        {
            var child = new ThoughtNode
            {
                Id = Guid.NewGuid().ToString(),
                Content = thought,
                Parent = parent,
                Depth = parent.Depth + 1
            };
            
            parent.Children.Add(child);
            children.Add(child);
        }
        
        return children;
    }
    
    /// <summary>
    /// Evaluate node quality
    /// </summary>
    private async Task<double> EvaluateNodeAsync(
        ThoughtNode node,
        AevatarAIContext context,
        AevatarAIStrategyDependencies dependencies,
        CancellationToken cancellationToken)
    {
        var pathToRoot = GetPathToRoot(node);
        var thoughtChain = string.Join(" -> ", pathToRoot.Select(n => n.Content));
        
        var prompt = $"Question: {context.Question}\n\n" +
                    $"Thought path:\n{thoughtChain}\n\n" +
                    "Evaluate this thought path on a scale of 0 to 1:\n" +
                    "- How relevant is it to answering the question?\n" +
                    "- How logical and coherent is the reasoning?\n" +
                    "- How promising is this direction?\n" +
                    "Respond with just a number between 0 and 1.";
        
        var response = await dependencies.LLMProvider.GenerateAsync(new AevatarLLMRequest
        {
            SystemPrompt = "You are evaluating the quality of a reasoning path. Be objective and critical.",
            UserPrompt = prompt,
            Settings = new AevatarLLMSettings
            {
                ModelId = dependencies.Configuration.Model,
                Temperature = 0.1,
                MaxTokens = 10
            }
        }, cancellationToken);
        
        if (double.TryParse(response.Content.Trim(), out var score))
        {
            return Math.Max(0, Math.Min(1, score)); // Clamp to [0, 1]
        }
        
        return 0.5; // Default score if parsing fails
    }
    
    /// <summary>
    /// Check if node is a solution
    /// </summary>
    private async Task<bool> IsSolutionAsync(
        ThoughtNode node,
        AevatarAIContext context,
        AevatarAIStrategyDependencies dependencies,
        CancellationToken cancellationToken)
    {
        // Root node is not a solution
        if (node.Depth == 0)
        {
            return false;
        }
        
        var pathToRoot = GetPathToRoot(node);
        var thoughtChain = string.Join(" -> ", pathToRoot.Select(n => n.Content));
        
        var prompt = $"Question: {context.Question}\n\n" +
                    $"Thought path:\n{thoughtChain}\n\n" +
                    "Does this thought path contain a complete answer to the question? " +
                    "Respond with YES or NO.";
        
        var response = await dependencies.LLMProvider.GenerateAsync(new AevatarLLMRequest
        {
            SystemPrompt = "Determine if a reasoning path provides a complete answer.",
            UserPrompt = prompt,
            Settings = new AevatarLLMSettings
            {
                ModelId = dependencies.Configuration.Model,
                Temperature = 0.1,
                MaxTokens = 10
            }
        }, cancellationToken);
        
        return response.Content.Contains("YES", StringComparison.OrdinalIgnoreCase);
    }
    
    /// <summary>
    /// Generate final answer from node
    /// </summary>
    private async Task<string> GenerateFinalAnswerFromNodeAsync(
        ThoughtNode node,
        AevatarAIStrategyDependencies dependencies,
        CancellationToken cancellationToken)
    {
        var pathToRoot = GetPathToRoot(node);
        var thoughtChain = string.Join("\n", pathToRoot.Select((n, i) => 
            $"{new string(' ', i * 2)}Step {i + 1}: {n.Content}"));
        
        var prompt = $"Based on this reasoning path:\n{thoughtChain}\n\n" +
                    "Provide a clear, comprehensive final answer that synthesizes all the insights from this thought process.";
        
        var response = await dependencies.LLMProvider.GenerateAsync(new AevatarLLMRequest
        {
            SystemPrompt = "Synthesize the reasoning path into a final answer.",
            UserPrompt = prompt,
            Settings = new AevatarLLMSettings
            {
                ModelId = dependencies.Configuration.Model,
                Temperature = 0.3
            }
        }, cancellationToken);
        
        // Publish Tree of Thoughts completion event
        if (dependencies.PublishEventCallback != null)
        {
            await dependencies.PublishEventCallback(new AevatarTreeOfThoughtsCompletedEvent
            {
                AgentId = dependencies.AgentId,
                ThoughtTreeId = Guid.NewGuid().ToString(),
                SelectedPath = thoughtChain,
                Confidence = node.Score,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            });
        }
        
        return response.Content;
    }
    
    /// <summary>
    /// Get path from node to root
    /// </summary>
    private List<ThoughtNode> GetPathToRoot(ThoughtNode node)
    {
        var path = new List<ThoughtNode>();
        var current = node;
        
        while (current != null)
        {
            path.Insert(0, current); // Insert at beginning to maintain root-to-leaf order
            current = current.Parent;
        }
        
        return path;
    }
    
    /// <summary>
    /// Parse multiple thoughts
    /// </summary>
    private List<string> ParseMultipleThoughts(string content, int expectedCount)
    {
        var thoughts = new List<string>();
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            
            // Remove numeric prefix (e.g., "1. ", "2) ", etc.)
            var cleaned = System.Text.RegularExpressions.Regex.Replace(
                trimmed, @"^\d+[\.\)]\s*", "");
            
            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                thoughts.Add(cleaned);
            }
            
            if (thoughts.Count >= expectedCount)
            {
                break;
            }
        }
        
        // If not enough thoughts, add defaults
        while (thoughts.Count < expectedCount)
        {
            thoughts.Add($"Alternative approach {thoughts.Count + 1}");
        }
        
        return thoughts;
    }
}

/// <summary>
/// Tree of Thoughts node
/// </summary>
internal class ThoughtNode
{
    public string Id { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public ThoughtNode? Parent { get; set; }
    public List<ThoughtNode> Children { get; set; } = new();
    public int Depth { get; set; }
    public double Score { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}
