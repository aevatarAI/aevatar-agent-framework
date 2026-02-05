// ============================================================================
//  Axiom Client - User-Level LLM Provider APIs
//  Calls all /api/user/llm/* and /api/sessions/*/agent-providers endpoints
// ============================================================================

import { fetchJson } from './fetch'
import type {
  UserLlmProviderDto,
  UserLlmProviderListResponse,
  CreateUserProviderRequest,
  UpdateUserProviderRequest,
  SetDefaultProviderRequest,
  ProviderTestResult,
  ProviderModelsResponse,
  CodexInitiateRequest,
  CodexInitiateResponse,
  CodexCallbackRequest,
  CodexCallbackResponse,
  CodexStatusResponse,
  CodexAuthModeResponse,
  DeviceCodeInitiateResponse,
  DeviceCodePollRequest,
  DeviceCodePollResponse,
  AgentProvidersSnapshotResponse,
  UpdateAgentProvidersRequest,
  AvailableProvidersResponse,
  OkResponse,
} from './types'

// === User Provider CRUD ===

/** List all LLM providers configured by the current user. */
export async function listUserProviders(): Promise<UserLlmProviderListResponse> {
  return fetchJson<UserLlmProviderListResponse>('/api/user/llm/providers')
}

/** Create a new user LLM provider. */
export async function createUserProvider(
  input: CreateUserProviderRequest
): Promise<UserLlmProviderDto> {
  return fetchJson<UserLlmProviderDto>('/api/user/llm/providers', {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

/** Update an existing user LLM provider. */
export async function updateUserProvider(
  id: string,
  input: UpdateUserProviderRequest
): Promise<UserLlmProviderDto> {
  return fetchJson<UserLlmProviderDto>(
    `/api/user/llm/providers/${encodeURIComponent(id)}`,
    {
      method: 'PUT',
      body: JSON.stringify(input),
    }
  )
}

/** Delete a user LLM provider. */
export async function deleteUserProvider(id: string): Promise<OkResponse> {
  return fetchJson<OkResponse>(
    `/api/user/llm/providers/${encodeURIComponent(id)}`,
    { method: 'DELETE' }
  )
}

/** Test a user LLM provider's connectivity. */
export async function testUserProvider(id: string): Promise<ProviderTestResult> {
  return fetchJson<ProviderTestResult>(
    `/api/user/llm/providers/${encodeURIComponent(id)}/test`,
    { method: 'POST' }
  )
}

/** List available models for a user provider. */
export async function listUserProviderModels(
  id: string,
  limit = 50
): Promise<ProviderModelsResponse> {
  return fetchJson<ProviderModelsResponse>(
    `/api/user/llm/providers/${encodeURIComponent(id)}/models?limit=${limit}`
  )
}

/** Set a provider as the user's default. */
export async function setUserDefaultProvider(
  input: SetDefaultProviderRequest
): Promise<OkResponse> {
  return fetchJson<OkResponse>('/api/user/llm/providers/default', {
    method: 'PUT',
    body: JSON.stringify(input),
  })
}

// === Codex OAuth ===

/** Initiate the Codex OAuth PKCE flow. */
export async function initiateCodexOAuth(
  input: CodexInitiateRequest
): Promise<CodexInitiateResponse> {
  return fetchJson<CodexInitiateResponse>('/api/user/llm/codex/initiate', {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

/** Complete the Codex OAuth flow with authorization code. */
export async function completeCodexOAuth(
  input: CodexCallbackRequest
): Promise<CodexCallbackResponse> {
  return fetchJson<CodexCallbackResponse>('/api/user/llm/codex/callback', {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

/** Get the current Codex connection status. */
export async function getCodexStatus(): Promise<CodexStatusResponse> {
  return fetchJson<CodexStatusResponse>('/api/user/llm/codex/status')
}

/** Disconnect the Codex OAuth connection. */
export async function disconnectCodex(): Promise<OkResponse> {
  return fetchJson<OkResponse>('/api/user/llm/codex', { method: 'DELETE' })
}

/** Get the server's configured Codex auth mode (localhost or devicecode). */
export async function getCodexAuthMode(): Promise<CodexAuthModeResponse> {
  return fetchJson<CodexAuthModeResponse>('/api/user/llm/codex/auth-mode')
}

// === Codex Device Code Flow ===

/** Request a device code for the Device Code authorization grant. */
export async function initiateDeviceCode(): Promise<DeviceCodeInitiateResponse> {
  return fetchJson<DeviceCodeInitiateResponse>('/api/user/llm/codex/device/initiate', {
    method: 'POST',
  })
}

/** Poll for device code authorization completion. */
export async function pollDeviceCode(
  input: DeviceCodePollRequest
): Promise<DeviceCodePollResponse> {
  return fetchJson<DeviceCodePollResponse>('/api/user/llm/codex/device/poll', {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

// === Session Agent Provider Mapping ===

/** Get agent-provider mappings for a session. */
export async function getSessionAgentProviders(
  sessionId: string
): Promise<AgentProvidersSnapshotResponse> {
  return fetchJson<AgentProvidersSnapshotResponse>(
    `/api/sessions/${encodeURIComponent(sessionId)}/agent-providers`
  )
}

/** Update agent-provider mappings for a session. */
export async function updateSessionAgentProviders(
  sessionId: string,
  input: UpdateAgentProvidersRequest
): Promise<AgentProvidersSnapshotResponse> {
  return fetchJson<AgentProvidersSnapshotResponse>(
    `/api/sessions/${encodeURIComponent(sessionId)}/agent-providers`,
    {
      method: 'PUT',
      body: JSON.stringify(input),
    }
  )
}

/** List all available providers for the current user (user + platform), scoped to a session. */
export async function getAvailableProviders(
  sessionId: string
): Promise<AvailableProvidersResponse> {
  return fetchJson<AvailableProvidersResponse>(
    `/api/sessions/${encodeURIComponent(sessionId)}/available-providers`
  )
}

/** List all available providers for the current user (user + platform), no session required. */
export async function getAvailableProvidersForUser(): Promise<AvailableProvidersResponse> {
  return fetchJson<AvailableProvidersResponse>('/api/user/llm/available-providers')
}
