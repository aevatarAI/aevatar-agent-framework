// ============================================================
//  SRA HTTP API types (plugin-side)
//
//  Notes:
//  - This is NOT a cross-runtime boundary contract inside Aevatar.
//  - We keep these types minimal and resilient to backend changes.
// ============================================================

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

export class SraApiError extends Error {
  readonly status: number;
  readonly url: string;
  readonly responseText: string;

  constructor(message: string, args: { status: number; url: string; responseText: string }) {
    super(message);
    this.name = "SraApiError";
    this.status = args.status;
    this.url = args.url;
    this.responseText = args.responseText;
  }
}


