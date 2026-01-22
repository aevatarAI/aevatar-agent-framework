using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.AI.Core;

// ============================================================
//  RoleAIGAgent
//
//  说明：
//  - 通用 role 驱动 Agent，承载大多数角色（coder/reviewer/...）。
//  - 行为由 YAML + 工具策略装配，类本身保持极简。
// ============================================================
public abstract class RoleAIGAgent<TCustomState> : AIGAgentBase<TCustomState>
    where TCustomState : class, IMessage<TCustomState>, new()
{
    private string? _sessionId;
    private IStepExecutionHandler? _stepExecutionHandler;

    protected string? SessionId => _sessionId;

    public string Role { get; private set; } = "role";

    public void InitializeRole(string? role)
    {
        var value = (role ?? string.Empty).Trim();
        Role = value.Length == 0 ? "role" : value;
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        if (State.Context.TryGetValue(ChatRequest.SessionIdKey, out var raw) &&
            !string.IsNullOrWhiteSpace(raw))
        {
            SetSessionId(raw.Trim());
        }
        else if (State.Context.TryGetValue(ChatRequest.SessionIdKeyCamel, out var camel) &&
                 !string.IsNullOrWhiteSpace(camel))
        {
            SetSessionId(camel.Trim());
        }
    }

    public virtual void ConfigureSessionContext(
        string sessionId,
        bool enableSessionMemory,
        bool enableAgentMemory)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return;

        SetSessionId(sessionId);

        EnableSessionMemoryStoreAppend = enableSessionMemory;
        EnableMemoryStoreAppend = enableAgentMemory;
    }

    protected void SetSessionId(string sessionId)
    {
        _sessionId = (sessionId ?? string.Empty).Trim();
        if (_sessionId.Length == 0)
            return;

        State.Context[ChatRequest.SessionIdKey] = _sessionId;
    }

    protected virtual void ApplySessionContext(ChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(_sessionId))
            return;

        request.SetSessionId(_sessionId);
    }

    public void SetStepExecutionHandler(IStepExecutionHandler? handler)
    {
        _stepExecutionHandler = handler;
    }

    [AllEventHandler]
    protected virtual async Task HandleEventEnvelope(EventEnvelope envelope)
    {
        var handler = _stepExecutionHandler;
        if (handler == null || envelope == null)
            return;

        if (!handler.CanHandle(envelope))
            return;

        var result = await handler.HandleAsync(envelope, this, CancellationToken.None);
        if (result?.Response == null)
            return;

        await PublishAsync(result.Response, result.Direction);
    }

    public override Task<string> GetDescriptionAsync()
        => Task.FromResult($"RoleAIGAgent({Role})");
}

public sealed class RoleAIGAgent : RoleAIGAgent<Empty>
{
}
