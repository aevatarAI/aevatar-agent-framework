using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Trade.Infrastructure.WeexApi;

// ============================================================================
//  WEEX AI Wars Contract API Client (partial)
//  - 对应 AI Wars 合约风格：/capi/v2/...
//  - 默认注入为 IWeexApiClient（Weex:Mode=Contract）
//
//  文件拆分：
//  - Contract/WeexContractApiClient.Market.cs   (行情)
//  - Contract/WeexContractApiClient.Account.cs  (账户/模式)
//  - Contract/WeexContractApiClient.Trading.cs  (下单/撤单/查询)
//  - Contract/WeexContractApiClient.Rules.cs    (stepSize 等规则)
//  - Contract/WeexContractApiClient.Json.cs     (Json 解析/DTO)
// ============================================================================

internal sealed partial class WeexContractApiClient : WeexApiClientBase, IWeexApiClient
{
    // Cache contract rules per symbol (avoid repeated /market/contracts calls).
    private readonly ConcurrentDictionary<string, decimal> _sizeStepCache =
        new(StringComparer.OrdinalIgnoreCase);

    // Cache margin mode per symbol (best-effort). Values:
    // - -1: omit marginMode field (some tenants infer from account config)
    // - 0/1/2/3: tenant-specific enum (docs commonly mention 1=Cross, 3=Isolated)
    private readonly ConcurrentDictionary<string, int> _marginModeCache =
        new(StringComparer.OrdinalIgnoreCase);

    public WeexContractApiClient(
        HttpClient httpClient,
        IOptions<WeexApiConfig> config,
        ILogger<WeexContractApiClient> logger)
        : base(httpClient, config, logger)
    {
    }
}


