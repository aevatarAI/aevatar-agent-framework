using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Tool.Abstractions;

namespace Aevatar.Agents.AI.Core;

internal sealed partial class ToolingRuntime
{
    private readonly IToolingInitHost _initHost;
    private readonly IToolingLoopHost _loopHost;
    private IAevatarToolManager? _toolManager;
    private IReadOnlyList<ToolDefinition> _registeredToolsCache = Array.Empty<ToolDefinition>();

    private IReadOnlyList<AevatarFunctionDefinition> _functionDefinitionsCache =
        Array.Empty<AevatarFunctionDefinition>();

    private readonly SemaphoreSlim _toolInitSemaphore = new(1, 1);
    private bool _toolsInitialized;

    internal ToolingRuntime(IToolingInitHost initHost, IToolingLoopHost loopHost)
    {
        _initHost = initHost ?? throw new ArgumentNullException(nameof(initHost));
        _loopHost = loopHost ?? throw new ArgumentNullException(nameof(loopHost));
    }

    internal IAevatarToolManager ToolManager
    {
        get
        {
            EnsureToolManagerInitialized();
            return _toolManager!;
        }
        set
        {
            _toolManager = value ?? throw new ArgumentNullException(nameof(value));
            _toolsInitialized = false;
        }
    }

    internal IReadOnlyList<ToolDefinition> RegisteredToolsCache => _registeredToolsCache;
    internal IReadOnlyList<AevatarFunctionDefinition> FunctionDefinitionsCache => _functionDefinitionsCache;

    internal void EnsureToolManagerInitialized()
    {
        if (_toolManager != null)
            return;

        _toolManager = _initHost.CreateToolManager();
    }

    internal async Task InitializeToolsAsync(CancellationToken cancellationToken = default)
    {
        if (_toolsInitialized)
            return;

        await _toolInitSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (_toolsInitialized)
                return;

            EnsureToolManagerInitialized();

            await _initHost.RegisterToolsAsync(cancellationToken);
            await RefreshToolCachesAsync(cancellationToken);

            _toolsInitialized = true;
        }
        finally
        {
            _toolInitSemaphore.Release();
        }
    }

    internal async Task RefreshToolCachesAsync(CancellationToken cancellationToken = default)
    {
        if (_toolManager == null)
        {
            _registeredToolsCache = Array.Empty<ToolDefinition>();
            _functionDefinitionsCache = Array.Empty<AevatarFunctionDefinition>();
            return;
        }

        _registeredToolsCache = await ToolManager.GetAvailableToolsAsync(cancellationToken) ?? [];
        _functionDefinitionsCache = await ToolManager.GenerateFunctionDefinitionsAsync(cancellationToken) ?? [];
    }

    internal async Task<IReadOnlyList<ToolDefinition>> GetRegisteredToolsAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureToolManagerInitialized();
        return await ToolManager.GetAvailableToolsAsync(cancellationToken) ?? [];
    }
}

