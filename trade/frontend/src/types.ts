// =============================================================================
// DTO Types（对齐后端返回；只定义“我们会用到的字段”）
// =============================================================================

export type TradingSystemStatus = {
  dataCollector: string;
  sentimentAnalyst: string;
  technicalAnalyst: string;
  coordinator: string;
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
  trading: {
    symbol: string;
    interval: string;
    executionMode: string; // "DryRun" | "Live" (stringified)
    minConfidenceToTrade: number;
    maxPositionPct: number;
    maxTotalPositionPct: number;
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


