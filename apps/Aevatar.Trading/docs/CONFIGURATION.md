# Trading Configuration Guide

> 目标：把交易所的 endpoint / api key / secret / passphrase 统一管理，避免分散到多个区块。

---

## 1) 统一配置结构（推荐）

核心思路：**Exchange 负责选择哪家交易所，ExchangeCredentials 负责保存凭证**。

```json
{
  "Exchange": {
    "ExchangeType": "WEEX",
    "Mode": "Contract",
    "Symbols": ["cmt_btcusdt"],
    "EnableWebsocket": true,
    "EnableRestPolling": true,
    "CredentialsKey": "weex"
  },
  "ExchangeCredentials": {
    "weex": {
      "Endpoint": "https://api-contract.weex.com",
      "ApiKey": "YOUR_WEEX_API_KEY",
      "ApiSecret": "YOUR_WEEX_API_SECRET",
      "Passphrase": "YOUR_WEEX_PASSPHRASE"
    },
    "okx": {
      "Endpoint": "",
      "ApiKey": "",
      "ApiSecret": "",
      "Passphrase": ""
    }
  }
}
```

### 字段说明

- `Exchange.ExchangeType`：运行时选用交易所（`WEEX` / `OKX`）
- `Exchange.CredentialsKey`：凭证映射键（建议使用小写，如 `weex` / `okx`）
- `ExchangeCredentials.{key}.Endpoint`：交易所基地址
- `ApiKey` / `ApiSecret` / `Passphrase`：鉴权三元组

> 规则：如果 `CredentialsKey` 为空，系统默认使用 `ExchangeType` 的小写作为 key。

---

## 2) 推荐落地方式

### appsettings.json（非敏感）

`apps/Aevatar.Trading/src/Aevatar.Trade.Api/appsettings.json`：

```json
{
  "Exchange": {
    "ExchangeType": "WEEX",
    "CredentialsKey": "weex"
  },
  "ExchangeCredentials": {
    "weex": {
      "Endpoint": "https://api-contract.weex.com",
      "ApiKey": "",
      "ApiSecret": "",
      "Passphrase": ""
    }
  }
}
```

### appsettings.secrets.json（敏感信息）

`apps/Aevatar.Trading/src/Aevatar.Trade.Api/appsettings.secrets.json`：

```json
{
  "ExchangeCredentials": {
    "weex": {
      "Endpoint": "https://api-contract.weex.com",
      "ApiKey": "YOUR_WEEX_API_KEY",
      "ApiSecret": "YOUR_WEEX_API_SECRET",
      "Passphrase": "YOUR_WEEX_PASSPHRASE"
    }
  }
}
```

> 另外提供完整模板：`apps/Aevatar.Trading/src/Aevatar.Trade.Api/appsettings.secrets.json.example.full`

---

## 3) LLMProviders（必须，来自用户级 secrets）

LLM 配置只从 **用户级 secrets** 读取（默认 `~/.aevatar/secrets.json`），系统会按顺序尝试多个 provider，直到找到可用的。

```json
{
  "LLMProviders": {
    "default": "trade-default",
    "providers": {
      "trade-default": {
        "providerType": "OpenAI",
        "apiKey": "${OPENAI_API_KEY}",
        "model": "gpt-4o-mini",
        "temperature": 0.3,
        "maxTokens": 2000
      },
      "backup": {
        "providerType": "AzureOpenAI",
        "apiKey": "${AZURE_API_KEY}",
        "endpoint": "https://your-resource.openai.azure.com",
        "deploymentName": "gpt-4o-mini"
      }
    }
  }
}
```

> 说明：`default` 不可用时会按顺序尝试其他 provider；全部失败才会报错。

---

## 4) Weex 高级配置（可选）

`Weex` 节只保留与 WEEX 相关的高级项（如 WS / MarketDataBaseUrl）。凭证统一走 `ExchangeCredentials`。

```json
{
  "Weex": {
    "Mode": "Contract",
    "BaseUrl": "https://api-contract.weex.com",
    "MarketDataBaseUrl": "",
    "TradingBaseUrl": "",
    "PublicWebSocketUrl": "",
    "WebSocketOrigin": "https://www.weex.com"
  }
}
```

> 如需 Weex 高级配置字段，请参考完整模板：
> `apps/Aevatar.Trading/src/Aevatar.Trade.Api/appsettings.secrets.json.example.full`

---

## 5) 策略配置（Policy）

`Policy` 是唯一权威入口，包含交易/分析/风控/触发配置：

```json
{
  "Policy": {
    "Trading": { "Symbol": "cmt_btcusdt", "ExecutionMode": "Live" },
    "Analysis": { "SentimentWeight": 0.3, "TechnicalWeight": 0.4, "NewsWeight": 0.3 },
    "Risk": { "MaxDailyLoss": 5, "StopLossPct": 2, "TakeProfitPct": 4 },
    "Trigger": { "PriceChangePct": 0.5, "WindowSeconds": 30, "CooldownSeconds": 30 }
  }
}
```

> 旧版的 `Trading` / `Analysis` / `RiskControl` / `DecisionTrigger` 已移除，避免重复和冲突。

---

## 6) 环境变量桥接（DotNet Skills）

系统会在启动时把 `ExchangeCredentials` 里对应的 WEEX 凭证桥接为环境变量：

```
WEEX_BASE_URL
WEEX_API_KEY
WEEX_API_SECRET
WEEX_PASSPHRASE
```

这样 `Tools/DotNetSkills/*` 可以保持不变。
