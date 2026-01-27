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
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.Cognitive.Streaming;
using Aevatar.Agents.Runtime.Local;
using Aevatar.Workshop.Messages;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Options;

namespace Aevatar.Workshop;

public enum WorkshopAgentMode
{
    Role,
    Workshop
}

public sealed record SessionSummary(
    string SessionId,
    string Mode,
    string? Role,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SessionSettingsSnapshot(
    bool EnableHistory,
    bool EnableCompaction,
    bool EnableMemoryStore,
    bool EnableSessionMemory,
    bool EnableVectorIndex,
    bool EnableMcp,
    bool EnableSkills,
    bool AllowDangerousTools,
    bool AllowInternalTools);

public sealed class SessionStore
{
    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new(StringComparer.Ordinal);
    private readonly WorkshopOptions _options;
    private readonly IGAgentFactory _agentFactory;
    private readonly LLMProvidersConfig _llmProviders;
    private readonly IMemoryStore? _memoryStore;
    private readonly IStateStore<AevatarAIAgentState>? _stateStore;
    private readonly RoleAgentFactory? _roleAgentFactory;

    public SessionStore(
        IOptions<WorkshopOptions> options,
        IGAgentFactory agentFactory,
        IOptions<LLMProvidersConfig> llmProviders,
        RoleAgentFactory? roleAgentFactory = null,
        IMemoryStore? memoryStore = null,
        IStateStore<AevatarAIAgentState>? stateStore = null)
    {
        _options = options.Value ?? new WorkshopOptions();
        _agentFactory = agentFactory;
        _llmProviders = llmProviders.Value ?? new LLMProvidersConfig();
        _roleAgentFactory = roleAgentFactory;
        _memoryStore = memoryStore;
        _stateStore = stateStore;
    }

    public SessionEntry CreateSession(WorkshopAgentMode mode, string? role)
    {
        var id = Guid.NewGuid().ToString("N");
        var entry = new SessionEntry(id, mode, role, _options, _agentFactory, _llmProviders, _roleAgentFactory, _memoryStore);
        _sessions[id] = entry;
        return entry;
    }

    public SessionEntry GetOrCreate(string sessionId)
    {
        sessionId = (sessionId ?? string.Empty).Trim();
        if (sessionId.Length == 0) throw new ArgumentException("sessionId is required", nameof(sessionId));

        return _sessions.GetOrAdd(
            sessionId,
            id => new SessionEntry(id, WorkshopAgentMode.Role, _options.AgentRole, _options, _agentFactory, _llmProviders, _roleAgentFactory, _memoryStore));
    }

    public IReadOnlyList<SessionSummary> ListSessionSummaries()
    {
        return _sessions.Values
            .OrderByDescending(s => s.UpdatedAt)
            .Select(s => new SessionSummary(s.SessionId, s.Mode.ToString().ToLowerInvariant(), s.Role, s.CreatedAt, s.UpdatedAt))
            .ToList();
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
        public AIGAgentBase Agent => _agent;
        public WorkshopAgentMode Mode { get; }
        public string? Role { get; }
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

        private readonly WorkshopOptions _options;
        private readonly LocalMessageStreamRegistry _registry = new();
        private readonly AIGAgentBase _agent;
        private readonly LocalGAgentActor _actor;
        private readonly IMemoryStore? _memoryStore;
        private readonly IMessageStream _traceStream;
        private readonly AgUiTraceProjectorOptions _projectorOptions;
        private readonly RoleAgentFactory? _roleAgentFactory;
        private bool _initialized;
        private readonly Task _warmupTask;

        private readonly BroadcastEventHub<AgUiEvent> _events = new(
            replayBufferSize: 0,
            subscriberBufferSize: 4096,
            warningLogger: null,
            hubName: "workshop-agui");

        private readonly List<AgUiMessage> _messages = new();
        private readonly HashSet<string> _streamingMessages = new(StringComparer.Ordinal);

        public SessionEntry(
            string sessionId,
            WorkshopAgentMode mode,
            string? role,
            WorkshopOptions options,
            IGAgentFactory agentFactory,
            LLMProvidersConfig llmProviders,
            RoleAgentFactory? roleAgentFactory,
            IMemoryStore? memoryStore)
        {
            _options = options ?? new WorkshopOptions();
            SessionId = sessionId;
            Mode = mode;
            Role = mode == WorkshopAgentMode.Role
                ? (string.IsNullOrWhiteSpace(role) ? _options.AgentRole : role)
                : null;

            CreatedAt = DateTimeOffset.UtcNow;
            _updatedAt = CreatedAt;
            _agent = mode == WorkshopAgentMode.Workshop
                ? agentFactory.CreateGAgent<WorkshopAIGAgent>(sessionId)
                : agentFactory.CreateGAgent<RoleAIGAgent>(sessionId);

            _roleAgentFactory = roleAgentFactory;
            _agent.EnableChatHistoryInState = true;
            _agent.EnableChatHistoryCompaction = false;
            _agent.ChatHistoryMaxMessages = Math.Max(1, _options.MaxSnapshotMessages);
            _agent.EnableMemoryStoreAppend = memoryStore != null;
            _agent.EnableSessionMemoryStoreAppend = memoryStore != null;
            _memoryStore = memoryStore;

            _actor = new LocalGAgentActor(_agent, _registry);
            _actor.ActivateAsync().GetAwaiter().GetResult();

            _warmupTask = Task.Run(() =>
            {
                EnsureInitializedOrNotify(llmProviders, _options);
                LoadSessionMemorySnapshot(_memoryStore, _options.MaxSnapshotMessages);
                LoadHistorySnapshot(_options.MaxSnapshotMessages);
            });

            // ------------------------------------------------------------
            //  Progress hook stream → AG-UI
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

            _ = _traceStream.SubscribeAsync<ChatStreamChunkEvent>(
                evt =>
                {
                    HandleAssistantChunk(evt);
                    return Task.CompletedTask;
                },
                null,
                CancellationToken.None);

            _ = _traceStream.SubscribeAsync<ChatResponseEvent>(
                evt => HandleChatResponseAsync(evt),
                null,
                CancellationToken.None);

            _ = _traceStream.SubscribeAsync<WorkshopPongEvent>(
                evt =>
                {
                    if (evt != null)
                    {
                        _events.Publish(new CustomEvent
                        {
                            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            Name = "WORKSHOP_PONG",
                            Value = new
                            {
                                requestId = evt.RequestId,
                                content = evt.Content,
                                agentId = AgentId
                            }
                        });
                    }
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

        public IReadOnlyList<object> GetEventModulesSnapshot()
        {
            if (_agent is not RoleAIGAgent roleAgent)
                return Array.Empty<object>();

            return roleAgent.GetEventModules()
                .Select(m => new { name = m.Name, priority = m.Priority })
                .OrderBy(m => m.name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public SessionSettingsSnapshot GetSettingsSnapshot()
        {
            return new SessionSettingsSnapshot(
                EnableHistory: _agent.EnableChatHistoryInState,
                EnableCompaction: _agent.EnableChatHistoryCompaction,
                EnableMemoryStore: _agent.EnableMemoryStoreAppend,
                EnableSessionMemory: _agent.EnableSessionMemoryStoreAppend,
                EnableVectorIndex: _agent.EnableMemoryVectorIndexAppend,
                EnableMcp: _agent.EnableMcpServers,
                EnableSkills: _agent.EnableAgentSkills,
                AllowDangerousTools: _agent.AllowDangerousTools,
                AllowInternalTools: _agent.AllowInternalTools);
        }

        public void ApplySettings(AgentSettingsInput input)
        {
            if (input.EnableHistory.HasValue)
                _agent.EnableChatHistoryInState = input.EnableHistory.Value;
            if (input.EnableCompaction.HasValue)
                _agent.EnableChatHistoryCompaction = input.EnableCompaction.Value;
            if (input.EnableMemoryStore.HasValue)
                _agent.EnableMemoryStoreAppend = input.EnableMemoryStore.Value && _memoryStore != null;
            if (input.EnableSessionMemory.HasValue)
                _agent.EnableSessionMemoryStoreAppend = input.EnableSessionMemory.Value && _memoryStore != null;
            if (input.EnableVectorIndex.HasValue)
                _agent.EnableMemoryVectorIndexAppend = input.EnableVectorIndex.Value;
            if (input.EnableMcp.HasValue)
                _agent.EnableMcpServers = input.EnableMcp.Value;
            if (input.EnableSkills.HasValue)
                _agent.EnableAgentSkills = input.EnableSkills.Value;
            if (input.AllowDangerousTools.HasValue)
                _agent.AllowDangerousTools = input.AllowDangerousTools.Value;
            if (input.AllowInternalTools.HasValue)
                _agent.AllowInternalTools = input.AllowInternalTools.Value;
        }

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

            if (_memoryStore != null && memoryLimit > 0)
            {
                try
                {
                    var service = new SessionHistoryService(_memoryStore);
                    var entries = await service.GetSessionEntriesAsync(SessionId, memoryLimit + 1, ct);
                    if (entries.Count > memoryLimit)
                    {
                        memoryEntries = memoryLimit;
                        memoryHasMore = true;
                    }
                    else
                    {
                        memoryEntries = entries.Count;
                    }
                }
                catch
                {
                    // Best-effort only
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

        public async Task<string> StartInputAsync(ChatRequestEvent request, CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var message = (request.Message ?? string.Empty).Replace("\r", "").Trim();
            if (message.Length == 0) throw new ArgumentException("message is required", nameof(request));

            var requestId = (request.RequestId ?? string.Empty).Trim();
            if (requestId.Length == 0)
            {
                requestId = $"{SessionId}:{Interlocked.Increment(ref _seq)}";
            }
            else
            {
                Interlocked.Increment(ref _seq);
            }

            request.RequestId = requestId;
            request.Message = message;
            request.Timestamp ??= Timestamp.FromDateTime(DateTime.UtcNow);
            request.StreamChunkEveryN = NormalizeStreamChunkEveryN(
                request.StreamChunkEveryN > 0 ? request.StreamChunkEveryN : null);
            request.Context[ChatRequest.SessionIdKey] = SessionId;

            _events.Publish(new RunStartedEvent { Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ThreadId = SessionId, RunId = requestId });
            PublishUserMessage(requestId, message);
            PublishAssistantStart(requestId);

            _ = Task.Run(async () =>
            {
                await _runGate.WaitAsync();
                try
                {
                    await _warmupTask;
                    await DispatchChatAsync(request, requestId, CancellationToken.None);
                }
                finally
                {
                    _runGate.Release();
                }
            });

            return requestId;
        }

        private int NormalizeStreamChunkEveryN(int? streamChunkEveryN)
        {
            var fallback = _options.StreamChunkEveryN > 0 ? _options.StreamChunkEveryN : 1;
            var value = streamChunkEveryN.GetValueOrDefault(0);
            if (value <= 0)
                value = fallback;
            return Math.Clamp(value, 1, 64);
        }

        public async Task<string> SendPingAsync(string content, CancellationToken ct = default)
        {
            var requestId = $"{SessionId}:ping:{Interlocked.Increment(ref _seq)}";
            var evt = new WorkshopPingEvent
            {
                RequestId = requestId,
                Content = content ?? string.Empty,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            await _actor.PublishEventAsync(evt, Aevatar.Agents.EventDirection.Down, ct);
            return requestId;
        }

        private async Task DispatchChatAsync(ChatRequestEvent request, string runId, CancellationToken ct)
        {
            var assistantMessageId = $"msg:{SessionId}:assistant:{runId}";

            try
            {
                await _actor.PublishEventAsync(request, Aevatar.Agents.EventDirection.Down, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                PublishAssistantEnd(assistantMessageId);
                _events.Publish(new RunErrorEvent { Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Message = "request cancelled" });
            }
            catch (Exception ex)
            {
                _events.Publish(new TextMessageContentEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    MessageId = assistantMessageId,
                    Delta = $"\n[workshop] LLM error: {ex.Message}\n"
                });
                PublishAssistantEnd(assistantMessageId);
                _events.Publish(new RunErrorEvent { Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Message = string.IsNullOrWhiteSpace(ex.Message) ? "run failed" : ex.Message.Trim() });
            }
        }

        private void PublishUserMessage(string runId, string message)
        {
            var messageId = $"msg:{SessionId}:user:{runId}";
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            _events.Publish(new TextMessageStartEvent { Timestamp = now, MessageId = messageId, Role = "user" });
            _events.Publish(new TextMessageContentEvent { Timestamp = now, MessageId = messageId, Delta = message });
            _events.Publish(new TextMessageEndEvent { Timestamp = now, MessageId = messageId });

            lock (_gate)
            {
                _messages.Add(new AgUiMessage { Id = messageId, Role = "user", Content = message });
                _updatedAt = DateTimeOffset.UtcNow;
            }
        }

        private void HandleAssistantChunk(ChatStreamChunkEvent? evt)
        {
            if (evt == null || string.IsNullOrWhiteSpace(evt.RequestId))
                return;

            var messageId = $"msg:{SessionId}:assistant:{evt.RequestId}";
            lock (_gate)
            {
                _streamingMessages.Add(messageId);
            }
            _events.Publish(new TextMessageContentEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MessageId = messageId,
                Delta = evt.Content ?? string.Empty
            });
        }

        private async Task HandleChatResponseAsync(ChatResponseEvent? evt)
        {
            if (evt == null || string.IsNullOrWhiteSpace(evt.RequestId))
                return;

            var messageId = $"msg:{SessionId}:assistant:{evt.RequestId}";
            var content = evt.Content ?? string.Empty;
            bool hadChunks;
            lock (_gate)
            {
                hadChunks = _streamingMessages.Remove(messageId);
            }

            if (!hadChunks && content.Length > 0)
            {
                await EmitAssistantFallbackStreamAsync(messageId, content);
            }
            else if (content.Length > 0)
            {
                _events.Publish(new TextMessageContentEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    MessageId = messageId,
                    Delta = content
                });
            }

            PublishAssistantEnd(messageId);
            _events.Publish(new RunFinishedEvent { Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ThreadId = SessionId, RunId = evt.RequestId, Result = new { length = content.Length } });

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

        private async Task EmitAssistantFallbackStreamAsync(string messageId, string content)
        {
            if (string.IsNullOrWhiteSpace(messageId) || string.IsNullOrEmpty(content))
                return;

            // 中文 + ASCII:
            // - 如果底层 LLM 未提供 stream chunk，就用小块模拟 streaming。
            // - 仅用于 UI 体验，不改变最终内容。
            const int chunkSize = 48;
            const int delayMs = 12;

            for (var i = 0; i < content.Length; i += chunkSize)
            {
                var take = Math.Min(chunkSize, content.Length - i);
                var delta = content.Substring(i, take);
                _events.Publish(new TextMessageContentEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    MessageId = messageId,
                    Delta = delta
                });

                if (delayMs > 0)
                    await Task.Delay(delayMs);
            }
        }

        private void PublishAssistantStart(string runId)
        {
            var assistantMessageId = $"msg:{SessionId}:assistant:{runId}";
            _events.Publish(new TextMessageStartEvent { Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), MessageId = assistantMessageId, Role = "assistant" });
        }

        private void PublishAssistantEnd(string messageId)
        {
            _events.Publish(new TextMessageEndEvent { Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), MessageId = messageId });
        }

        private void EnsureInitializedOrNotify(
            LLMProvidersConfig llmProviders,
            WorkshopOptions options)
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
                if (_agent is RoleAIGAgent roleAgent)
                {
                    var role = (Role ?? string.Empty).Trim();
                    if (options.EnableAgentYaml && role.Length > 0)
                    {
                        roleAgent.InitializeRole(role);
                        _roleAgentFactory?.ApplyYamlAsync(roleAgent, role).GetAwaiter().GetResult();
                    }
                }

                if (string.IsNullOrWhiteSpace(_agent.SystemPrompt))
                {
                    _agent.SystemPrompt = options.SystemPrompt;
                }

                Action<AevatarAIAgentConfig>? yamlConfig = null;
                if (_agent is RoleAIGAgent && options.EnableAgentYaml && !string.IsNullOrWhiteSpace(Role))
                {
                    yamlConfig = _roleAgentFactory?.BuildYamlConfigAction(Role);
                }

                _agent.InitializeAsync(
                        providerName,
                        config =>
                        {
                            config.Temperature = options.Temperature;
                            config.MaxOutputTokens = options.MaxOutputTokens;
                            yamlConfig?.Invoke(config);
                        })
                    .GetAwaiter()
                    .GetResult();

                var moduleInfo = _agent is RoleAIGAgent moduleAgent
                    ? moduleAgent.GetEventModules()
                        .Select(m => m.Name)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                    : new List<string>();

                var moduleText = moduleInfo.Count == 0
                    ? "no agent.yaml modules"
                    : $"agent.yaml modules: [{string.Join(", ", moduleInfo)}]";

                PublishSystemMessage(
                    $"LLM 已启用：provider='{providerName}'（mode={Mode.ToString().ToLowerInvariant()}; {moduleText}）");
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
