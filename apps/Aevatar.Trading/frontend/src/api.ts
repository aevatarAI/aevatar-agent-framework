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


