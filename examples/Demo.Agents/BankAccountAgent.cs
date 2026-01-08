using Aevatar.Agents;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.EventSourcing;
using Demo.Agents;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Demo.Agents;

/// <summary>
/// Bank account agent - supports Event Sourcing
/// </summary>
public class BankAccountAgent : GAgentBase<BankAccountState>
{
    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        
        // If State has not been initialized yet (no events to replay), set initial values
        if (string.IsNullOrEmpty(State.AccountId))
        {
            State.AccountId = Id.ToString();
            State.LastTransaction = DateTimeOffset.UtcNow.ToTimestamp();
        }
    }
    
    [EventHandler]
    public async Task HandleDeposit(DepositEvent deposit)
    {
        Logger?.LogInformation("BankAccount {Id} processing deposit of {Amount}", Id, deposit.Amount);
        
        // 创建状态变更事件
        var stateChange = new BankAccountStateChange
        {
            EventType = "Deposit",
            Amount = deposit.Amount,
            Description = deposit.Description
        };
        
        // ✅ API: Raise and Confirm
        RaiseEvent(stateChange);
        await ConfirmEventsAsync();
    }
    
    [EventHandler]
    public async Task HandleWithdraw(WithdrawEvent withdraw)
    {
        if (State.Balance >= withdraw.Amount)
        {
            Logger?.LogInformation("BankAccount {Id} processing withdrawal of {Amount}", Id, withdraw.Amount);
            
        // Create state change event
            var stateChange = new BankAccountStateChange
            {
                EventType = "Withdraw",
                Amount = withdraw.Amount,
                Description = withdraw.Description
            };
            
            // ✅ API: Raise and Confirm
            RaiseEvent(stateChange);
            await ConfirmEventsAsync();
        }
        else
        {
            Logger?.LogWarning("BankAccount {Id} insufficient balance for withdrawal of {Amount}", Id, withdraw.Amount);
        }
    }
    
    /// <summary>
    /// Pure functional state transition
    /// Framework automatically clones state, just modify it directly
    /// </summary>
    protected override void TransitionState(BankAccountState state, IMessage evt)
    {
        if (evt is BankAccountStateChange change)
        {
            switch (change.EventType)
            {
                case "Deposit":
                    state.Balance += change.Amount;
                    state.TransactionCount++;
                    state.LastTransaction = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
                    break;
                case "Withdraw":
                    state.Balance -= change.Amount;
                    state.TransactionCount++;
                    state.LastTransaction = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
                    break;
            }
        }
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"BankAccount {Id}: Balance={State.Balance:C}, Transactions={State.TransactionCount}");
    }
}