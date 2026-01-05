# AI Processing Strategies

## Overview

The AI Agent Framework now uses a **Strategy Pattern** to handle different AI processing modes. This design provides flexibility, maintainability, and extensibility for various AI reasoning approaches.

## Architecture

```
AIGAgentBase
    ↓
    ├── IAevatarAIProcessingStrategyFactory (creates strategies)
    │       ↓
    └── IAevatarAIProcessingStrategy (interface)
            ├── StandardProcessingStrategy
            ├── ChainOfThoughtProcessingStrategy
            ├── ReActProcessingStrategy
            └── TreeOfThoughtsProcessingStrategy
```

## Key Components

### 1. IAevatarAIProcessingStrategy
The core interface that all processing strategies must implement:

```csharp
public interface IAevatarAIProcessingStrategy
{
    string Name { get; }
    AevatarAIProcessingMode Mode { get; }
    Task<string> ProcessAsync(
        AevatarAIContext context,
        AevatarAIEventHandlerAttribute? config,
        AevatarAIStrategyDependencies dependencies,
        CancellationToken cancellationToken);
}
```

### 2. Strategy Implementations

#### StandardProcessingStrategy
- Direct prompt-response mode
- Supports function calling
- Ideal for straightforward queries

#### ChainOfThoughtProcessingStrategy
- Step-by-step reasoning
- Builds thought chains
- Good for complex problem solving

#### ReActProcessingStrategy
- Combines Reasoning + Acting
- Iterative approach with observations
- Excellent for task completion with tool usage

#### TreeOfThoughtsProcessingStrategy
- Explores multiple reasoning branches
- Evaluates and selects best paths
- Best for complex decision-making

### 3. AevatarAIStrategyDependencies
Encapsulates all dependencies needed by strategies:

```csharp
public class AevatarAIStrategyDependencies
{
    public IAevatarLLMProvider LLMProvider { get; init; }
    public IAevatarPromptManager PromptManager { get; init; }
    public IAevatarToolManager ToolManager { get; init; }
    public AevatarAIAgentConfiguration Configuration { get; init; }
    public ILogger? Logger { get; init; }
    public string AgentId { get; init; }
    public Func<IMessage, Task>? PublishEventCallback { get; init; }
    public Func<string, Dictionary<string, object>, CancellationToken, Task<object?>>? ExecuteToolCallback { get; init; }
}
```

## Usage Examples

### Basic Usage in an AI Agent

```csharp
public class MyAIAgent : AIGAgentBase<MyAgentState>
{
    [AevatarAIEventHandler(Mode = AevatarAIProcessingMode.ChainOfThought)]
    public async Task<IMessage?> HandleComplexQuery(QueryEvent evt, EventEnvelope envelope)
    {
        // The base class will automatically use ChainOfThoughtProcessingStrategy
        return await ProcessWithAIAsync(envelope, new AevatarAIEventHandlerAttribute
        {
            Mode = AevatarAIProcessingMode.ChainOfThought,
            PromptTemplate = "Solve this step by step: {question}"
        });
    }
}
```

### Custom Strategy Implementation

```csharp
public class CustomProcessingStrategy : IAevatarAIProcessingStrategy
{
    public string Name => "Custom Strategy";
    public AevatarAIProcessingMode Mode => AevatarAIProcessingMode.Custom;
    
    public async Task<string> ProcessAsync(
        AevatarAIContext context,
        AevatarAIEventHandlerAttribute? config,
        AevatarAIStrategyDependencies dependencies,
        CancellationToken cancellationToken)
    {
        // Your custom processing logic here
        var customPrompt = $"Custom processing for: {context.Question}";
        
        var response = await dependencies.LLMProvider.GenerateAsync(
            new AevatarLLMRequest
            {
                UserPrompt = customPrompt,
                Settings = new AevatarLLMSettings
                {
                    Temperature = 0.7
                }
            }, 
            cancellationToken);
            
        return response.Content;
    }
}

// Register the custom strategy
var factory = new AevatarAIProcessingStrategyFactory();
factory.RegisterStrategy(AevatarAIProcessingMode.Custom, new CustomProcessingStrategy());
```

### Dependency Injection Setup

```csharp
// In your Startup.cs or Program.cs
services.AddAevatarAIProcessingStrategies();

// Custom strategy registration
services.AddTransient<CustomProcessingStrategy>();
services.AddSingleton<IAevatarAIProcessingStrategyFactory>(provider =>
{
    var factory = new AevatarAIProcessingStrategyFactory(provider);
    factory.RegisterStrategyType(
        AevatarAIProcessingMode.Custom, 
        typeof(CustomProcessingStrategy));
    return factory;
});
```

## Benefits of This Architecture

1. **Separation of Concerns**: Each strategy focuses on its specific reasoning approach
2. **Testability**: Strategies can be unit tested independently
3. **Extensibility**: Easy to add new processing modes without modifying existing code
4. **Reusability**: Strategies can be shared across different agent types
5. **Maintainability**: Changes to one strategy don't affect others
6. **Configuration**: Each strategy can have its own configuration parameters

## Performance Considerations

- Strategies are stateless and can be cached/reused
- The factory maintains a cache of strategy instances
- Consider using `useCache: false` when creating one-time strategies
- Dependencies are created per-request to ensure fresh state

## Migration from Old Code

If you were previously overriding `ProcessStandardAsync`, `ProcessChainOfThoughtAsync`, etc. in your agents:

**Before:**
```csharp
protected override async Task<string> ProcessChainOfThoughtAsync(...)
{
    // Custom chain of thought logic
}
```

**After:**
```csharp
// Option 1: Create a custom strategy
public class MyChainOfThoughtStrategy : ChainOfThoughtProcessingStrategy
{
    public override async Task<string> ProcessAsync(...)
    {
        // Your custom logic
    }
}

// Option 2: Override CreateStrategyDependencies to customize behavior
protected override AevatarAIStrategyDependencies CreateStrategyDependencies()
{
    var deps = base.CreateStrategyDependencies();
    // Customize dependencies
    return deps;
}
```

## Testing Strategies

```csharp
[Test]
public async Task TestChainOfThoughtStrategy()
{
    // Arrange
    var strategy = new ChainOfThoughtProcessingStrategy();
    var context = new AevatarAIContext { Question = "Test question" };
    var dependencies = new AevatarAIStrategyDependencies
    {
        LLMProvider = mockLLMProvider,
        PromptManager = mockPromptManager,
        // ... other mocked dependencies
    };
    
    // Act
    var result = await strategy.ProcessAsync(
        context, 
        null, 
        dependencies, 
        CancellationToken.None);
    
    // Assert
    Assert.NotNull(result);
    // ... additional assertions
}
```

## Future Enhancements

- **Strategy Composition**: Combine multiple strategies
- **Dynamic Strategy Selection**: Choose strategy based on input characteristics
- **Strategy Metrics**: Track performance and success rates
- **Parallel Strategy Execution**: Run multiple strategies and select best result
- **Learning Strategies**: Strategies that improve over time
