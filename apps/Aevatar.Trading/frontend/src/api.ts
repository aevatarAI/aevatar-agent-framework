import type {
  DecisionTriggerEvent,
  DecisionTriggerRequest,
  PositionsResponse,
  TradingPolicyConfig,
  UpdatePolicyRequest,
  UpdatePolicyResponse,
} from "./types";

// =============================================================================
// API Client（最小封装，避免重复 if/else）
//
// - 默认使用同源（配合 Vite proxy）
// - 也支持通过 VITE_API_BASE_URL 直连某个后端地址
// =============================================================================

export type ApiError = {
  status: number;
  message: string;
  details?: unknown;
};

function getApiBaseUrl(): string {
  const raw = import.meta.env.VITE_API_BASE_URL as string | undefined;
  if (!raw) return "";
  return raw.replace(/\/+$/, "");
}

async function readJsonSafe(res: Response): Promise<unknown> {
  const text = await res.text();
  if (!text) return null;
  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
}

export async function apiFetch<T>(
  path: string,
  init?: RequestInit,
): Promise<T> {
  const url = `${getApiBaseUrl()}${path}`;
  const res = await fetch(url, {
    ...init,
    headers: {
      "Content-Type": "application/json",
      ...(init?.headers ?? {}),
    },
  });

  const body = await readJsonSafe(res);
  if (!res.ok) {
    const message =
      typeof body === "object" && body && "error" in body
        ? String((body as { error?: unknown }).error ?? res.statusText)
        : res.statusText;
    const err: ApiError = { status: res.status, message, details: body };
    throw err;
  }
  return body as T;
}

export function prettyJson(x: unknown): string {
  try {
    return JSON.stringify(x, null, 2);
  } catch {
    return String(x);
  }
}

// =============================================================================
// AG-UI Chat
// =============================================================================
export async function sendAgUiChat(message: string, userId?: string): Promise<void> {
  await apiFetch("/api/agui/chat", {
    method: "POST",
    body: JSON.stringify({ message, userId }),
  });
}

// =============================================================================
// Policy / Decision / Positions
// =============================================================================
export function fetchPolicy(): Promise<TradingPolicyConfig> {
  return apiFetch<TradingPolicyConfig>("/api/policy");
}

export function updatePolicy(payload: UpdatePolicyRequest): Promise<UpdatePolicyResponse> {
  return apiFetch<UpdatePolicyResponse>("/api/policy", {
    method: "POST",
    body: JSON.stringify(payload),
  });
}

export function triggerDecision(payload: DecisionTriggerRequest): Promise<DecisionTriggerEvent> {
  return apiFetch<DecisionTriggerEvent>("/api/decision/trigger", {
    method: "POST",
    body: JSON.stringify(payload),
  });
}

export function fetchPositions(symbol?: string): Promise<PositionsResponse> {
  const qs = symbol ? `?${new URLSearchParams({ symbol }).toString()}` : "";
  return apiFetch<PositionsResponse>(`/api/positions${qs}`);
}


