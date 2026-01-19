namespace Aevatar.Trade.Infrastructure.Exchanges;

// ============================================================================
//  能力模型 / Exchange Capabilities
//  - 用于描述不同交易所/模式的可用能力
// ============================================================================

public sealed record ExchangeCapabilities
{
    public bool SupportsWebSocket { get; init; }
    public bool SupportsPositions { get; init; }
    public bool SupportsFundingRate { get; init; }
    public bool SupportsOpenInterest { get; init; }
    public bool SupportsFills { get; init; }
    public bool SupportsBalances { get; init; }
    public bool SupportsOrders { get; init; }
    public bool SupportsKlines { get; init; }

    public static ExchangeCapabilities Minimal => new()
    {
        SupportsBalances = true,
        SupportsOrders = true,
        SupportsKlines = true
    };
}
