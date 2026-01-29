using System.Text;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI.Core.Messages;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

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
    private IEventModule[] _eventModules = [];

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
        UpdateModules(modules =>
        {
            modules.RemoveAll(m => m is StepExecutionModule);
            if (handler != null)
            {
                modules.Add(new StepExecutionModule(handler));
            }

            return modules;
        });
    }

    public void RegisterEventModule(IEventModule module)
    {
        if (module == null) return;

        UpdateModules(modules =>
        {
            modules.RemoveAll(m => string.Equals(m.Name, module.Name, StringComparison.OrdinalIgnoreCase));
            modules.Add(module);
            return modules;
        });
    }

    public void SetEventModules(IEnumerable<IEventModule> modules)
    {
        UpdateModules(_ =>
        {
            var list = new List<IEventModule>();
            foreach (var module in modules)
            {
                if (module != null)
                    list.Add(module);
            }

            return list;
        });
    }

    public IReadOnlyList<IEventModule> GetEventModules()
    {
        return SnapshotModules();
    }

    // HandleChatRequestEvent 由基类 AIGAgentBase 处理，
    // 使用 EventDirection.Self + OnlySelfHandling 模式

    [AllEventHandler]
    protected virtual async Task HandleEventEnvelope(EventEnvelope envelope)
    {
        if (envelope == null)
            return;

        var modules = SnapshotModules();
        if (modules.Length == 0)
            return;

        var host = new EventModuleHost(this);
        foreach (var module in modules)
        {
            try
            {
                if (!module.CanHandle(envelope))
                    continue;

                await module.HandleAsync(envelope, host, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[EventModule] {Module} failed on {EventType}",
                    module.Name, envelope.Payload?.TypeUrl ?? "unknown");
            }
        }
    }

    public override Task<string> GetDescriptionAsync()
        => Task.FromResult($"RoleAIGAgent({Role})");

    private IEventModule[] SnapshotModules()
    {
        return Volatile.Read(ref _eventModules);
    }

    private Task PublishModuleAsync(IMessage evt, EventDirection direction, CancellationToken ct)
        => PublishAsync((dynamic)evt, direction, ct);

    private void UpdateModules(Func<List<IEventModule>, List<IEventModule>> update)
    {
        while (true)
        {
            var snapshot = Volatile.Read(ref _eventModules);
            var next = update(new List<IEventModule>(snapshot));
            var ordered = next
                .OrderBy(m => m.Priority)
                .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var original = Interlocked.CompareExchange(ref _eventModules, ordered, snapshot);
            if (ReferenceEquals(original, snapshot))
                return;
        }
    }

    private sealed class EventModuleHost(RoleAIGAgent<TCustomState> owner) : IEventModuleHost
    {
        private readonly RoleAIGAgent<TCustomState> _owner = owner;

        public string AgentId => _owner.Id;
        public AIGAgentBase Agent => _owner;
        public ILogger Logger => _owner.Logger;

        public Task PublishAsync(IMessage evt, EventDirection direction, CancellationToken ct)
            => _owner.PublishModuleAsync(evt, direction, ct);
    }

}

public class RoleAIGAgent : RoleAIGAgent<Empty>
{
}
