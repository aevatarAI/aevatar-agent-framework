namespace Aevatar.Trade.Infrastructure.WeexApi;

// ============================================================================
//  WEEX API 配置
//  - 通过 appsettings.json / appsettings.secrets.json 绑定
//  - 通过 Weex:Mode 决定走 Spot 还是 AI Wars Contract
// ============================================================================

public enum WeexApiMode
{
    Contract = 0,
    Spot = 1
}

/// <summary>
/// WEEX API configuration
/// </summary>
public class WeexApiConfig
{
    /// <summary>
    /// API 模式（默认 Contract）
    /// </summary>
    public WeexApiMode Mode { get; set; } = WeexApiMode.Contract;

    /// <summary>
    /// 默认 BaseUrl（Contract 默认：api-contract.weex.com；Spot 可切到 api-spot.weex.com）
    /// </summary>
    public string BaseUrl { get; set; } = "https://api-contract.weex.com";

    // --------------------------------------------------------------------
    //  可选：把 market / trading 拆到不同域名
    //  - MarketDataBaseUrl：行情域名
    //  - TradingBaseUrl：交易/账户域名
    // --------------------------------------------------------------------
    public string MarketDataBaseUrl { get; set; } = "";
    public string TradingBaseUrl { get; set; } = "";

    // --------------------------------------------------------------------
    //  WebSocket
    // --------------------------------------------------------------------
    public string PublicWebSocketUrl { get; set; } = "";
    public string WebSocketOrigin { get; set; } = "https://www.weex.com";

    // --------------------------------------------------------------------
    //  API Credentials
    // --------------------------------------------------------------------
    public string ApiKey { get; set; } = "";
    public string ApiSecret { get; set; } = "";
    public string Passphrase { get; set; } = "";
}


