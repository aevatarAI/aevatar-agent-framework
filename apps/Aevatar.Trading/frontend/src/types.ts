// =============================================================================
// DTO Types（对齐后端返回；只定义“我们会用到的字段”）
// =============================================================================

export type TradingSystemStatus = {
  dataCollector: string;
  sentimentAnalyst: string;
  technicalAnalyst: string;
  coordinator: string;
  decisionTrigger: string;
  policyManager: string;
  riskManager: string;
  executor: string;
  tradeAudit: string;
  aiWarsUploader: string;
};

export type AgentsResponse = {
  agents: Array<{ name: string; status: string }>;
};

export type TickerResponse = {
  symbol: string;
  lastPrice: number;
  bidPrice: number;
  askPrice: number;
  volume24h: number;
  change24h: number;
  high24h: number;
  low24h: number;
  timestamp: string;
};

export type BalanceInfo = {
  currency: string;
  balance: number;
  available: number;
  frozen: number;
};

export type OrderRequest = {
  symbol: string;
  side: "buy" | "sell";
  orderType: "limit" | "market";
  force?: "normal" | "postOnly" | "fok" | "ioc";
  quantity: string;
  price?: string;
  clientOrderId?: string;
};

export type OrderResult = {
  success: boolean;
  orderId?: string | null;
  clientOrderId?: string | null;
  errorCode?: string | null;
  errorMessage?: string | null;
};

export type CancelOrderResult = {
  success: boolean;
  orderId?: string | null;
  clientOrderId?: string | null;
  errorCode?: string | null;
  errorMessage?: string | null;
};

export type OrderInfo = {
  orderId: string;
  clientOrderId?: string | null;
  symbol: string;
  side: string;
  orderType: string;
  status: string;
  price: number;
  quantity: number;
  filledQuantity: number;
  filledPrice: number;
  fee: number;
  createTime: string;
  updateTime?: string | null;
};

export type PositionInfo = {
  symbol: string;
  side: string;
  size: number;
  entryPrice?: number | null;
  markPrice?: number | null;
  unrealizedPnl?: number | null;
  notional?: number | null;
  leverage?: number | null;
};

export type FillInfo = {
  ts?: number | null;
  timeUtc?: string | null;
  symbol?: string | null;
  side: string;
  price?: number | null;
  quantity?: number | null;
  orderId?: string | null;
  fee?: number | null;
};

// =============================================================================
// Dashboard / Audit
// =============================================================================

export type MetaResponse = {
  exchange?: {
    type: string;
    mode: string;
    symbols: string[];
    enableWebsocket: boolean;
    enableRestPolling: boolean;
    capabilities: {
      supportsWebSocket: boolean;
      supportsPositions: boolean;
      supportsFundingRate: boolean;
      supportsOpenInterest: boolean;
      supportsFills: boolean;
      supportsBalances: boolean;
      supportsOrders: boolean;
      supportsKlines: boolean;
    };
  };
  trading: {
    symbol: string;
    interval: string;
    executionMode: string; // "DryRun" | "Live" (stringified)
    minConfidenceToTrade: number;
    maxPositionPct: number;
    maxTotalPositionPct: number;
  };
  trigger?: {
    priceChangePct: number;
    priceChangeAbs: number;
    windowSeconds: number;
    cooldownSeconds: number;
    triggerOnStartup: boolean;
  };
  policy?: {
    trading: {
      symbol: string;
      interval: string;
      executionMode: string;
      minConfidenceToTrade: number;
      maxPositionPct: number;
      maxTotalPositionPct: number;
    };
    risk: {
      maxConsecutiveLosses: number;
      cooldownMinutes: number;
      stopLossPct: number;
      takeProfitPct: number;
    };
    analysis: {
      sentimentWeight: number;
      technicalWeight: number;
      newsWeight: number;
    };
    trigger: {
      priceChangePct: number;
      priceChangeAbs: number;
      windowSeconds: number;
      cooldownSeconds: number;
      triggerOnStartup: boolean;
    };
  };
  weex: {
    mode: string; // "Contract" | "Spot"
    baseUrl: string;
    marketDataBaseUrl: string;
    tradingBaseUrl: string;
    publicWebSocketUrl: string;
    webSocketOrigin: string;
  };
  audit: {
    enabled: boolean;
    outputDir: string;
    includeMarketData: boolean;
    requestAiWarsUpload: boolean;
  };
  aiWars: {
    enabled: boolean;
    baseUrl: string;
    uploadPath: string;
  };
};

// =============================================================================
// Policy / Trigger / Positions
// =============================================================================

export type TradingPolicyConfig = {
  trading?: {
    symbol?: string;
    interval?: string;
    executionMode?: string;
    minConfidenceToTrade?: number;
    maxPositionPct?: number;
    maxTotalPositionPct?: number;
    maxLossPerTrade?: number;
    maxDailyLoss?: number;
    minBaseAssetUsdOnStart?: number;
  };
  risk?: {
    maxConsecutiveLosses?: number;
    cooldownMinutes?: number;
    stopLossPct?: number;
    takeProfitPct?: number;
  };
  analysis?: {
    sentimentWeight?: number;
    technicalWeight?: number;
    newsWeight?: number;
  };
  trigger?: {
    priceChangePct?: number;
    priceChangeAbs?: number;
    windowSeconds?: number;
    cooldownSeconds?: number;
    triggerOnStartup?: boolean;
  };
};

export type TradingPolicyUpdatedEvent = {
  updatedBy: string;
  reason: string;
  policy: TradingPolicyConfig;
  timestamp: string;
};

export type UpdatePolicyRequest = {
  policy: TradingPolicyConfig;
  updatedBy?: string;
  reason?: string;
};

export type UpdatePolicyResponse = {
  message: string;
  event: TradingPolicyUpdatedEvent;
};

export type DecisionTriggerRequest = {
  symbol: string;
  reason?: string;
  deltaPct?: number;
  deltaAbs?: number;
};

export type DecisionTriggerEvent = {
  triggerId: string;
  symbol: string;
  reason: string;
  deltaPct: number;
  deltaAbs: number;
  basePrice: number;
  latestPrice: number;
  timestamp: string;
};

export type PositionsResponse = {
  supported: boolean;
  count: number;
  positions: PositionInfo[];
};

export type AuditLatestResponse = {
  directory: string;
  file: string | null;
  runId: string | null;
  updatedAtUtc: string | null;
  content: string;
};

export type AuditFileListResponse = {
  directory: string | null;
  files: Array<{
    name: string;
    sizeBytes: number;
    lastWriteTimeUtc: string;
  }>;
};


