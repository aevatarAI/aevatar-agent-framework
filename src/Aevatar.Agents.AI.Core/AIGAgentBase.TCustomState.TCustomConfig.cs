using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

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
        Config.CustomConfig = Any.Pack(CustomConfig);
        await base.OnActivateAsync(ct);
    }

    protected override void ConfigAI(AevatarAIAgentConfig config)
    {
        base.ConfigAI(config);
        ConfigCustom(config.CustomConfig.Unpack<TCustomConfig>());
    }

    protected virtual void ConfigCustom(TCustomConfig customConfig)
    {

    }
}