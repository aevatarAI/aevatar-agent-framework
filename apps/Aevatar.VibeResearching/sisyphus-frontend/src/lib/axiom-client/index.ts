// ============================================================================
//  Axiom Client - Unified Re-exports
//  All modules accessible from '@/lib/axiom-client'
// ============================================================================

// === Types ===
export * from './types'

// === Core Fetch Infrastructure ===
export {
  API_BASE,
  fetchJson,
  getSessionAbortController,
  abortCurrentSessionRequests,
  clearRequestCache,
  validateSessionId,
} from './fetch'

// === Session APIs ===
export {
  listSessions,
  listWorkflows,
  createSession,
  runSession,
  sendMessage,
  stopSession,
  pauseSession,
  resumeSession,
  terminateSession,
  getSessionResult,
  getSessionAgents,
  getAgentStates,
  getAgentHistory,
  getSessionStatus,
  getDagSnapshot,
  getGlobalDagSnapshot,
  getDagNodeExplain,
  getKnowledgeChain,
  getSessionEvents,
} from './session'

// === Agent Utilities ===
export { parseWorkersFromEvents } from './agent'

// === Event Stream ===
export { createAxiomEventStream } from './stream'
export type { AxiomCustomEvents, EventStream } from './stream'

// === LLM Provider APIs ===
export {
  getApiInfo,
  getDefaultProvider,
  setDefaultProvider,
  listLlmProviders,
  listLlmInstances,
  getLlmProvider,
  getApiKeyStatus,
  setLlmApiKey,
  deleteLlmApiKey,
  testLlmProvider,
  fetchLlmModels,
  setSecret,
  removeSecret,
  saveProviderEndpoint,
  saveProviderModel,
} from './llm'

// === Tools & MCP APIs ===
export {
  getToolsSnapshot,
  reconnectMcp,
  syncSkills,
  getSkillsSyncStatus,
  getAgentProviders,
  setAgentProvider,
} from './tools'

// === SkillsMP Marketplace APIs ===
export { getSkillsMpStatus, searchSkillsMp, installSkillPack } from './skills'

// === Knowledge Graph APIs ===
export {
  getNodeExplanation,
  getSessionSummary,
  getFullDagSummary,
  createPivotSnapshot,
  getPivotSnapshots,
} from './graph'

// === Upload APIs ===
export { uploadWithExtraction } from './upload'

// === Review Agent APIs ===
export {
  getReviewAgentStatus,
  getReviewAgentSettings,
  updateReviewAgentSettings,
  getReviewAgentIterations,
  getReviewAgentIteration,
  getReviewAgentGraph,
  getReviewAgentCurrentEntries,
  triggerReviewAgent,
} from './review'
