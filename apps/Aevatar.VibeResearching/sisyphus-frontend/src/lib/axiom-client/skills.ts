// ============================================================================
//  Axiom Client - SkillsMP Marketplace APIs
// ============================================================================

import { fetchJson } from './fetch'
import type { SkillsMpStatus, SkillsMpSearchResponse, SkillPackConfig } from './types'

// === SkillsMP Status ===

export async function getSkillsMpStatus(): Promise<SkillsMpStatus> {
  return fetchJson<SkillsMpStatus>('/api/skillsmp/status')
}

// === SkillsMP Search ===

export async function searchSkillsMp(
  query: string,
  mode: 'search' | 'ai-search' = 'search',
  options?: { page?: number; limit?: number; sortBy?: string }
): Promise<SkillsMpSearchResponse> {
  const params = new URLSearchParams({ q: query })
  if (options?.page) params.set('page', String(options.page))
  if (options?.limit) params.set('limit', String(options.limit))
  if (options?.sortBy) params.set('sortBy', options.sortBy)

  const endpoint = mode === 'ai-search' ? '/api/skillsmp/ai-search' : '/api/skillsmp/search'
  return fetchJson<SkillsMpSearchResponse>(`${endpoint}?${params}`)
}

// === Skill Pack Installation ===

export async function installSkillPack(config: SkillPackConfig): Promise<unknown> {
  return fetchJson<unknown>('/api/skillsmp/install', {
    method: 'POST',
    body: JSON.stringify({
      name: config.name || undefined,
      repoUrl: config.repoUrl,
      ref: config.ref || 'main',
      skillsSubDir: config.skillsSubDir || 'skills',
      sync: config.sync ?? true,
    }),
  })
}
