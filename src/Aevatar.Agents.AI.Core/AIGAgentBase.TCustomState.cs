using System.Diagnostics.Tracing;
using Google.Protobuf;

namespace Aevatar.Agents.AI.Core;

// ReSharper disable InconsistentNaming
/// <summary>
/// Level 1: Basic AI Agent with chat capabilities using state-based conversation management.
/// Uses AIAgentConfig as the default configuration type.
/// </summary>
/// <typeparam name="TCustomState">The business state type (defined by the developer using protobuf)</typeparam>
public abstract class AIGAgentBase<TCustomState> : AIGAgentBase
    where TCustomState : class, IMessage<TCustomState>, new()
{
    private readonly StatePropertyAccessor<TCustomState> _customStateAccessor;

    protected TCustomState CustomState
    {
        get => _customStateAccessor.GetValue(State.CustomState);
        set
        {
            if (GetCurrentVersion() > 0)
            {
                throw new InvalidOperationException(
                    "Direct CustomState modification is not allowed when Event Sourcing is active (Version > 0). " +
                    "Use RaiseEvent to modify state.");
            }

            State.CustomState = _customStateAccessor.SetValue(value, "Direct State assignment");
        }
    }

    public TCustomState GetCustomState() => _customStateAccessor.GetValue(State.CustomState, false);

    public AIGAgentBase()
    {
        _customStateAccessor = new StatePropertyAccessor<TCustomState>();
    }

    public AIGAgentBase(string id) : base(id)
    {
        _customStateAccessor = new StatePropertyAccessor<TCustomState>();
    }

    protected override void TransitionState(AevatarAIAgentState state, IMessage evt)
    {
        base.TransitionState(state, evt);
        var customState = state.CustomState == null ? new TCustomState() : state.CustomState.Unpack<TCustomState>();
        TransitionState(customState, evt);
    }

    protected virtual void TransitionState(TCustomState state, IMessage evt)
    {

    }
}