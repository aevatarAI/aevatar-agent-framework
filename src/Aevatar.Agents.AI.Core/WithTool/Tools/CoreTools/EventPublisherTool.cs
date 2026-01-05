using System.Text.Json;
using System.Text.Json.Nodes;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.WithTool.Abstractions;
using Aevatar.Agents.AI.WithTool.Messages;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.WithTool.Tools.CoreTools;

/// <summary>
/// Event publishing tool implementation
/// Used to publish events to Agent stream
/// </summary>
public class EventPublisherTool : AevatarToolBase
{
    /// <inheritdoc />
    public override string Name => "publish_event";

    /// <inheritdoc />
    public override string Description => "Publish an event to the agent stream";

    /// <inheritdoc />
    public override ToolCategory Category => ToolCategory.Core;

    /// <inheritdoc />
    public override string Version => "1.0.0";

    /// <inheritdoc />
    public override IList<string> Tags => new List<string> { "core", "event", "communication" };

    /// <inheritdoc />
    protected override bool RequiresInternalAccess() => true;

    /// <inheritdoc />
    protected override bool CanBeOverridden() => false;

    /// <inheritdoc />
    protected override bool IsDangerous() => true;

    /// <inheritdoc />
    protected override bool RequiresConfirmation() => true;

    /// <inheritdoc />
    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Items = new Dictionary<string, ToolParameter>
            {
                ["event_type"] = new()
                {
                    Type = "string",
                    Required = true,
                    Description = "The type of event to publish"
                },
                ["payload"] = new()
                {
                    Type = "object",
                    Required = true,
                    Description = "The event payload data"
                },
                ["direction"] = new()
                {
                    Type = "string",
                    Enum = new[] { "up", "down", "both" },
                    Description = "The direction to publish the event",
                    DefaultValue = "both"
                }
            },
            Required = new[] { "event_type", "payload" }
        };
    }

    /// <inheritdoc />
    public override async Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        // Validate parameters
        var validation = ValidateParameters(ToNullableParameters(parameters));
        if (!validation.IsValid)
        {
            logger?.LogWarning("Invalid parameters: {Errors}", string.Join(", ", validation.Errors));
            var errorResult = new { success = false, errors = validation.Errors };
            return JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(errorResult));
        }

        if (context.PublishEventWithDirectionCallback == null && context.PublishEventCallback == null)
        {
            logger?.LogWarning("PublishEventCallback not provided, cannot publish event");
            var errorResult = new { success = false, error = "Event publishing not available" };
            return JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(errorResult));
        }

        try
        {
            var eventType = parameters["event_type"]?.ToString();
            var payload = parameters["payload"];
            var directionStr = parameters.GetValueOrDefault("direction", "both")?.ToString();
            var direction = ParseDirection(directionStr);

            logger?.LogInformation("Publishing event {EventType} with direction {Direction}",
                eventType, direction);

            if (string.IsNullOrWhiteSpace(eventType))
            {
                var errorResult = new { success = false, error = "Parameter 'event_type' is required." };
                return JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(errorResult));
            }

            // Tool calls are JSON-first; publish a Protobuf message that carries the JSON payload.
            var payloadJson = SerializePayload(payload);
            var toolEvent = new AevatarToolPublishedEvent
            {
                EventType = eventType,
                PayloadJson = payloadJson,
                AgentId = context.AgentId,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };

            string? publishedEventId = null;
            var usedDirectionalCallback = false;

            if (context.PublishEventWithDirectionCallback != null)
            {
                usedDirectionalCallback = true;
                publishedEventId = await context.PublishEventWithDirectionCallback(toolEvent, direction, cancellationToken);
            }
            else
            {
                await context.PublishEventCallback!(toolEvent);
            }

            var result = new
            {
                success = true,
                eventType = eventType,
                direction = directionStr ?? "both",
                published = true,
                eventId = publishedEventId,
                usedDirectionalCallback
            };
            
            return JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(result));
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error publishing event");
            var errorResult = new { success = false, error = ex.Message };
            return JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(errorResult));
        }
    }

    /// <summary>
    /// Parse direction string to <see cref="EventDirection"/>.
    /// </summary>
    private static EventDirection ParseDirection(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "up" => EventDirection.Up,
            "down" => EventDirection.Down,
            "both" => EventDirection.Both,
            _ => EventDirection.Both
        };
    }

    /// <summary>
    /// Serialize event payload
    /// </summary>
    private static string SerializePayload(object payload)
    {
        return payload switch
        {
            string str => str,
            JsonNode jNode => jNode.ToJsonString(),
            JsonElement jElement => jElement.GetRawText(),
            _ => JsonSerializer.Serialize(payload)
        };
    }
}