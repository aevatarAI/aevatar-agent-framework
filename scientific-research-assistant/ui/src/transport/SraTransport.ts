// ============================================================
//  Shared UI ↔ SRA backend transport contract
//
//  中文说明：
//  - 这是前端 UI 的“宿主无关”传输层抽象（Web/Obsidian 都实现它）
//  - 不是 Aevatar runtime 的跨边界契约（不会影响 Protobuf 规则）
//  - 目标：让同一套 React UI 在不同宿主环境中复用
// ============================================================

export type SraJson = null | boolean | number | string | SraJson[] | { [key: string]: SraJson };

export type SraTransportStatus = "Connecting" | "Connected" | "Disconnected" | "Error";

export type SraAgUiEvent = any;

export interface SraTransportCapabilities {
  // local-only backend APIs (e.g. /files/*) should be gated when baseUrl is remote
  filesApi: boolean;

  // showing/revealing stored secrets should be gated when baseUrl is remote
  revealApiKey: boolean;

  // whether the host can do Node-style SSE (Obsidian Desktop can; browser doesn't need it)
  nodeSse: boolean;
}

export interface SraSessionSummary {
  sessionId: string;
  createdAt: string; // ISO string
  providerName?: string | null;
}

export interface SraListSessionsResponse {
  count: number;
  sessions: SraSessionSummary[];
}

export interface SraCreateSessionResponse {
  ok: boolean;
  sessionId: string;
}

export interface SraSendInputRequest {
  message: string;
  requestId?: string;
  providerName?: string;
  mode?: "chat" | "vibe" | "vibe_loop" | string;
  loop?: {
    maxIterations?: number;
    maxTotalDurationMs?: number;
  };
  toAgents?: string[];
  attachmentPaths?: string[];
}

export interface SraSendInputResponse {
  ok: boolean;
  sessionId: string;
  runId: string;
}

export interface SraDeliverablesResponse {
  ok: boolean;
  sessionId: string;
  brief?: any;
  delivery?: any;
}

export interface SraApiInfoResponse {
  system?: any;
  llm?: any;
  mcp?: any;
}

export interface SraUploadFile {
  filename: string;
  contentType: string;
  data: Uint8Array;
}

export interface SraAgUiConnection {
  close(): void;
}

export interface SraTransport {
  readonly capabilities: SraTransportCapabilities;

  // ============================================================
  //  Core endpoints
  // ============================================================
  health(): Promise<{ ok: boolean; status: number; body: string }>;
  info(): Promise<SraApiInfoResponse>;

  listSessions(): Promise<SraListSessionsResponse>;
  createSession(providerName?: string): Promise<{ sessionId: string }>;

  sendInput(sessionId: string, input: SraSendInputRequest): Promise<{ runId: string }>;
  getDeliverables(sessionId: string): Promise<SraDeliverablesResponse>;

  uploadAttachments(sessionId: string, files: SraUploadFile[]): Promise<{ attachmentPaths: string[] }>;

  // ============================================================
  //  Streaming (AG-UI SSE)
  // ============================================================
  connectAgUiEvents(
    sessionId: string,
    handlers: {
      onStatus?: (status: SraTransportStatus, detail?: string) => void;
      onEvent: (evt: SraAgUiEvent) => void;
      onError?: (err: unknown) => void;
    },
  ): SraAgUiConnection;

  // ============================================================
  //  Optional endpoints used by panels (kept minimal, evolve as needed)
  // ============================================================
  getJson?(path: string): Promise<any>;
  putJson?(path: string, body: any): Promise<any>;
  postJson?(path: string, body?: any): Promise<any>;
  deleteJson?(path: string): Promise<any>;
}


