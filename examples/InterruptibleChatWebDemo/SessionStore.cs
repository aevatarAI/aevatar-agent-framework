using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Rpc;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.Core.Runtime;
using Aevatar.Agents.Runtime.Local;
using Aevatar.Agents.Rpc;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Options;

namespace InterruptibleChatWebDemo;

public sealed class SessionStore
{
    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new(StringComparer.Ordinal);
    private readonly DemoOptions _options;
    private readonly IGAgentFactory _agentFactory;
    private readonly LLMProvidersConfig _llmProviders;

    public SessionStore(
        IOptions<DemoOptions> options,
        IGAgentFactory agentFactory,
        IOptions<LLMProvidersConfig> llmProviders)
    {
        _options = options.Value ?? new DemoOptions();
        _agentFactory = agentFactory;
        _llmProviders = llmProviders.Value ?? new LLMProvidersConfig();
    }

    public SessionEntry GetOrCreate(string sessionId)
    {
        sessionId = (sessionId ?? string.Empty).Trim();
        if (sessionId.Length == 0) throw new ArgumentException("sessionId is required", nameof(sessionId));

        return _sessions.GetOrAdd(sessionId, id => new SessionEntry(id, _options, _agentFactory, _llmProviders));
    }

    public sealed class SessionEntry
    {
        private readonly object _gate = new();
        private int _seq;

        public string SessionId { get; }

        public string? ActiveRunId { get; private set; }

        private readonly LocalMessageStreamRegistry _registry = new();
        private readonly InterruptibleChatAgent _agent;
        private readonly LocalGAgentActor _actor;
        private bool _initialized;

        // SSE subscribers
        private readonly List<ChannelWriter<string>> _subscribers = new();

        public SessionEntry(
            string sessionId,
            DemoOptions options,
            IGAgentFactory agentFactory,
            LLMProvidersConfig llmProviders)
        {
            SessionId = sessionId;
            _agent = agentFactory.CreateGAgent<InterruptibleChatAgent>(sessionId); // stable agent id per session

            _actor = new LocalGAgentActor(_agent, _registry);
            _actor.ActivateAsync().GetAwaiter().GetResult();

            // Best-effort init: like Cursor, we want the first chat to work without the user thinking about init.
            // Missing API key -> show a clear hint in UI.
            EnsureInitializedOrNotify(llmProviders, options);

            // Subscribe to agent stream and forward assistant deltas to SSE clients.
            var stream = _registry.GetOrCreateStream(sessionId);
            _ = stream.SubscribeAsync<EventEnvelope>(
                envelope =>
                {
                    if (envelope.Payload?.Is(StringValue.Descriptor) != true)
                        return Task.CompletedTask;

                    var v = envelope.Payload.Unpack<StringValue>().Value ?? string.Empty;
                    // v format: "{runId}|assistant|delta"
                    var parts = v.Split('|', 3);
                    if (parts.Length < 3)
                        return Task.CompletedTask;

                    var runId = parts[0];
                    var role = parts[1];
                    var delta = parts[2];

                    var payload = JsonSerializer.Serialize(new
                    {
                        type = "assistant_delta",
                        runId,
                        role,
                        delta
                    });

                    Broadcast(payload);
                    return Task.CompletedTask;
                },
                null,
                CancellationToken.None);
        }

        public ChannelReader<string> Subscribe()
        {
            var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = true
            });

            lock (_gate)
            {
                _subscribers.Add(channel.Writer);
            }

            // Tell the client current run id (best-effort).
            Broadcast(JsonSerializer.Serialize(new
            {
                type = "hello",
                sessionId = SessionId,
                activeRunId = ActiveRunId ?? ""
            }));

            return channel.Reader;
        }

        public void Unsubscribe(ChannelReader<string> reader)
        {
            // Best-effort cleanup: we can't map reader -> writer easily without wrapper.
            // Keep it simple for demo: writers that fail will be removed during broadcast.
        }

        public async Task<string> StartInputAsync(string message)
        {
            message = (message ?? string.Empty).Replace("\r", "").Trim();
            if (message.Length == 0) throw new ArgumentException("message is required", nameof(message));

            var runId = $"{SessionId}:{Interlocked.Increment(ref _seq)}";

            string? prev;
            lock (_gate)
            {
                prev = ActiveRunId;
                ActiveRunId = runId;
            }

            if (!string.IsNullOrWhiteSpace(prev))
            {
                Broadcast(JsonSerializer.Serialize(new
                {
                    type = "run_interrupted",
                    oldRunId = prev,
                    newRunId = runId
                }));
            }

            // Emit user message immediately.
            Broadcast(JsonSerializer.Serialize(new
            {
                type = "user_message",
                runId,
                role = "user",
                content = message
            }));

            Broadcast(JsonSerializer.Serialize(new { type = "run_started", runId }));

            // Fire-and-forget: invoke agent via RPC with run_id metadata to enable interruptible runs.
            _ = Task.Run(async () =>
            {
                try
                {
                    var req = new RpcRequest
                    {
                        MethodName = nameof(IInterruptibleChatAgent.ChatSlowAsync),
                        CorrelationId = Guid.NewGuid().ToString("N")
                    };
                    req.Metadata[RunContextScope.MetadataKeys.RunId] = runId;
                    req.Metadata[RunContextScope.MetadataKeys.RunScopeId] = SessionId;
                    req.Args.Add(ProtobufPacker.Pack(message));

                    var bytes = await _actor.InvokeRpcAsync(req.ToByteArray());
                    var resp = RpcResponse.Parser.ParseFrom(bytes);

                    if (!resp.Success)
                    {
                        // Cancellation is expected when interrupted.
                        Broadcast(JsonSerializer.Serialize(new
                        {
                            type = "run_finished",
                            runId,
                            ok = false,
                            canceled = string.Equals(resp.Error?.ErrorType, typeof(TaskCanceledException).FullName, StringComparison.Ordinal),
                            error = resp.Error?.Message ?? "rpc failed"
                        }));
                        return;
                    }

                    Broadcast(JsonSerializer.Serialize(new { type = "run_finished", runId, ok = true }));
                }
                catch (OperationCanceledException)
                {
                    Broadcast(JsonSerializer.Serialize(new { type = "run_finished", runId, ok = false, canceled = true }));
                }
                catch (Exception ex)
                {
                    Broadcast(JsonSerializer.Serialize(new { type = "run_finished", runId, ok = false, error = ex.Message }));
                }
            });

            return runId;
        }

        private void EnsureInitializedOrNotify(
            LLMProvidersConfig llmProviders,
            DemoOptions options)
        {
            lock (_gate)
            {
                if (_initialized) return;
                _initialized = true;
            }

            var providerName = (llmProviders.Default ?? string.Empty).Trim();
            if (providerName.Length == 0)
            {
                Broadcast(JsonSerializer.Serialize(new
                {
                    type = "system",
                    message = "LLMProviders.default 未配置。请在 appsettings.json 里设置默认 provider。"
                }));
                return;
            }

            try
            {
                // Initialize agent with default provider from config + demo-level prompt/params.
                _agent.SystemPrompt = options.SystemPrompt;
                _agent.InitializeAsync(
                        providerName,
                        config =>
                        {
                            config.Temperature = options.Temperature;
                            config.MaxOutputTokens = options.MaxOutputTokens;
                        })
                    .GetAwaiter()
                    .GetResult();

                Broadcast(JsonSerializer.Serialize(new
                {
                    type = "system",
                    message = $"LLM 已启用：provider='{providerName}'（发送新消息可打断旧消息输出）"
                }));
            }
            catch (Exception ex)
            {
                // Most common: missing ApiKey in LLMProviders:Providers:*:ApiKey
                Broadcast(JsonSerializer.Serialize(new
                {
                    type = "system",
                    message =
                        "LLM 初始化失败（通常是缺少 APIKey）。\n" +
                        $"provider='{providerName}'\n" +
                        $"error='{ex.Message}'\n\n" +
                        "配置方式：\n" +
                        "- ~/.aevatar/secrets.json（推荐，AddAevatarUserSecrets）\n" +
                        "- 或 examples/InterruptibleChatWebDemo/appsettings.secrets.json\n" +
                        $"需要的 key：LLMProviders:Providers:{providerName}:ApiKey"
                }));
            }
        }

        private void Broadcast(string jsonLine)
        {
            List<ChannelWriter<string>> snapshot;
            lock (_gate)
            {
                snapshot = _subscribers.ToList();
            }

            if (snapshot.Count == 0)
                return;

            var dead = new List<ChannelWriter<string>>();
            foreach (var w in snapshot)
            {
                if (!w.TryWrite(jsonLine))
                    dead.Add(w);
            }

            if (dead.Count > 0)
            {
                lock (_gate)
                {
                    foreach (var d in dead)
                        _subscribers.Remove(d);
                }
            }
        }
    }
}


