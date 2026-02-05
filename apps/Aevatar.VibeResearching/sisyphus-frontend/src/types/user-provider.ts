// ============================================================================
//  User-Level LLM Provider Configuration - Type Definitions
//  Maps to backend DTOs defined in Application.Contracts
// ============================================================================

// === Supported Provider Types ===

export const SUPPORTED_PROVIDER_TYPES = [
  'OpenAI',
  'AzureOpenAI',
  'Anthropic',
  'Google',
  'Ollama',
  'OpenRouter',
  'DeepSeek',
  'CodexOAuth',
] as const

export type SupportedProviderType = (typeof SUPPORTED_PROVIDER_TYPES)[number]

// === Provider Constants ===

export const USER_PROVIDER_CONSTS = {
  maxProvidersPerUser: 20,
  maxNameLength: 100,
  maxApiKeyLength: 500,
  maxModelLength: 200,
  maxDeploymentNameLength: 200,
  maxEndpointLength: 2000,
  namePattern: /^[a-zA-Z0-9 _-]+$/,
  codexProviderName: 'Codex (ChatGPT)',
  codexDefaultModel: 'gpt-5.2-codex',
} as const

/** Models supported by Codex OAuth (no dynamic API — hardcoded from opencode/Cline). */
export const CODEX_SUPPORTED_MODELS = [
  { id: 'gpt-5.2-codex', label: 'GPT-5.2 Codex — Flagship agentic coding' },
  { id: 'gpt-5.2', label: 'GPT-5.2 — General-purpose with strong reasoning' },
  { id: 'gpt-5.1-codex-max', label: 'GPT-5.1 Codex Max — Long-horizon agentic coding' },
  { id: 'gpt-5.1-codex', label: 'GPT-5.1 Codex — Standard coding' },
  { id: 'gpt-5.1-codex-mini', label: 'GPT-5.1 Codex Mini — Cost-effective' },
] as const

// === Provider Namespace ===

export type ProviderSource = 'user' | 'codex' | 'platform'

// === API Response DTOs ===

export interface UserLlmProviderDto {
  id: string
  name: string
  providerType: string
  endpoint: string | null
  defaultModel: string
  deploymentName: string | null
  isDefault: boolean
  isCodexOAuth: boolean
  maskedApiKey: string
  createdAt: string
  updatedAt: string
}

export interface UserLlmProviderListResponse {
  providers: UserLlmProviderDto[]
}

// === API Request DTOs ===

export interface CreateUserProviderRequest {
  name: string
  providerType: string
  apiKey: string
  endpoint: string | null
  defaultModel: string
  deploymentName: string | null
  isDefault: boolean
}

export interface UpdateUserProviderRequest {
  name?: string
  providerType?: string
  apiKey?: string | null
  endpoint?: string | null
  defaultModel?: string
  deploymentName?: string | null
}

export interface SetDefaultProviderRequest {
  providerId: string
}

// === Provider Test ===

export interface ProviderTestResult {
  ok: boolean
  latencyMs: number
  model: string
  message: string
}

// === Provider Models ===

export interface ProviderModelItem {
  id: string
  name: string
  ownedBy: string
}

export interface ProviderModelsResponse {
  models: ProviderModelItem[]
}

// === Codex OAuth ===

export interface CodexInitiateRequest {
  redirectUri: string
}

export interface CodexInitiateResponse {
  authUrl: string
  state: string
}

export interface CodexCallbackRequest {
  code: string
  state: string
  redirectUri: string
}

export interface CodexCallbackResponse {
  status: string
  email: string | null
  providerId: string
}

export interface CodexStatusResponse {
  connected: boolean
  email?: string
  connectedAt?: string
  providerId?: string
}

// === Session Agent Provider Mapping ===

export interface AgentProviderDetail {
  namespace: string
  name: string
  providerType: string
  model: string
}

export interface AgentProvidersSnapshotResponse {
  version: number
  updatedAt: string
  map: Record<string, AgentProviderDetail>
}

export interface UpdateAgentProvidersRequest {
  map: Record<string, string>
}

// === Available Providers (Aggregated) ===

export interface AvailableProviderDto {
  namespace: string
  name: string
  providerType: string
  defaultModel: string
  source: ProviderSource
  isDefault: boolean
}

export interface AvailableProvidersResponse {
  providers: AvailableProviderDto[]
}

// === Generic API Responses ===

export interface OkResponse {
  ok: boolean
}

export interface ApiErrorResponse {
  error: string
  code: string
  details?: Record<string, string>
}

// === Error Codes ===

export const USER_PROVIDER_ERROR_CODES = {
  VALIDATION_FAILED: 'VALIDATION_FAILED',
  PROVIDER_NOT_FOUND: 'PROVIDER_NOT_FOUND',
  PROVIDER_NAME_CONFLICT: 'PROVIDER_NAME_CONFLICT',
  PROVIDER_LIMIT_REACHED: 'PROVIDER_LIMIT_REACHED',
  CODEX_STATE_INVALID: 'CODEX_STATE_INVALID',
  CODEX_TOKEN_EXCHANGE_FAILED: 'CODEX_TOKEN_EXCHANGE_FAILED',
  CODEX_TOKEN_REVOKED: 'CODEX_TOKEN_REVOKED',
  PROVIDER_CONNECTIVITY_FAILED: 'PROVIDER_CONNECTIVITY_FAILED',
  NO_PROVIDER_AVAILABLE: 'NO_PROVIDER_AVAILABLE',
} as const
