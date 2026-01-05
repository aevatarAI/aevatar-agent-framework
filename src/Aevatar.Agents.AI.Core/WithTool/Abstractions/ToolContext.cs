using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.WithTool.Abstractions;

/// <summary>
/// Tool context
/// Contains all dependencies needed for creating and executing tools
/// </summary>
public class ToolContext
{
    /// <summary>
    /// Agent ID
    /// </summary>
    public string AgentId { get; set; } = string.Empty;
    
    /// <summary>
    /// Agent type name
    /// </summary>
    public string AgentType { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether to include core tools
    /// </summary>
    public bool IncludeCoreTools { get; set; } = true;
    
    /// <summary>
    /// Tool category filter (if empty, includes all categories)
    /// </summary>
    public IList<ToolCategory>? Categories { get; set; }
    
    /// <summary>
    /// Callback to get Agent state
    /// </summary>
    public Func<IMessage>? GetStateCallback { get; set; }

    /// <summary>
    /// Optional callback to generate embeddings (semantic search / rerank).
    /// <para/>
    /// NOTE:
    /// - This is a runtime-only callback (DI boundary), not a cross-runtime message.
    /// - Tools should treat it as best-effort and fall back when null.
    /// </summary>
    public Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<Embedding<float>>>>? GenerateEmbeddingsAsync { get; set; }
    
    /// <summary>
    /// Callback to publish events
    /// </summary>
    public Func<IMessage, Task>? PublishEventCallback { get; set; }

    /// <summary>
    /// Callback to publish events with explicit propagation direction.
    /// <para/>
    /// Preferred over <see cref="PublishEventCallback"/> when available.
    /// </summary>
    public Func<IMessage, EventDirection, CancellationToken, Task<string>>? PublishEventWithDirectionCallback { get; set; }
    
    /// <summary>
    /// Callback to get session ID
    /// </summary>
    public Func<string>? GetSessionIdCallback { get; set; }
    
    /// <summary>
    /// Logger
    /// </summary>
    public ILogger? Logger { get; set; }
    
    /// <summary>
    /// Additional configuration data
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}