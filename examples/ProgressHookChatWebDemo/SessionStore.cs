using System.Collections.Concurrent;
using System.Text.Json;
using Aevatar.Agents;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.Abstractions.Persistence;
using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Cognitive.Streaming;
using Aevatar.Agents.Runtime.Local;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Options;

namespace ProgressHookChatWebDemo;

public sealed class SessionStore
{
    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new(StringComparer.Ordinal);
    private readonly DemoOptions _options;
    private readonly IGAgentFactory _agentFactory;
    private readonly LLMProvidersConfig _llmProviders;
    private readonly IMemoryStore? _memoryStore;
    private readonly IStateStore<AevatarAIAgentState>? _stateStore;

    public SessionStore(
        IOptions<DemoOptions> options,
        IGAgentFactory agentFactory,
        IOptions<LLMProvidersConfig> llmProviders,
        IMemoryStore? memoryStore = null,
        IStateStore<AevatarAIAgentState>? stateStore = null)
    {
        _options = options.Value ?? new DemoOptions();
        _agentFactory = agentFactory;
        _llmProviders = llmProviders.Value ?? new LLMProvidersConfig();
        _memoryStore = memoryStore;
        _stateStore = stateStore;
    }

    public SessionEntry GetOrCreate(string sessionId)
    {
        sessionId = (sessionId ?? string.Empty).Trim();
        if (sessionId.Length == 0) throw new ArgumentException("sessionId is required", nameof(sessionId));

        return _sessions.GetOrAdd(
            sessionId,
            id => new SessionEntry(id, _options, _agentFactory, _llmProviders, _memoryStore));
    }

    public async Task<IReadOnlyList<AevatarChatMessage>> GetStateHistoryAsync(
        string sessionId,
        int limit,
        CancellationToken ct = default)
    {
        var session = GetOrCreate(sessionId);

        AevatarAIAgentState? state = null;
        if (_stateStore != null)
        {
            state = await _stateStore.LoadAsync(session.AgentId, ct);
        }

        state ??= session.GetStateSnapshot();
        if (state.History == null || state.History.Count == 0)
            return Array.Empty<AevatarChatMessage>();

        var list = state.History.Select(msg => msg.Clone()).ToList();
        if (limit > 0 && list.Count > limit)
        {
            list = list.TakeLast(limit).ToList();
        }

        return list;
    }

    public IReadOnlyList<string> ListSessions()
    {
        return _sessions.Keys
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
    }

    public Task<SessionInfoSnapshot> GetSessionInfoAsync(
        string sessionId,
        int memoryLimit,
        CancellationToken ct = default)
    {
        var session = GetOrCreate(sessionId);
        return session.GetInfoSnapshotAsync(memoryLimit, ct);
    }

    public sealed class SessionEntry
    {
        private readonly object _gate = new();
        private readonly SemaphoreSlim _runGate = new(1, 1);
        private int _seq;
        private DateTimeOffset _updatedAt;

        public string SessionId { get; }
        public string AgentId => _agent.Id;
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset UpdatedAt
        {
            get
            {
                lock (_gate)
                {
                    return _updatedAt;
                }
            }
        }

        private readonly LocalMessageStreamRegistry _registry = new();
        private readonly ProgressChatAgent _agent;
        private readonly LocalGAgentActor _actor;
        private readonly IMemoryStore? _memoryStore;
        private readonly IMessageStream _traceStream;
        private readonly AgUiTraceProjectorOptions _projectorOptions;
        private bool _initialized;

        private readonly BroadcastEventHub<AgUiEvent> _events = new(
            replayBufferSize: 0,
            subscriberBufferSize: 512,
            warningLogger: null,
            hubName: "progress-demo-agui");

        private readonly List<AgUiMessage> _messages = new();

        public SessionEntry(
            string sessionId,
            DemoOptions options,
            IGAgentFactory agentFactory,
            LLMProvidersConfig llmProviders,
            IMemoryStore? memoryStore)
        {
            SessionId = sessionId;
            CreatedAt = DateTimeOffset.UtcNow;
            _updatedAt = CreatedAt;
            _agent = agentFactory.CreateGAgent<ProgressChatAgent>(sessionId);
            _agent.EnableChatHistoryInState = true;
            _agent.EnableChatHistoryCompaction = false;
            _agent.ChatHistoryMaxMessages = Math.Max(1, options.MaxSnapshotMessages);
            _agent.EnableSessionMemoryStoreAppend = memoryStore != null;
            _memoryStore = memoryStore;

            _actor = new LocalGAgentActor(_agent, _registry);
            _actor.ActivateAsync().GetAwaiter().GetResult();

            EnsureInitializedOrNotify(llmProviders, options);
            LoadSessionMemorySnapshot(memoryStore, options.MaxSnapshotMessages);
            LoadHistorySnapshot(options.MaxSnapshotMessages);

            // ------------------------------------------------------------
            //  Progress hook stream → AG-UI
            //
            //  中文 + ASCII:
            //  - ExecutionTraceProgressHook emits ExecutionTraceEvent via PublishAsync.
            //  - LocalGAgentActor routes them into LocalMessageStream.
            //  - We subscribe, project to AG-UI, and forward to SSE.
            // ------------------------------------------------------------
            _traceStream = _registry.GetOrCreateStream(sessionId);
            _projectorOptions = new AgUiTraceProjectorOptions
            {
                ResolveThreadId = _ => SessionId
            };

            _ = _traceStream.SubscribeAsync<ExecutionTraceEvent>(
                evt =>
                {
                    var mapped = AgUiTraceProjector.Map(evt, _projectorOptions);
                    foreach (var agui in mapped)
                        _events.Publish(agui);
                    return Task.CompletedTask;
                },
                null,
                CancellationToken.None);

            _ = _traceStream.SubscribeAsync<StringValue>(
                evt =>
                {
                    HandleAssistantDelta(evt?.Value ?? string.Empty);
                    return Task.CompletedTask;
                },
                null,
                CancellationToken.None);

            _ = _traceStream.SubscribeAsync<ChatResponseEvent>(
                evt =>
                {
                    HandleChatResponse(evt);
                    return Task.CompletedTask;
                },
                null,
                CancellationToken.None);

        }

        public IReadOnlyList<AgUiMessage> GetMessagesSnapshot(int maxMessages)
        {
            lock (_gate)
            {
                if (_messages.Count <= maxMessages)
                    return _messages.ToList();
                return _messages.TakeLast(maxMessages).ToList();
            }
        }

        public IAsyncEnumerable<AgUiEvent> SubscribeAsync(CancellationToken ct)
            => _events.SubscribeAsync(replay: false, ct);

        public AevatarAIAgentState GetStateSnapshot()
            => _agent.GetState();

        public async Task<SessionInfoSnapshot> GetInfoSnapshotAsync(int memoryLimit, CancellationToken ct = default)
        {
            AgUiMessage? lastMessage;
            int messageCount;
            DateTimeOffset updatedAt;
            lock (_gate)
            {
                messageCount = _messages.Count;
                lastMessage = _messages.Count > 0 ? _messages[^1] : null;
                updatedAt = _updatedAt;
            }

            var memoryEnabled = _memoryStore != null;
            var memoryEntries = 0;
            var memoryHasMore = false;

            if (memoryEnabled)
            {
                var limit = Math.Max(1, memoryLimit);
                var service = new SessionHistoryService(_memoryStore!);
                var entries = await service.GetSessionEntriesAsync(SessionId, limit + 1, ct);
                if (entries.Count > limit)
                {
                    memoryEntries = limit;
                    memoryHasMore = true;
                }
                else
                {
                    memoryEntries = entries.Count;
                }
            }

            return new SessionInfoSnapshot(
                SessionId,
                CreatedAt,
                updatedAt,
                messageCount,
                lastMessage,
                memoryEnabled,
                memoryEntries,
                memoryHasMore);
        }

        public async Task<string> StartInputAsync(string message, CancellationToken ct = default)
        {
            message = (message ?? string.Empty).Replace("\r", "").Trim();
            if (message.Length == 0) throw new ArgumentException("message is required", nameof(message));

            var runId = $"{SessionId}:{Interlocked.Increment(ref _seq)}";
            var request = new ChatRequestEvent
            {
                RequestId = runId,
                Message = message,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
            request.Context[ChatRequest.SessionIdKey] = SessionId;

            PublishUserMessage(runId, message);
            PublishAssistantStart(runId);

            _ = Task.Run(async () =>
            {
                await _runGate.WaitAsync();
                try
                {
                    // Do not tie chat execution to the HTTP request lifetime.
                    await DispatchChatAsync(request, runId, CancellationToken.None);
                }
                finally
                {
                    _runGate.Release();
                }
            });

            return runId;
        }

        private async Task DispatchChatAsync(ChatRequestEvent request, string runId, CancellationToken ct)
        {
            var assistantMessageId = $"msg:{SessionId}:assistant:{runId}";

            try
            {
                await _actor.PublishEventAsync(request, EventDirection.Down, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                PublishAssistantEnd(assistantMessageId);
            }
            catch (Exception ex)
            {
                _events.Publish(new TextMessageContentEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    MessageId = assistantMessageId,
                    Delta = $"\n[demo] LLM error: {ex.Message}\n"
                });
                PublishAssistantEnd(assistantMessageId);
            }
        }

        private void PublishUserMessage(string runId, string message)
        {
            var messageId = $"msg:{SessionId}:user:{runId}";
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            _events.Publish(new TextMessageStartEvent
            {
                Timestamp = now,
                MessageId = messageId,
                Role = "user"
            });
            _events.Publish(new TextMessageContentEvent
            {
                Timestamp = now,
                MessageId = messageId,
                Delta = message
            });
            _events.Publish(new TextMessageEndEvent
            {
                Timestamp = now,
                MessageId = messageId
            });

            lock (_gate)
            {
                _messages.Add(new AgUiMessage
                {
                    Id = messageId,
                    Role = "user",
                    Content = message
                });
                _updatedAt = DateTimeOffset.UtcNow;
            }
        }

        private void HandleAssistantDelta(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return;

            // Format: "{runId}|assistant|delta"
            var parts = payload.Split('|', 3);
            if (parts.Length < 3)
                return;

            var runId = parts[0];
            var role = parts[1];
            var delta = parts[2];

            if (!string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase))
                return;

            var messageId = $"msg:{SessionId}:assistant:{runId}";
            _events.Publish(new TextMessageContentEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MessageId = messageId,
                Delta = delta
            });
        }

        private void HandleChatResponse(ChatResponseEvent? evt)
        {
            if (evt == null || string.IsNullOrWhiteSpace(evt.RequestId))
                return;

            var messageId = $"msg:{SessionId}:assistant:{evt.RequestId}";
            PublishAssistantEnd(messageId);

            var content = evt.Content ?? string.Empty;
            lock (_gate)
            {
                _messages.RemoveAll(m => string.Equals(m.Id, messageId, StringComparison.Ordinal));
                _messages.Add(new AgUiMessage
                {
                    Id = messageId,
                    Role = "assistant",
                    Content = content
                });

                _updatedAt = DateTimeOffset.UtcNow;
            }
        }

        private void PublishAssistantStart(string runId)
        {
            var assistantMessageId = $"msg:{SessionId}:assistant:{runId}";
            _events.Publish(new TextMessageStartEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MessageId = assistantMessageId,
                Role = "assistant"
            });
        }

        private void PublishAssistantEnd(string messageId)
        {
            _events.Publish(new TextMessageEndEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MessageId = messageId
            });
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
                PublishSystemMessage(
                    "LLMProviders.default 未配置。请先通过 Aevatar.Config 写入默认 provider。");
                return;
            }

            try
            {
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

                PublishSystemMessage($"LLM 已启用：provider='{providerName}'（progress hook 已接入）");
            }
            catch (Exception ex)
            {
                PublishSystemMessage(
                    "LLM 初始化失败（通常是缺少 APIKey）。\n" +
                    $"provider='{providerName}'\n" +
                    $"error='{ex.Message}'\n\n" +
                    "配置方式：\n" +
                    "- 运行 apps/Aevatar.Config 写入 ~/.aevatar/secrets.json\n" +
                    "- 或在示例目录创建 appsettings.secrets.json\n" +
                    $"需要的 key：LLMProviders:Providers:{providerName}:ApiKey");
            }
        }

        private void PublishSystemMessage(string message)
        {
            var messageId = $"msg:{SessionId}:system:init";
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            _events.Publish(new TextMessageStartEvent
            {
                Timestamp = now,
                MessageId = messageId,
                Role = "system"
            });
            _events.Publish(new TextMessageContentEvent
            {
                Timestamp = now,
                MessageId = messageId,
                Delta = message
            });
            _events.Publish(new TextMessageEndEvent
            {
                Timestamp = now,
                MessageId = messageId
            });

            lock (_gate)
            {
                _messages.Add(new AgUiMessage
                {
                    Id = messageId,
                    Role = "system",
                    Content = message
                });
                _updatedAt = DateTimeOffset.UtcNow;
            }
        }

        private void LoadSessionMemorySnapshot(IMemoryStore? memoryStore, int maxMessages)
        {
            if (memoryStore == null)
                return;

            try
            {
                var service = new SessionHistoryService(memoryStore);
                var entries = service.GetSessionEntriesAsync(SessionId, maxMessages)
                    .GetAwaiter()
                    .GetResult();

                if (entries.Count == 0)
                    return;

                var list = entries.Select((entry, idx) => new AgUiMessage
                    {
                        Id = string.IsNullOrWhiteSpace(entry.EntryId)
                            ? $"memory:{SessionId}:{idx}"
                            : entry.EntryId,
                        Role = string.IsNullOrWhiteSpace(entry.Role) ? "assistant" : entry.Role,
                        Content = entry.Content ?? string.Empty
                    })
                    .ToList();

                lock (_gate)
                {
                    if (_messages.Count == 0)
                        _messages.AddRange(list);
                }
            }
            catch
            {
                // best-effort only
            }
        }

        private void LoadHistorySnapshot(int maxMessages)
        {
            try
            {
                var state = _agent.GetState();
                if (state.History == null || state.History.Count == 0)
                    return;

                var list = state.History
                    .Select((msg, idx) => new AgUiMessage
                    {
                        Id = string.IsNullOrWhiteSpace(msg.Id)
                            ? $"history:{SessionId}:{idx}"
                            : msg.Id,
                        Role = msg.Role.ToString().ToLowerInvariant(),
                        Content = msg.Content ?? string.Empty
                    })
                    .ToList();

                if (maxMessages > 0 && list.Count > maxMessages)
                {
                    list = list.TakeLast(maxMessages).ToList();
                }

                lock (_gate)
                {
                    if (_messages.Count == 0)
                        _messages.AddRange(list);
                }
            }
            catch
            {
                // best-effort only
            }
        }
    }

    public sealed record SessionInfoSnapshot(
        string SessionId,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        int MessageCount,
        AgUiMessage? LastMessage,
        bool MemoryEnabled,
        int MemoryEntries,
        bool MemoryHasMore);
}
