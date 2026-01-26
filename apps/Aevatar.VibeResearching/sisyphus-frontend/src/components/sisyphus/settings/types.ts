// ============================================================================
//  Settings Panel - Shared Types
// ============================================================================

export type TabKey = 'tools' | 'providers' | 'agents' | 'advanced'

export interface SettingsPanelProps {
  sessionId: string | null
  connected: boolean
}

export interface SkillsSyncLog {
  message?: string
}

export interface SkillsSyncStatus {
  running?: boolean
  current?: {
    packName?: string
    repoUrl?: string
    step?: string
  }
  logs?: SkillsSyncLog[]
}

export interface SyncSkillsResult {
  ok?: boolean
  packs?: Array<{ ok?: boolean }>
}

export interface AgentProvidersResponse {
  map?: Record<string, string>
}

export interface SetAgentProviderResponse {
  ok?: boolean
  map?: Record<string, string>
  error?: string
}
