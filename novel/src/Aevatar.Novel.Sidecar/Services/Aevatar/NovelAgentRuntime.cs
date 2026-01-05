using Aevatar.Agents.Abstractions;
using Aevatar.Novel.Sidecar.Agents;

namespace Aevatar.Novel.Sidecar.Services.Aevatar;

// ============================================================
//  NovelAgentRuntime (Local)
//
//  PURPOSE:
//  - Create and hold long-lived agents for the sidecar process (Local runtime).
//  - Provide stable actor ids so sessions/tests behave predictably.
//
//  NOTE:
//  - v1 uses Local runtime only. Later we can swap to Orleans/ProtoActor without changing agents.
// ============================================================

public sealed class NovelAgentRuntime
{
    private readonly IGAgentActorFactory _factory;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private IGAgentActor? _narrativeTestActor;
    private IGAgentActor? _deviationImpactActor;
    private IGAgentActor? _canonGovernanceActor;
    private IGAgentActor? _setupPayoffActor;

    public NovelAgentRuntime(IGAgentActorFactory factory)
    {
        _factory = factory;
    }

    public async Task<IGAgentActor> GetNarrativeTestActorAsync(CancellationToken ct)
    {
        if (_narrativeTestActor is not null)
            return _narrativeTestActor;

        await _lock.WaitAsync(ct);
        try
        {
            if (_narrativeTestActor is not null)
                return _narrativeTestActor;

            // Raw id will be auto-prefixed by runtime (AgentType:RawId).
            _narrativeTestActor = await _factory.CreateGAgentActorAsync<NarrativeTestAgent>("novel-narrative-tests", ct);
            await _narrativeTestActor.ActivateAsync(ct);
            return _narrativeTestActor;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IGAgentActor> GetDeviationImpactActorAsync(CancellationToken ct)
    {
        if (_deviationImpactActor is not null)
            return _deviationImpactActor;

        await _lock.WaitAsync(ct);
        try
        {
            if (_deviationImpactActor is not null)
                return _deviationImpactActor;

            // Raw id will be auto-prefixed by runtime (AgentType:RawId).
            _deviationImpactActor = await _factory.CreateGAgentActorAsync<DeviationImpactAgent>("novel-deviation-impact", ct);
            await _deviationImpactActor.ActivateAsync(ct);
            return _deviationImpactActor;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IGAgentActor> GetCanonGovernanceActorAsync(CancellationToken ct)
    {
        if (_canonGovernanceActor is not null)
            return _canonGovernanceActor;

        await _lock.WaitAsync(ct);
        try
        {
            if (_canonGovernanceActor is not null)
                return _canonGovernanceActor;

            _canonGovernanceActor = await _factory.CreateGAgentActorAsync<CanonGovernanceAgent>("novel-canon-governance", ct);
            await _canonGovernanceActor.ActivateAsync(ct);
            return _canonGovernanceActor;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IGAgentActor> GetSetupPayoffLedgerActorAsync(CancellationToken ct)
    {
        if (_setupPayoffActor is not null)
            return _setupPayoffActor;

        await _lock.WaitAsync(ct);
        try
        {
            if (_setupPayoffActor is not null)
                return _setupPayoffActor;

            _setupPayoffActor = await _factory.CreateGAgentActorAsync<SetupPayoffLedgerAgent>("novel-setup-payoff-ledger", ct);
            await _setupPayoffActor.ActivateAsync(ct);
            return _setupPayoffActor;
        }
        finally
        {
            _lock.Release();
        }
    }
}


