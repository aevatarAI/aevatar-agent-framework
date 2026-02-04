// ============================================================================
//  Axiom Client - LLM Provider APIs
// ============================================================================

import { fetchJson } from './fetch'
import type {
  ProviderItem,
  ProviderPublic,
  ProviderInstance,
  ApiKeyStatusResponse,
  TestProviderResponse,
  FetchModelsResponse,
} from './types'

// === API Info ===

export async function getApiInfo(): Promise<unknown> {
  return fetchJson<unknown>('/api/info')
}

// === Default Provider ===

export async function getDefaultProvider(): Promise<{ ok: boolean; providerName: string }> {
  return fetchJson<{ ok: boolean; providerName: string }>('/api/llm/default')
}

export async function setDefaultProvider(
  providerName: string
): Promise<{ ok: boolean; providerName?: string; error?: string }> {
  return fetchJson<{ ok: boolean; providerName?: string; error?: string }>('/api/llm/default', {
    method: 'POST',
    body: JSON.stringify({ providerName }),
  })
}

// === Provider Catalog ===

export async function listLlmProviders(): Promise<{ providers: ProviderItem[] }> {
  return fetchJson<{ providers: ProviderItem[] }>('/api/llm/providers')
}

export async function listLlmInstances(): Promise<{ instances: ProviderInstance[] }> {
  return fetchJson<{ instances: ProviderInstance[] }>('/api/llm/instances')
}

export async function getLlmProvider(
  providerName: string
): Promise<{ provider: ProviderPublic }> {
  return fetchJson<{ provider: ProviderPublic }>(
    `/api/llm/provider/${encodeURIComponent(providerName)}`
  )
}

// === API Key Management ===

export async function getApiKeyStatus(
  providerName: string,
  reveal = false
): Promise<ApiKeyStatusResponse> {
  const q = reveal ? '?reveal=true' : ''
  return fetchJson<ApiKeyStatusResponse>(
    `/api/llm/api-key/${encodeURIComponent(providerName)}${q}`
  )
}

export async function setLlmApiKey(
  providerName: string,
  apiKey: string
): Promise<{ ok: boolean }> {
  return fetchJson<{ ok: boolean }>('/api/llm/api-key', {
    method: 'POST',
    body: JSON.stringify({ providerName, apiKey }),
  })
}

export async function deleteLlmApiKey(providerName: string): Promise<{ ok: boolean }> {
  return fetchJson<{ ok: boolean }>(`/api/llm/api-key/${encodeURIComponent(providerName)}`, {
    method: 'DELETE',
  })
}

// === Provider Testing ===

export async function testLlmProvider(providerName: string): Promise<TestProviderResponse> {
  return fetchJson<TestProviderResponse>(`/api/llm/test/${encodeURIComponent(providerName)}`)
}

export async function fetchLlmModels(
  providerName: string,
  limit = 200
): Promise<FetchModelsResponse> {
  return fetchJson<FetchModelsResponse>(
    `/api/llm/models/${encodeURIComponent(providerName)}?limit=${limit}`
  )
}

// === Secrets Management ===

export async function setSecret(key: string, value: string): Promise<{ ok: boolean }> {
  return fetchJson<{ ok: boolean }>('/api/secrets/set', {
    method: 'POST',
    body: JSON.stringify({ key, value }),
  })
}

export async function removeSecret(key: string): Promise<{ ok: boolean; removed: boolean }> {
  return fetchJson<{ ok: boolean; removed: boolean }>('/api/secrets/remove', {
    method: 'POST',
    body: JSON.stringify({ key }),
  })
}

export async function saveProviderEndpoint(
  providerName: string,
  endpoint: string
): Promise<{ ok: boolean }> {
  const key = `LLMProviders:Providers:${providerName}:Endpoint`
  if (!endpoint.trim()) {
    return removeSecret(key)
  }
  return setSecret(key, endpoint.trim())
}

export async function saveProviderModel(
  providerName: string,
  model: string
): Promise<{ ok: boolean }> {
  const key = `LLMProviders:Providers:${providerName}:Model`
  if (!model.trim()) {
    return removeSecret(key)
  }
  return setSecret(key, model.trim())
}
