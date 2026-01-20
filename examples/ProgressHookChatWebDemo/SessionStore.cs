using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions.Configuration;
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
        private readonly SemaphoreSlim _runGate = new(1, 1);
        private int _seq;

        public string SessionId { get; }

        private readonly LocalMessageStreamRegistry _registry = new();
        private readonly ProgressChatAgent _agent;
        private readonly LocalGAgentActor _actor;
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
            LLMProvidersConfig llmProviders)
        {
            SessionId = sessionId;
            _agent = agentFactory.CreateGAgent<ProgressChatAgent>(sessionId);

            _actor = new LocalGAgentActor(_agent, _registry);
            _actor.ActivateAsync().GetAwaiter().GetResult();

            EnsureInitializedOrNotify(llmProviders, options);

            // ------------------------------------------------------------
            //  Progress hook stream → AG-UI
            //
            //  中文 + ASCII:
            //  - ExecutionTraceProgressHook emits ExecutionTraceEvent via PublishAsync.
            //  - LocalGAgentActor routes them into LocalMessageStream.
            //  - We subscribe, project to AG-UI, and forward to SSE.
            // ------------------------------------------------------------
            var stream = _registry.GetOrCreateStream(sessionId);
            var projectorOptions = new AgUiTraceProjectorOptions
            {
                ResolveThreadId = _ => SessionId
            };

            _ = stream.SubscribeAsync<ExecutionTraceEvent>(
                evt =>
                {
                    var mapped = AgUiTraceProjector.Map(evt, projectorOptions);
                    foreach (var agui in mapped)
                        _events.Publish(agui);
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

        public async Task<string> StartInputAsync(string message, CancellationToken ct = default)
        {
            message = (message ?? string.Empty).Replace("\r", "").Trim();
            if (message.Length == 0) throw new ArgumentException("message is required", nameof(message));

            var runId = $"{SessionId}:{Interlocked.Increment(ref _seq)}";
            var request = new ChatRequest
            {
                RequestId = runId,
                Message = message
            };

            PublishUserMessage(runId, message);

            _ = Task.Run(async () =>
            {
                await _runGate.WaitAsync();
                try
                {
                    // Do not tie chat execution to the HTTP request lifetime.
                    await RunChatAsync(request, runId, CancellationToken.None);
                }
                finally
                {
                    _runGate.Release();
                }
            });

            return runId;
        }

        private async Task RunChatAsync(ChatRequest request, string runId, CancellationToken ct)
        {
            var startedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var assistantMessageId = $"msg:{SessionId}:assistant:{runId}";

            _events.Publish(new TextMessageStartEvent
            {
                Timestamp = startedAt,
                MessageId = assistantMessageId,
                Role = "assistant"
            });

            var buffer = new StringBuilder();
            try
            {
                await foreach (var chunk in _agent.ChatStreamAsync(request, ct))
                {
                    if (string.IsNullOrEmpty(chunk))
                        continue;

                    buffer.Append(chunk);
                    _events.Publish(new TextMessageContentEvent
                    {
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        MessageId = assistantMessageId,
                        Delta = chunk
                    });
                }

                _events.Publish(new TextMessageEndEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    MessageId = assistantMessageId
                });

                lock (_gate)
                {
                    _messages.Add(new AgUiMessage
                    {
                        Id = assistantMessageId,
                        Role = "assistant",
                        Content = buffer.ToString()
                    });
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _events.Publish(new TextMessageEndEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    MessageId = assistantMessageId
                });
            }
            catch (Exception ex)
            {
                _events.Publish(new TextMessageContentEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    MessageId = assistantMessageId,
                    Delta = $"\n[demo] LLM error: {ex.Message}\n"
                });
                _events.Publish(new TextMessageEndEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    MessageId = assistantMessageId
                });
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
            }
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
            }
        }
    }
}
