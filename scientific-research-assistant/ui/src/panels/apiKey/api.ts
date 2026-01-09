import type { SraTransport } from "../../transport/SraTransport";
import type { ProviderItem, ProviderPublic } from "./types";

function requireGet(transport: SraTransport) {
  if (!transport.getJson) throw new Error("Transport does not implement getJson.");
  return transport.getJson.bind(transport);
}

function requirePost(transport: SraTransport) {
  if (!transport.postJson) throw new Error("Transport does not implement postJson.");
  return transport.postJson.bind(transport);
}

function requireDelete(transport: SraTransport) {
  if (!transport.deleteJson) throw new Error("Transport does not implement deleteJson.");
  return transport.deleteJson.bind(transport);
}

export async function listProvidersAsync(transport: SraTransport): Promise<ProviderItem[]> {
  const getJson = requireGet(transport);
  const json: any = await getJson("/api/llm/providers");
  const list = Array.isArray(json?.providers) ? (json.providers as ProviderItem[]) : [];
  return list;
}

export async function getProviderAsync(transport: SraTransport, providerName: string): Promise<ProviderPublic | null> {
  const getJson = requireGet(transport);
  const name = String(providerName ?? "").trim();
  if (!name) return null;
  const json: any = await getJson(`/api/llm/provider/${encodeURIComponent(name)}`);
  const p = (json?.provider as ProviderPublic) || null;
  return p;
}

export async function getApiKeyStatusAsync(
  transport: SraTransport,
  providerName: string,
  opts?: { reveal?: boolean },
): Promise<{ configured: boolean; masked: string; value?: string }> {
  const getJson = requireGet(transport);
  const name = String(providerName ?? "").trim();
  if (!name) throw new Error("providerName is required");
  const q = opts?.reveal === true ? "?reveal=true" : "";
  const json: any = await getJson(`/api/llm/api-key/${encodeURIComponent(name)}${q}`);
  if (!json || json.ok !== true) throw new Error(String(json?.error ?? "bad response"));
  return {
    configured: Boolean(json.configured),
    masked: String(json.masked || ""),
    value: typeof json.value === "string" ? json.value : undefined,
  };
}

export async function setLlmApiKeyAsync(transport: SraTransport, providerName: string, apiKey: string): Promise<void> {
  const postJson = requirePost(transport);
  const name = String(providerName ?? "").trim();
  const key = String(apiKey ?? "").trim();
  if (!name) throw new Error("providerName is required");
  if (!key) throw new Error("apiKey is required");
  const json: any = await postJson("/api/llm/api-key", { providerName: name, apiKey: key });
  if (!json || json.ok !== true) throw new Error(String(json?.error ?? "save failed"));
}

export async function deleteLlmApiKeyAsync(transport: SraTransport, providerName: string): Promise<void> {
  const deleteJson = requireDelete(transport);
  const name = String(providerName ?? "").trim();
  if (!name) throw new Error("providerName is required");
  const json: any = await deleteJson(`/api/llm/api-key/${encodeURIComponent(name)}`);
  if (!json || json.ok !== true) throw new Error(String(json?.error ?? "delete failed"));
}

export async function setSecretAsync(transport: SraTransport, key: string, value: string): Promise<void> {
  const postJson = requirePost(transport);
  const k = String(key ?? "").trim();
  const v = String(value ?? "").trim();
  if (!k) throw new Error("key is required");
  if (!v) throw new Error("value is required");
  const json: any = await postJson("/api/secrets/set", { key: k, value: v });
  if (!json || json.ok !== true) throw new Error(String(json?.error ?? "save failed"));
}

export async function removeSecretAsync(transport: SraTransport, key: string): Promise<{ removed: boolean }> {
  const postJson = requirePost(transport);
  const k = String(key ?? "").trim();
  if (!k) throw new Error("key is required");
  const json: any = await postJson("/api/secrets/remove", { key: k });
  if (!json || json.ok !== true) throw new Error(String(json?.error ?? "remove failed"));
  return { removed: Boolean(json.removed) };
}

export async function testProviderAsync(transport: SraTransport, providerName: string): Promise<any> {
  const getJson = requireGet(transport);
  const name = String(providerName ?? "").trim();
  if (!name) throw new Error("providerName is required");
  return await getJson(`/api/llm/test/${encodeURIComponent(name)}`);
}

export async function fetchModelsAsync(
  transport: SraTransport,
  providerName: string,
  limit = 200,
): Promise<{ models: string[]; raw: any }> {
  const getJson = requireGet(transport);
  const name = String(providerName ?? "").trim();
  if (!name) throw new Error("providerName is required");
  const json: any = await getJson(`/api/llm/models/${encodeURIComponent(name)}?limit=${encodeURIComponent(String(limit))}`);
  const arr = Array.isArray(json?.models) ? json.models.map((x: any) => String(x)).filter(Boolean) : [];
  return { models: arr, raw: json };
}


