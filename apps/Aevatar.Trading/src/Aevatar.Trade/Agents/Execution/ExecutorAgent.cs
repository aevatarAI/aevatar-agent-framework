using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Aevatar.Trade.Infrastructure.Exchanges;
using Aevatar.Trade.Infrastructure.WeexApi;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Trade.Agents.Execution;

/// <summary>
/// Trade execution agent
/// Responsibilities: Execute actual trades and manage order lifecycle
/// </summary>
public class ExecutorAgent : GAgentBase<ExecutorState>
{
    // ============ Dependencies ============

    private IExchangeTradeClient? _tradeClient;
    private IExchangeAccountClient? _accountClient;
    
    // ============ Execution Mode ============
    //
    // Design principles:
    // - DryRun enabled by default: Run through the closed loop and observation first, then switch to Live
    // - Live requires exchange API keys and real order placement
    //
    private TradeExecutionMode _executionMode = TradeExecutionMode.DryRun;

    public IExchangeClient ExchangeClient
    {
        set
        {
            _tradeClient = value.Trade;
            _accountClient = value.Account;
        }
    }
    
    /// <summary>
    /// Configure execution mode (injected at system orchestration layer)
    /// </summary>
    public void Configure(TradeExecutionMode mode)
    {
        _executionMode = mode;
        State.ExecutionMode = mode.ToString();
        
        Logger.LogInformation("[Executor] Configured: Mode={Mode}", mode);
    }

    // ============ Lifecycle ============

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        
        State.AgentId = Id.ToString();
        State.OrdersPlaced = 0;
        State.OrdersFilled = 0;
        State.OrdersFailed = 0;
        State.OrdersSimulated = 0;
        State.ExecutionMode = _executionMode.ToString();
        
        Logger.LogInformation("[Executor] Activated: {AgentId}", State.AgentId);
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult(
            $"Executor: Mode={State.ExecutionMode}, " +
            $"Placed={State.OrdersPlaced}, " +
            $"Filled={State.OrdersFilled}, " +
            $"Failed={State.OrdersFailed}, " +
            $"Simulated={State.OrdersSimulated}, " +
            $"PnL=${State.TotalRealizedPnl:F2}");
    }

    // ============ Event Handlers ============

    /// <summary>
    /// Handle approved trades and execute order placement
    /// </summary>
    [EventHandler]
    public async Task HandleApprovedTrade(ApprovedTradeEvent evt)
    {
        var clientOrderId = GenerateClientOrderId(evt.DecisionId);

        // Live mode depends on API Client; DryRun can run safely without API keys
        if (_executionMode == TradeExecutionMode.Live && _tradeClient == null)
        {
            Logger.LogError("[Executor] Trade client not configured");
            await PublishOrderFailed(evt, clientOrderId, "TRADE_CLIENT_NOT_CONFIGURED", "Trade client not configured");
            return;
        }

        Logger.LogInformation(
            "[Executor] Executing trade: {DecisionId}, {Side} {Symbol}, Qty={Qty}",
            evt.DecisionId, evt.Side, evt.Symbol, evt.Quantity);

        await ExecuteTradeAsync(evt, clientOrderId);
    }

    /// <summary>
    /// Handle circuit breaker events and cancel all pending orders
    /// </summary>
    [EventHandler]
    public async Task HandleCircuitBreaker(CircuitBreakerTriggeredEvent evt)
    {
        Logger.LogWarning(
            "[Executor] Circuit breaker triggered: {Type}, cancelling all orders",
            evt.TriggerType);

        await CancelAllOrdersAsync();
    }

    // ============ Trade Execution ============

    private async Task ExecuteTradeAsync(ApprovedTradeEvent trade, string clientOrderId)
    {
        // ------------------------------------------------------------
        //  DryRun: Do not place real orders, only publish simulated events to ensure full-chain observability
        // ------------------------------------------------------------
        if (_executionMode == TradeExecutionMode.DryRun)
        {
            State.OrdersSimulated++;
            State.LastExecution = Timestamp.FromDateTime(DateTime.UtcNow);
            
            var simulatedOrderId = $"SIM_{clientOrderId}";
            
            await PublishAsync(new OrderSimulatedEvent
            {
                SimulatedOrderId = simulatedOrderId,
                ClientOrderId = clientOrderId,
                DecisionId = trade.DecisionId,
                Symbol = trade.Symbol,
                Side = trade.Side,
                OrderType = trade.OrderType,
                Quantity = trade.Quantity,
                Price = trade.OrderType == "limit" ? trade.Price : 0,
                StopLoss = trade.StopLoss,
                TakeProfit = trade.TakeProfit,
                Reason = "DRY_RUN",
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            });
            
            Logger.LogInformation(
                "[Executor] DRY_RUN simulated: {DecisionId}, {Side} {Symbol}, Qty={Qty}, ClientId={ClientId}",
                trade.DecisionId, trade.Side, trade.Symbol, trade.Quantity, clientOrderId);
            
            return;
        }

        try
        {
            // Build order request
            var request = new OrderRequest
            {
                Symbol = trade.Symbol,
                Side = trade.Side,
                OrderType = trade.OrderType,
                Quantity = trade.Quantity.ToString("F8"),
                Price = trade.OrderType == "limit" ? trade.Price.ToString("F8") : null,
                ClientOrderId = clientOrderId
            };

            // Execute order placement
            var result = await _tradeClient!.PlaceOrderAsync(request);
            State.OrdersPlaced++;
            State.LastExecution = Timestamp.FromDateTime(DateTime.UtcNow);

            if (result.Success)
            {
                // Record active orders
                State.ActiveOrderIds.Add(result.OrderId ?? clientOrderId);

                Logger.LogInformation(
                    "[Executor] Order placed: {OrderId}, ClientId={ClientId}",
                    result.OrderId, clientOrderId);

                // Publish execution success event
                await PublishAsync(new OrderExecutedEvent
                {
                    OrderId = result.OrderId ?? "",
                    ClientOrderId = clientOrderId,
                    DecisionId = trade.DecisionId,
                    Symbol = trade.Symbol,
                    Side = trade.Side,
                    Quantity = trade.Quantity,
                    FilledPrice = trade.Price,
                    Status = "SUBMITTED",
                    Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
                });

                // If stop loss or take profit exists, set conditional orders
                if (trade.StopLoss > 0 || trade.TakeProfit > 0)
                {
                    await SetStopOrdersAsync(trade, result.OrderId ?? clientOrderId);
                }
            }
            else
            {
                State.OrdersFailed++;
                Logger.LogError(
                    "[Executor] Order failed: {Error} ({Code})",
                    result.ErrorMessage, result.ErrorCode);

                await PublishOrderFailed(
                    trade, 
                    clientOrderId,
                    result.ErrorCode ?? "UNKNOWN", 
                    result.ErrorMessage ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            State.OrdersFailed++;
            State.LastExecution = Timestamp.FromDateTime(DateTime.UtcNow);
            Logger.LogError(ex, "[Executor] Order execution exception");
            await PublishOrderFailed(trade, clientOrderId, "EXCEPTION", ex.Message);
        }
    }

    private async Task SetStopOrdersAsync(ApprovedTradeEvent trade, string parentOrderId)
    {
        // Set stop loss order
        if (trade.StopLoss > 0)
        {
            try
            {
                var stopSide = trade.Side == "buy" ? "sell" : "buy";
                var stopRequest = new OrderRequest
                {
                    Symbol = trade.Symbol,
                    Side = stopSide,
                    OrderType = "limit",
                    Quantity = trade.Quantity.ToString("F8"),
                    Price = trade.StopLoss.ToString("F8"),
                    ClientOrderId = $"SL_{parentOrderId}"
                };

                // Note: Real exchanges may have dedicated stop loss order APIs
                Logger.LogDebug(
                    "[Executor] Stop loss set at {Price} for {OrderId}",
                    trade.StopLoss, parentOrderId);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[Executor] Failed to set stop loss");
            }
        }

        // Set take profit order
        if (trade.TakeProfit > 0)
        {
            try
            {
                var tpSide = trade.Side == "buy" ? "sell" : "buy";
                var tpRequest = new OrderRequest
                {
                    Symbol = trade.Symbol,
                    Side = tpSide,
                    OrderType = "limit",
                    Quantity = trade.Quantity.ToString("F8"),
                    Price = trade.TakeProfit.ToString("F8"),
                    ClientOrderId = $"TP_{parentOrderId}"
                };

                Logger.LogDebug(
                    "[Executor] Take profit set at {Price} for {OrderId}",
                    trade.TakeProfit, parentOrderId);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[Executor] Failed to set take profit");
            }
        }

        await Task.CompletedTask;
    }

    private async Task PublishOrderFailed(ApprovedTradeEvent trade, string clientOrderId, string code, string message)
    {
        await PublishAsync(new OrderFailedEvent
        {
            ClientOrderId = clientOrderId,
            DecisionId = trade.DecisionId,
            Symbol = trade.Symbol,
            Side = trade.Side,
            ErrorCode = code,
            ErrorMessage = message,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        });
    }

    // ============ Order Management ============

    /// <summary>
    /// Cancel a specific order
    /// </summary>
    public async Task<bool> CancelOrderAsync(string symbol, string orderId)
    {
        if (_tradeClient == null) return false;

        try
        {
            var result = await _tradeClient.CancelOrderAsync(symbol, orderId);
            
            if (result.Success)
            {
                State.OrdersCancelled++;
                State.ActiveOrderIds.Remove(orderId);

                await PublishAsync(new OrderCancelledEvent
                {
                    OrderId = orderId,
                    Symbol = symbol,
                    Reason = "Manual cancellation",
                    Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
                });

                Logger.LogInformation("[Executor] Order cancelled: {OrderId}", orderId);
                return true;
            }

            Logger.LogWarning(
                "[Executor] Failed to cancel order: {OrderId}, {Error}",
                orderId, result.ErrorMessage);
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[Executor] Cancel order exception: {OrderId}", orderId);
            return false;
        }
    }

    /// <summary>
    /// Cancel all pending orders
    /// </summary>
    public async Task CancelAllOrdersAsync()
    {
        if (_tradeClient == null) return;

        try
        {
            var openOrders = await _tradeClient.GetOpenOrdersAsync();
            
            foreach (var order in openOrders)
            {
                await CancelOrderAsync(order.Symbol, order.OrderId);
            }

            Logger.LogInformation(
                "[Executor] Cancelled {Count} orders",
                openOrders.Count);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[Executor] Cancel all orders exception");
        }
    }

    /// <summary>
    /// Sync order status
    /// </summary>
    public async Task SyncOrderStatusAsync(string symbol)
    {
        if (_tradeClient == null) return;

        try
        {
            var openOrders = await _tradeClient.GetOpenOrdersAsync(symbol);
            
            // Update active order list
            State.ActiveOrderIds.Clear();
            foreach (var order in openOrders)
            {
                State.ActiveOrderIds.Add(order.OrderId);
            }

            Logger.LogDebug(
                "[Executor] Synced {Count} open orders for {Symbol}",
                openOrders.Count, symbol);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[Executor] Sync order status exception");
        }
    }

    /// <summary>
    /// Sync position information
    /// </summary>
    public async Task SyncPositionsAsync()
    {
        if (_accountClient == null) return;

        try
        {
            var balances = await _accountClient.GetBalancesAsync();
            
            State.Positions.Clear();
            foreach (var balance in balances.Where(b => b.Balance > 0))
            {
                State.Positions[balance.Currency] = (double)balance.Balance;
            }

            Logger.LogDebug(
                "[Executor] Synced {Count} positions",
                State.Positions.Count);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[Executor] Sync positions exception");
        }
    }

    // ============ Helper Methods ============

    private static string GenerateClientOrderId(string decisionId)
        => $"{decisionId}_{DateTime.UtcNow:HHmmssfff}";
}
