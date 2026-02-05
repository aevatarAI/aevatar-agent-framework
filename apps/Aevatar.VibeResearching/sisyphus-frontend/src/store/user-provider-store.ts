import { create } from 'zustand'
import type {
  UserLlmProviderDto,
  CodexStatusResponse,
  AvailableProviderDto,
} from '@/types/user-provider'
import {
  listUserProviders,
  getCodexStatus,
} from '@/lib/axiom-client/user-provider'

// ============================================================================
//  User Provider Store - Zustand State Management
//  Manages user-level LLM providers, Codex connection, and available providers
// ============================================================================

interface UserProviderState {
  // Provider list
  providers: UserLlmProviderDto[]
  providersLoading: boolean
  providersError: string | null

  // Codex OAuth status
  codexStatus: CodexStatusResponse | null
  codexLoading: boolean

  // Available providers (user + platform merged)
  availableProviders: AvailableProviderDto[]
  availableLoading: boolean

  // Actions
  fetchProviders: () => Promise<void>
  fetchCodexStatus: () => Promise<void>
  setProviders: (providers: UserLlmProviderDto[]) => void
  addProvider: (provider: UserLlmProviderDto) => void
  updateProvider: (provider: UserLlmProviderDto) => void
  removeProvider: (id: string) => void
  setCodexStatus: (status: CodexStatusResponse | null) => void
  setAvailableProviders: (providers: AvailableProviderDto[]) => void
  setAvailableLoading: (loading: boolean) => void
  reset: () => void
}

const initialState = {
  providers: [] as UserLlmProviderDto[],
  providersLoading: false,
  providersError: null as string | null,
  codexStatus: null as CodexStatusResponse | null,
  codexLoading: false,
  availableProviders: [] as AvailableProviderDto[],
  availableLoading: false,
}

export const useUserProviderStore = create<UserProviderState>((set) => ({
  ...initialState,

  fetchProviders: async () => {
    set({ providersLoading: true, providersError: null })
    try {
      const res = await listUserProviders()
      const providers = Array.isArray(res?.providers) ? res.providers : []
      set({ providers, providersLoading: false })
    } catch (e) {
      const err = e as Error
      set({
        providersError: err.message || 'Failed to load providers',
        providersLoading: false,
      })
    }
  },

  fetchCodexStatus: async () => {
    set({ codexLoading: true })
    try {
      const status = await getCodexStatus()
      set({ codexStatus: status, codexLoading: false })
    } catch {
      set({ codexStatus: { connected: false }, codexLoading: false })
    }
  },

  setProviders: (providers) => set({ providers }),

  addProvider: (provider) =>
    set((state) => ({ providers: [...state.providers, provider] })),

  updateProvider: (provider) =>
    set((state) => ({
      providers: state.providers.map((p) =>
        p.id === provider.id ? provider : p
      ),
    })),

  removeProvider: (id) =>
    set((state) => ({
      providers: state.providers.filter((p) => p.id !== id),
    })),

  setCodexStatus: (status) => set({ codexStatus: status }),

  setAvailableProviders: (providers) =>
    set({ availableProviders: providers }),

  setAvailableLoading: (loading) => set({ availableLoading: loading }),

  reset: () => set(initialState),
}))

// === Derived Selectors ===

export const useUserProviders = () =>
  useUserProviderStore((s) => s.providers)

export const useDefaultUserProvider = () =>
  useUserProviderStore((s) => s.providers.find((p) => p.isDefault) ?? null)

export const useCodexConnected = () =>
  useUserProviderStore((s) => s.codexStatus?.connected ?? false)

export const useUserProvidersLoading = () =>
  useUserProviderStore((s) => s.providersLoading)
