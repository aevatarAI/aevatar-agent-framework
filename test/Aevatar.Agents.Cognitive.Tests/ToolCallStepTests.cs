using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.Evolution;
using Aevatar.Agents.Cognitive.Execution;
using Aevatar.Agents.Cognitive.Messages;
using Aevatar.Agents.Cognitive.Utilities;
using Aevatar.Agents.Core.Helpers;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Cognitive.Tests;

public class ToolCallStepTests
{
    [Fact]
    public async Task ToolCallStep_ShouldExecuteTool()
    {
        var agent = new TestToolAgent
        {
            ToolEvolutionOptions = new ToolEvolutionOptions
            {
                EnableToolCalls = true
            }
        };

        AgentEventPublisherInjector.InjectEventPublisher(agent, NullEventPublisher.Instance);

        var init = typeof(Aevatar.Agents.AI.Core.AIGAgentBase)
            .GetMethod("InternalInitializeToolsAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        init.ShouldNotBeNull();
        await (Task)init!.Invoke(agent, new object?[] { CancellationToken.None })!;

        var handler = new CognitiveStepExecutionHandler();
        var request = new ExecuteStepRequestEvent
        {
            RequestId = "req",
            StepId = "step1",
            StepType = "tool_call"
        };

        request.Parameters["tool"] = ProtoValueConverter.ToProto("echo");
        request.Parameters["args"] = ProtoValueConverter.ToProto(new Dictionary<string, object>
        {
            ["text"] = "hi"
        });

        var envelope = new EventEnvelope
        {
            Payload = Any.Pack(request)
        };

        var result = await handler.HandleAsync(envelope, agent, CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Response.ShouldBeOfType<StepCompletedEventProto>();
        var completed = (StepCompletedEventProto)result.Response;
        completed.Success.ShouldBeTrue(completed.Error);
        completed.Result.ShouldContain("hi");
    }

    private sealed class TestToolAgent : Aevatar.Agents.AI.Core.RoleAIGAgent
    {
        protected override async Task RegisterToolsAsync(CancellationToken cancellationToken = default)
        {
            await RegisterToolAsync(new EchoTool(), NullLogger.Instance, cancellationToken);
        }

        public override Task<string> GetDescriptionAsync()
            => Task.FromResult("test tool agent");
    }

    private sealed class EchoTool : AevatarToolBase
    {
        public override string Name => "echo";
        public override string Description => "Echo tool";

        public override ToolParameters CreateParameters()
        {
            return new ToolParameters
            {
                Required = new List<string> { "text" },
                Items = new Dictionary<string, ToolParameter>
                {
                    ["text"] = new ToolParameter
                    {
                        Type = "string",
                        Description = "Text to echo",
                        Required = true
                    }
                }
            };
        }

        public override Task<IMessage> ExecuteAsync(
            Dictionary<string, object> parameters,
            ToolContext context,
            Microsoft.Extensions.Logging.ILogger? logger,
            CancellationToken cancellationToken = default)
        {
            var text = parameters.TryGetValue("text", out var value)
                ? value?.ToString() ?? string.Empty
                : string.Empty;
            return Task.FromResult<IMessage>(new StringValue { Value = text });
        }
    }
}
