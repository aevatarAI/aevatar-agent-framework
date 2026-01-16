using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Aevatar.Agents.Core.StateProtection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

public abstract class AIGAgentBase<TCustomState, TCustomConfig> : AIGAgentBase<TCustomState>
    where TCustomState : class, IMessage<TCustomState>, new()
    where TCustomConfig : class, IMessage<TCustomConfig>, new()
{
    private readonly StatePropertyAccessor<TCustomConfig> _customConfigAccessor;

    protected TCustomConfig CustomConfig
    {
        get => _customConfigAccessor.GetValue(Config.CustomConfig);
        set => Config.CustomConfig = _customConfigAccessor.SetValue(value, "Direct Config assignment");
    }

    public TCustomConfig GetCustomConfig() => _customConfigAccessor.GetValue(Config.CustomConfig, false);

    public AIGAgentBase()
    {
        _customConfigAccessor = new StatePropertyAccessor<TCustomConfig>();
    }

    public AIGAgentBase(string id) : base(id)
    {
        _customConfigAccessor = new StatePropertyAccessor<TCustomConfig>();
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        // IMPORTANT: follow repo convention - base first.
        // We still must ensure CustomConfig is a typed Any before other flows try to Unpack it.
        using var initScope = StateProtectionContext.BeginInitializationScope();

        await base.OnActivateAsync(ct);

        // Persist any CustomConfig mutations performed by derived agents in their OnActivateAsync,
        // while also ensuring a typed Any exists for future Unpack<TCustomConfig>() calls.
        Config.CustomConfig = Any.Pack(CustomConfig);

        // base OnActivateAsync already saves Config once; we changed it, so persist again (best-effort).
        if (ConfigStore != null)
        {
            await ConfigStore.SaveAsync(GetType(), Id, Config, ct);
        }
    }

    protected override void ConfigAI(AevatarAIAgentConfig config)
    {
        base.ConfigAI(config);

        // Ensure typed Any exists and persist any modifications made by ConfigCustom().
        var custom = TryUnpackCustomConfig(config.CustomConfig) ?? new TCustomConfig();
        ConfigCustom(custom);
        config.CustomConfig = Any.Pack(custom);
    }

    protected virtual void ConfigCustom(TCustomConfig customConfig)
    {

    }

    private TCustomConfig? TryUnpackCustomConfig(Any any)
    {
        try
        {
            return any.Unpack<TCustomConfig>();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex,
                "Invalid CustomConfig Any payload for agent {AgentId} ({AgentType}); resetting to default {ConfigType} (best-effort).",
                Id, GetType().Name, typeof(TCustomConfig).Name);
            return null;
        }
    }
}