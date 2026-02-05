// ============================================================================
//  User Providers Tab - Full settings tab combining:
//  - Codex (ChatGPT) connection card
//  - User provider list with add/edit/delete/test/default actions
//  - Provider limit indicator
// ============================================================================

import React, { useEffect, useState, useCallback } from 'react'
import { Plus } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { useToast } from '@/components/ui/toast'
import { useUserProviderStore } from '@/store/user-provider-store'
import { ProviderList } from './ProviderList'
import { ProviderForm } from './ProviderForm'
import { DeleteProviderConfirm } from './DeleteProviderConfirm'
import { CodexConnectionCard } from './CodexConnectionCard'
import { USER_PROVIDER_CONSTS } from '@/types/user-provider'
import type {
  UserLlmProviderDto,
  CreateUserProviderRequest,
  UpdateUserProviderRequest,
} from '@/types/user-provider'
import {
  createUserProvider,
  updateUserProvider,
  deleteUserProvider,
  setUserDefaultProvider,
  testUserProvider,
} from '@/lib/axiom-client/user-provider'

/**
 * Main tab component for user-level LLM provider management.
 * Integrates provider CRUD, Codex OAuth, and default selection.
 */
export const UserProvidersTab: React.FC = () => {
  const toast = useToast()
  const providers = useUserProviderStore((s) => s.providers)
  const loading = useUserProviderStore((s) => s.providersLoading)
  const fetchProviders = useUserProviderStore((s) => s.fetchProviders)
  const fetchCodexStatus = useUserProviderStore((s) => s.fetchCodexStatus)
  const addProviderToStore = useUserProviderStore((s) => s.addProvider)
  const updateProviderInStore = useUserProviderStore((s) => s.updateProvider)
  const removeProviderFromStore = useUserProviderStore((s) => s.removeProvider)

  // Dialog state
  const [formOpen, setFormOpen] = useState(false)
  const [deleteOpen, setDeleteOpen] = useState(false)
  const [editingProvider, setEditingProvider] = useState<UserLlmProviderDto | null>(null)
  const [deletingProvider, setDeletingProvider] = useState<UserLlmProviderDto | null>(null)
  const [busy, setBusy] = useState(false)

  // Load data on mount
  useEffect(() => {
    fetchProviders()
    fetchCodexStatus()
  }, [fetchProviders, fetchCodexStatus])

  // === Handlers ===

  const handleAdd = useCallback(() => {
    setEditingProvider(null)
    setFormOpen(true)
  }, [])

  const handleEdit = useCallback((provider: UserLlmProviderDto) => {
    setEditingProvider(provider)
    setFormOpen(true)
  }, [])

  const handleDeleteClick = useCallback((provider: UserLlmProviderDto) => {
    setDeletingProvider(provider)
    setDeleteOpen(true)
  }, [])

  const handleFormSubmit = useCallback(
    async (
      data: CreateUserProviderRequest | UpdateUserProviderRequest,
      isEdit: boolean
    ) => {
      setBusy(true)
      try {
        if (isEdit && editingProvider) {
          const updated = await updateUserProvider(editingProvider.id, data as UpdateUserProviderRequest)
          updateProviderInStore(updated)
          toast.success('Provider updated', `${updated.name} has been updated.`)
        } else {
          const created = await createUserProvider(data as CreateUserProviderRequest)
          addProviderToStore(created)
          toast.success('Provider added', `${created.name} has been configured.`)
        }
        setFormOpen(false)
        // Refresh to pick up default changes
        await fetchProviders()
      } catch (e) {
        const err = e as Error
        toast.error(isEdit ? 'Update failed' : 'Creation failed', err.message)
      }
      setBusy(false)
    },
    [editingProvider, updateProviderInStore, addProviderToStore, fetchProviders, toast]
  )

  const handleDeleteConfirm = useCallback(async () => {
    if (!deletingProvider) return
    setBusy(true)
    try {
      await deleteUserProvider(deletingProvider.id)
      removeProviderFromStore(deletingProvider.id)
      toast.success('Provider deleted', `${deletingProvider.name} has been removed.`)
      setDeleteOpen(false)
      // Refresh to pick up default promotion
      await fetchProviders()
    } catch (e) {
      const err = e as Error
      toast.error('Delete failed', err.message)
    }
    setBusy(false)
  }, [deletingProvider, removeProviderFromStore, fetchProviders, toast])

  const handleSetDefault = useCallback(
    async (provider: UserLlmProviderDto) => {
      if (provider.isDefault) return
      try {
        await setUserDefaultProvider({ providerId: provider.id })
        toast.success('Default updated', `${provider.name} is now your default provider.`)
        await fetchProviders()
      } catch (e) {
        const err = e as Error
        toast.error('Failed to set default', err.message)
      }
    },
    [fetchProviders, toast]
  )

  const handleTest = useCallback(
    async (provider: UserLlmProviderDto) => {
      try {
        const result = await testUserProvider(provider.id)
        if (result.ok) {
          toast.success(
            'Connection successful',
            `${provider.name}: ${result.message}${result.latencyMs > 0 ? ` (${result.latencyMs}ms)` : ''}`
          )
        } else {
          toast.error('Connection failed', result.message)
        }
      } catch (e) {
        const err = e as Error
        toast.error('Test failed', err.message)
      }
    },
    [toast]
  )

  const isAtLimit = providers.length >= USER_PROVIDER_CONSTS.maxProvidersPerUser

  return (
    <div className="space-y-6">
      {/* Codex OAuth Card */}
      <CodexConnectionCard />

      {/* Provider Header */}
      <div className="flex items-center justify-between">
        <div>
          <h3 className="text-sm font-display font-semibold text-neon-cyan tracking-wider">
            Your LLM Providers
          </h3>
          <p className="text-[11px] text-text-muted mt-0.5">
            {providers.length} / {USER_PROVIDER_CONSTS.maxProvidersPerUser} providers configured
          </p>
        </div>
        <Button
          size="sm"
          onClick={handleAdd}
          disabled={isAtLimit}
          title={isAtLimit ? 'Maximum providers reached' : 'Add a new provider'}
        >
          <Plus className="w-3.5 h-3.5" />
          Add Provider
        </Button>
      </div>

      {/* Limit warning */}
      {isAtLimit && (
        <div className="px-3 py-2 rounded-lg bg-neon-gold/5 border border-neon-gold/20 text-[11px] text-neon-gold font-mono">
          Maximum of {USER_PROVIDER_CONSTS.maxProvidersPerUser} providers reached. Delete an existing provider to add a new one.
        </div>
      )}

      {/* Provider List */}
      <ProviderList
        providers={providers}
        loading={loading}
        onEdit={handleEdit}
        onDelete={handleDeleteClick}
        onSetDefault={handleSetDefault}
        onTest={handleTest}
      />

      {/* Add/Edit Dialog */}
      <ProviderForm
        open={formOpen}
        onOpenChange={setFormOpen}
        editingProvider={editingProvider}
        onSubmit={handleFormSubmit}
        busy={busy}
      />

      {/* Delete Confirmation Dialog */}
      <DeleteProviderConfirm
        open={deleteOpen}
        onOpenChange={setDeleteOpen}
        provider={deletingProvider}
        onConfirm={handleDeleteConfirm}
        busy={busy}
      />

      {/* Hint */}
      <div className="pt-2 border-t border-border-subtle">
        <p className="text-[10px] text-text-dimmed font-mono">
          Your API keys are encrypted with AES-256-GCM at rest. The platform default provider is used when no user provider is configured.
        </p>
      </div>
    </div>
  )
}
