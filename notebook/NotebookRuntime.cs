using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.Runtime.Local;
using Microsoft.Extensions.Options;
using Aevatar.Notebook.Agents;

namespace Aevatar.Notebook;

// ============================================================
//  NotebookRuntime
//
//  Purpose:
//  - Manage a single NotebookAgent instance for MVP.
//  - Provide a stable agentId for UI sessions (resettable).
// ============================================================
public sealed class NotebookRuntime
{
    private readonly IGAgentActorFactory _actorFactory;
    private readonly ILogger<NotebookRuntime> _logger;
    private readonly IOptions<LLMProvidersConfig> _llm;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private IGAgentActor? _actor;
    private NotebookAgent? _agent;
    private string _agentId = $"notebook-{Guid.NewGuid():N}";
    private bool _isReady;
    private string? _lastError;

    public NotebookRuntime(
        IGAgentActorFactory actorFactory,
        ILogger<NotebookRuntime> logger,
        IOptions<LLMProvidersConfig> llm)
    {
        _actorFactory = actorFactory;
        _logger = logger;
        _llm = llm;
    }

    public async Task<(NotebookAgent Agent, string AgentId)> GetAgentAsync(CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);
        if (_agent == null || _actor == null)
            throw new InvalidOperationException(_lastError ?? "agent not initialized");
        return (_agent, _agentId);
    }

    public async Task<NotebookStatus> GetStatusAsync(CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);
        return new NotebookStatus
        {
            AgentId = _agentId,
            IsReady = _isReady,
            LastError = _lastError
        };
    }

    public async Task<NotebookStatus> ResetAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _actor = null;
            _agent = null;
            _isReady = false;
            _lastError = null;
            _agentId = $"notebook-{Guid.NewGuid():N}";
        }
        finally
        {
            _lock.Release();
        }

        return await GetStatusAsync(ct);
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_isReady)
            return;

        await _lock.WaitAsync(ct);
        try
        {
            if (_isReady)
                return;

            _lastError = null;

            _logger.LogInformation("[Notebook] Creating agent actor: {AgentId}", _agentId);
            _actor = await _actorFactory.CreateGAgentActorAsync<NotebookAgent>(_agentId);
            _agent = (NotebookAgent)_actor.GetAgent();

            // Initialize LLM provider (use config default if present).
            var providerName = string.IsNullOrWhiteSpace(_llm.Value.Default) ? "default" : _llm.Value.Default;
            _logger.LogInformation("[Notebook] Initializing LLM provider: {Provider}", providerName);

            await _agent.InitializeAsync(
                providerName,
                cfg =>
                {
                    cfg.Temperature = 0.2f;
                    cfg.MaxOutputTokens = 1200;
                },
                ct);

            _logger.LogInformation("[Notebook] Ready.");
            _isReady = true;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _logger.LogError(ex, "[Notebook] Initialization failed: {Message}", ex.Message);
            _isReady = false;
        }
        finally
        {
            _lock.Release();
        }
    }
}

public sealed class NotebookStatus
{
    public required string AgentId { get; init; }
    public required bool IsReady { get; init; }
    public string? LastError { get; init; }
}


