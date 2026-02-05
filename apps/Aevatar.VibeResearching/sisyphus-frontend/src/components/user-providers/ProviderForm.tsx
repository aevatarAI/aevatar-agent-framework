// ============================================================================
//  Provider Form - Add / Edit user LLM provider
//  Shared dialog form component for creating and editing providers
// ============================================================================

import React, { useState, useCallback, useEffect } from 'react'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
} from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import {
  SUPPORTED_PROVIDER_TYPES,
  USER_PROVIDER_CONSTS,
  CODEX_SUPPORTED_MODELS,
} from '@/types/user-provider'
import type {
  UserLlmProviderDto,
  CreateUserProviderRequest,
  UpdateUserProviderRequest,
} from '@/types/user-provider'

// === Form State ===

interface ProviderFormData {
  name: string
  providerType: string
  apiKey: string
  endpoint: string
  defaultModel: string
  deploymentName: string
  isDefault: boolean
}

const EMPTY_FORM: ProviderFormData = {
  name: '',
  providerType: 'OpenAI',
  apiKey: '',
  endpoint: '',
  defaultModel: '',
  deploymentName: '',
  isDefault: false,
}

// === Props ===

interface ProviderFormProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** Existing provider to edit. If null, creates a new provider. */
  editingProvider: UserLlmProviderDto | null
  onSubmit: (
    data: CreateUserProviderRequest | UpdateUserProviderRequest,
    isEdit: boolean
  ) => Promise<void>
  busy: boolean
}

/** Dialog form for adding or editing a user LLM provider. */
export const ProviderForm: React.FC<ProviderFormProps> = ({
  open,
  onOpenChange,
  editingProvider,
  onSubmit,
  busy,
}) => {
  const isEdit = editingProvider !== null
  const [form, setForm] = useState<ProviderFormData>(EMPTY_FORM)
  const [errors, setErrors] = useState<Partial<Record<keyof ProviderFormData, string>>>({})

  // Populate form when editing
  useEffect(() => {
    if (editingProvider) {
      setForm({
        name: editingProvider.name,
        providerType: editingProvider.providerType,
        apiKey: '',
        endpoint: editingProvider.endpoint ?? '',
        defaultModel: editingProvider.defaultModel,
        deploymentName: editingProvider.deploymentName ?? '',
        isDefault: editingProvider.isDefault,
      })
    } else {
      setForm(EMPTY_FORM)
    }
    setErrors({})
  }, [editingProvider, open])

  const updateField = useCallback(
    <K extends keyof ProviderFormData>(key: K, value: ProviderFormData[K]) => {
      setForm((prev) => ({ ...prev, [key]: value }))
      setErrors((prev) => ({ ...prev, [key]: undefined }))
    },
    []
  )

  const validate = useCallback((): boolean => {
    const newErrors: Partial<Record<keyof ProviderFormData, string>> = {}
    if (!form.name.trim()) {
      newErrors.name = 'Name is required'
    } else if (form.name.length > USER_PROVIDER_CONSTS.maxNameLength) {
      newErrors.name = `Name must be at most ${USER_PROVIDER_CONSTS.maxNameLength} characters`
    } else if (!USER_PROVIDER_CONSTS.namePattern.test(form.name)) {
      newErrors.name = 'Name can only contain letters, numbers, spaces, hyphens, and underscores'
    }
    if (!form.providerType) {
      newErrors.providerType = 'Provider type is required'
    }
    if (!isEdit && !form.apiKey.trim()) {
      newErrors.apiKey = 'API key is required'
    }
    if (form.apiKey && form.apiKey.length > USER_PROVIDER_CONSTS.maxApiKeyLength) {
      newErrors.apiKey = `API key must be at most ${USER_PROVIDER_CONSTS.maxApiKeyLength} characters`
    }
    if (!form.defaultModel.trim()) {
      newErrors.defaultModel = 'Model is required'
    }
    if (form.endpoint && !isValidUrl(form.endpoint)) {
      newErrors.endpoint = 'Must be a valid URL'
    }
    setErrors(newErrors)
    return Object.keys(newErrors).length === 0
  }, [form, isEdit])

  const handleSubmit = useCallback(async () => {
    if (!validate() || busy) return
    if (isEdit) {
      const data: UpdateUserProviderRequest = {
        name: form.name,
        providerType: form.providerType,
        apiKey: form.apiKey.trim() || null,
        endpoint: form.endpoint.trim() || null,
        defaultModel: form.defaultModel,
        deploymentName: form.deploymentName.trim() || null,
      }
      await onSubmit(data, true)
    } else {
      const data: CreateUserProviderRequest = {
        name: form.name,
        providerType: form.providerType,
        apiKey: form.apiKey,
        endpoint: form.endpoint.trim() || null,
        defaultModel: form.defaultModel,
        deploymentName: form.deploymentName.trim() || null,
        isDefault: form.isDefault,
      }
      await onSubmit(data, false)
    }
  }, [validate, busy, isEdit, form, onSubmit])

  const isCodex = editingProvider?.isCodexOAuth ?? false

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-md" showCloseButton>
        <DialogHeader>
          <DialogTitle>
            {isEdit ? 'Edit Provider' : 'Add LLM Provider'}
          </DialogTitle>
          <DialogDescription>
            {isEdit
              ? 'Update your provider configuration. Leave API key empty to keep the existing key.'
              : 'Configure a new LLM provider with your own API key.'}
          </DialogDescription>
        </DialogHeader>

        <div className="p-4 space-y-4">
          {/* Name */}
          <FormField label="Name" error={errors.name}>
            <input
              type="text"
              value={form.name}
              onChange={(e) => updateField('name', e.target.value)}
              placeholder="My OpenAI"
              className="input w-full text-sm"
              maxLength={USER_PROVIDER_CONSTS.maxNameLength}
              disabled={isCodex}
            />
          </FormField>

          {/* Provider Type */}
          <FormField label="Provider Type" error={errors.providerType}>
            <Select
              value={form.providerType}
              onValueChange={(v) => updateField('providerType', v)}
              disabled={isCodex}
            >
              <SelectTrigger className="w-full text-sm">
                <SelectValue placeholder="Select provider type..." />
              </SelectTrigger>
              <SelectContent>
                {SUPPORTED_PROVIDER_TYPES.filter((t) => t !== 'CodexOAuth').map((t) => (
                  <SelectItem key={t} value={t}>{t}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </FormField>

          {/* API Key */}
          {!isCodex && (
            <FormField label="API Key" error={errors.apiKey}>
              <input
                type="password"
                value={form.apiKey}
                onChange={(e) => updateField('apiKey', e.target.value)}
                placeholder={isEdit ? 'Enter new key to replace...' : 'sk-...'}
                className="input w-full text-sm font-mono"
                maxLength={USER_PROVIDER_CONSTS.maxApiKeyLength}
              />
            </FormField>
          )}

          {/* Default Model */}
          <FormField label="Default Model" error={errors.defaultModel}>
            {isCodex ? (
              <Select
                value={form.defaultModel}
                onValueChange={(v) => updateField('defaultModel', v)}
              >
                <SelectTrigger className="w-full text-sm">
                  <SelectValue placeholder="Select model..." />
                </SelectTrigger>
                <SelectContent>
                  {CODEX_SUPPORTED_MODELS.map((m) => (
                    <SelectItem key={m.id} value={m.id}>{m.label}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            ) : (
              <input
                type="text"
                value={form.defaultModel}
                onChange={(e) => updateField('defaultModel', e.target.value)}
                placeholder="e.g. gpt-4o, claude-sonnet-4-20250514"
                className="input w-full text-sm"
                maxLength={USER_PROVIDER_CONSTS.maxModelLength}
              />
            )}
          </FormField>

          {/* Endpoint (optional) — hidden for Codex (managed by backend) */}
          {!isCodex && (
            <FormField label="Endpoint (optional)" error={errors.endpoint}>
              <input
                type="text"
                value={form.endpoint}
                onChange={(e) => updateField('endpoint', e.target.value)}
                placeholder="https://api.openai.com/v1"
                className="input w-full text-sm"
              />
            </FormField>
          )}

          {/* Deployment Name (optional, Azure) */}
          {form.providerType === 'AzureOpenAI' && (
            <FormField label="Deployment Name">
              <input
                type="text"
                value={form.deploymentName}
                onChange={(e) => updateField('deploymentName', e.target.value)}
                placeholder="my-gpt4-deployment"
                className="input w-full text-sm"
                maxLength={USER_PROVIDER_CONSTS.maxDeploymentNameLength}
              />
            </FormField>
          )}

          {/* Set as default (new only) */}
          {!isEdit && (
            <label className="flex items-center gap-2 cursor-pointer">
              <input
                type="checkbox"
                checked={form.isDefault}
                onChange={(e) => updateField('isDefault', e.target.checked)}
                className="rounded border-border-subtle"
              />
              <span className="text-xs text-text-muted">Set as default provider</span>
            </label>
          )}
        </div>

        <DialogFooter>
          <Button variant="ghost" onClick={() => onOpenChange(false)} disabled={busy}>
            Cancel
          </Button>
          <Button onClick={handleSubmit} disabled={busy}>
            {busy ? 'Saving...' : isEdit ? 'Update' : 'Add Provider'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

// === Helpers ===

interface FormFieldProps {
  label: string
  error?: string
  children: React.ReactNode
}

const FormField: React.FC<FormFieldProps> = ({ label, error, children }) => (
  <div>
    <label className="text-xs font-mono text-text-muted block mb-1.5">{label}</label>
    {children}
    {error && <p className="mt-1 text-[10px] text-neon-red font-mono">{error}</p>}
  </div>
)

function isValidUrl(value: string): boolean {
  try {
    new URL(value)
    return true
  } catch {
    return false
  }
}
