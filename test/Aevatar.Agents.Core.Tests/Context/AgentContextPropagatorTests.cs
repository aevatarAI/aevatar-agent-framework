using Aevatar.Agents;
using Aevatar.Agents.Abstractions.Context;
using Aevatar.Agents.Core.Context;
using Shouldly;

namespace Aevatar.Agents.Core.Tests.Context;

/// <summary>
/// Unit tests for AgentContextPropagator
/// </summary>
public class AgentContextPropagatorTests
{
    [Fact(DisplayName = "InjectContext should add context metadata to envelope")]
    public void InjectContext_Should_Add_Context_Metadata_To_Envelope()
    {
        // Arrange
        var accessor = new AsyncLocalAgentContextAccessor();
        var context = new AsyncLocalAgentContext();
        context.Set(AgentContextKeys.UserId, "user123");
        context.Set(AgentContextKeys.Language, "en");
        accessor.Context = context;

        var propagator = new AgentContextPropagator(accessor);
        var envelope = new EventEnvelope();

        // Act
        propagator.InjectContext(envelope);

        // Assert
        envelope.ContextMetadata.ShouldContainKey("UserId");
        envelope.ContextMetadata.ShouldContainKey("Language");
        envelope.ContextMetadata["UserId"].StringValue.ShouldBe("user123");
        envelope.ContextMetadata["Language"].StringValue.ShouldBe("en");
    }

    [Fact(DisplayName = "InjectContext should do nothing when context is null")]
    public void InjectContext_Should_Do_Nothing_When_Context_Is_Null()
    {
        // Arrange
        var accessor = new AsyncLocalAgentContextAccessor();
        // Don't set any context
        var propagator = new AgentContextPropagator(accessor);
        var envelope = new EventEnvelope();

        // Act
        propagator.InjectContext(envelope);

        // Assert
        envelope.ContextMetadata.ShouldBeEmpty();
    }

    [Fact(DisplayName = "ExtractContext should create context from envelope metadata")]
    public void ExtractContext_Should_Create_Context_From_Envelope_Metadata()
    {
        // Arrange
        var accessor = new AsyncLocalAgentContextAccessor();
        var propagator = new AgentContextPropagator(accessor);
        
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString()
        };
        envelope.ContextMetadata["UserId"] = new ContextValue { StringValue = "user123" };
        envelope.ContextMetadata["Language"] = new ContextValue { StringValue = "en" };
        envelope.ContextMetadata["IsCN"] = new ContextValue { BoolValue = true };

        // Act
        var extractedContext = propagator.ExtractContext(envelope);

        // Assert
        extractedContext.ShouldNotBeNull();
        extractedContext!.Get("UserId").ShouldBe("user123");
        extractedContext.Get("Language").ShouldBe("en");
        extractedContext.Get("IsCN").ShouldBe(true);
    }

    [Fact(DisplayName = "ExtractContext should return null for empty metadata")]
    public void ExtractContext_Should_Return_Null_For_Empty_Metadata()
    {
        // Arrange
        var accessor = new AsyncLocalAgentContextAccessor();
        var propagator = new AgentContextPropagator(accessor);
        
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString()
        };
        // No metadata added

        // Act
        var extractedContext = propagator.ExtractContext(envelope);

        // Assert
        extractedContext.ShouldBeNull();
    }

    [Fact(DisplayName = "ApplyContext should update ambient context")]
    public void ApplyContext_Should_Update_Ambient_Context()
    {
        // Arrange
        var accessor = new AsyncLocalAgentContextAccessor();
        var propagator = new AgentContextPropagator(accessor);
        
        var envelope = new EventEnvelope();
        envelope.ContextMetadata["UserId"] = new ContextValue { StringValue = "user123" };

        // Act
        propagator.ApplyContext(envelope);

        // Assert
        accessor.Context.ShouldNotBeNull();
        accessor.Context!.Get("UserId").ShouldBe("user123");
    }

    [Fact(DisplayName = "Round trip should preserve context")]
    public void Round_Trip_Should_Preserve_Context()
    {
        // Arrange - Set up source context
        var sourceAccessor = new AsyncLocalAgentContextAccessor();
        var sourceContext = new AsyncLocalAgentContext();
        sourceContext.Set(AgentContextKeys.CorrelationId, "corr-123");
        sourceContext.Set(AgentContextKeys.UserId, "user456");
        sourceContext.Set(AgentContextKeys.IsCN, true);
        sourceAccessor.Context = sourceContext;

        var sourcePropagator = new AgentContextPropagator(sourceAccessor);

        // Create envelope and inject
        var envelope = new EventEnvelope();
        sourcePropagator.InjectContext(envelope);

        // Arrange - Set up destination
        var destAccessor = new AsyncLocalAgentContextAccessor();
        var destPropagator = new AgentContextPropagator(destAccessor);

        // Act - Extract at destination
        var extractedContext = destPropagator.ExtractContext(envelope);

        // Assert
        extractedContext.ShouldNotBeNull();
        extractedContext!.Get(AgentContextKeys.CorrelationId).ShouldBe("corr-123");
        extractedContext.Get(AgentContextKeys.UserId).ShouldBe("user456");
        extractedContext.Get(AgentContextKeys.IsCN).ShouldBe(true);
    }

    [Fact(DisplayName = "Should propagate all keys by default")]
    public void Should_Propagate_All_Keys_By_Default()
    {
        // Arrange
        var accessor = new AsyncLocalAgentContextAccessor();
        var context = new AsyncLocalAgentContext();
        context.Set(AgentContextKeys.UserId, "user123");
        context.Set("CustomKey", "custom-value");
        accessor.Context = context;

        var propagator = new AgentContextPropagator(accessor);
        var envelope = new EventEnvelope();

        // Act
        propagator.InjectContext(envelope);

        // Assert - All keys propagated by default
        envelope.ContextMetadata.ShouldContainKey("UserId");
        envelope.ContextMetadata.ShouldContainKey("CustomKey");
    }

    [Fact(DisplayName = "Should only propagate allowlisted keys when configured")]
    public void Should_Only_Propagate_Allowlisted_Keys_When_Configured()
    {
        // Arrange
        var accessor = new AsyncLocalAgentContextAccessor();
        var context = new AsyncLocalAgentContext();
        context.Set(AgentContextKeys.UserId, "user123");
        context.Set("CustomKey", "custom-value"); // Not in allowlist
        accessor.Context = context;

        var options = new AgentContextPropagationOptions().Allow("UserId");
        var propagator = new AgentContextPropagator(accessor, options);
        var envelope = new EventEnvelope();

        // Act
        propagator.InjectContext(envelope);

        // Assert
        envelope.ContextMetadata.ShouldContainKey("UserId");
        envelope.ContextMetadata.ShouldNotContainKey("CustomKey");
    }

    [Fact(DisplayName = "Should respect denied keys")]
    public void Should_Respect_Denied_Keys_In_Propagator()
    {
        // Arrange
        var accessor = new AsyncLocalAgentContextAccessor();
        var context = new AsyncLocalAgentContext();
        context.Set(AgentContextKeys.UserId, "user123");
        context.Set("SensitiveKey", "secret");
        accessor.Context = context;

        var options = new AgentContextPropagationOptions().Deny("SensitiveKey");
        var propagator = new AgentContextPropagator(accessor, options);
        var envelope = new EventEnvelope();

        // Act
        propagator.InjectContext(envelope);

        // Assert
        envelope.ContextMetadata.ShouldContainKey("UserId");
        envelope.ContextMetadata.ShouldNotContainKey("SensitiveKey");
    }
}
