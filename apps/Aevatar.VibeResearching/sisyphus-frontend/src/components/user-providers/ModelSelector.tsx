// ============================================================================
//  Model Selector - Fetches and displays available models from a provider
// ============================================================================

import React, { useState, useCallback } from 'react'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { RefreshCw, Loader2 } from 'lucide-react'
import { listUserProviderModels } from '@/lib/axiom-client/user-provider'
import type { ProviderModelItem } from '@/types/user-provider'

interface ModelSelectorProps {
  providerId: string
  value: string
  onChange: (model: string) => void
  placeholder?: string
  className?: string
}

/** Dropdown that fetches available models from a user provider. */
export const ModelSelector: React.FC<ModelSelectorProps> = ({
  providerId,
  value,
  onChange,
  placeholder = 'Select model...',
  className,
}) => {
  const [models, setModels] = useState<ProviderModelItem[]>([])
  const [loading, setLoading] = useState(false)
  const [fetched, setFetched] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const handleFetch = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const res = await listUserProviderModels(providerId)
      const items = Array.isArray(res?.models) ? res.models : []
      setModels(items)
      setFetched(true)
    } catch (e) {
      const err = e as Error
      setError(err.message || 'Failed to fetch models')
    }
    setLoading(false)
  }, [providerId])

  // Show text input if models not yet fetched
  if (!fetched || models.length === 0) {
    return (
      <div className={cn('flex gap-2', className)}>
        <input
          type="text"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          placeholder={placeholder}
          className="input flex-1 text-sm"
        />
        <Button
          variant="ghost"
          size="sm"
          onClick={handleFetch}
          disabled={loading}
          title="Fetch available models"
        >
          {loading ? (
            <Loader2 className="w-3.5 h-3.5 animate-spin" />
          ) : (
            <RefreshCw className="w-3.5 h-3.5" />
          )}
          <span className="ml-1">{fetched ? 'Retry' : 'Fetch'}</span>
        </Button>
        {error && (
          <span className="text-[10px] font-mono text-neon-red self-center">{error}</span>
        )}
      </div>
    )
  }

  return (
    <div className={cn('flex gap-2', className)}>
      <Select value={value || undefined} onValueChange={onChange}>
        <SelectTrigger className="flex-1 text-sm">
          <SelectValue placeholder={placeholder} />
        </SelectTrigger>
        <SelectContent>
          {models.map((m) => (
            <SelectItem key={m.id} value={m.id}>
              <span>{m.name || m.id}</span>
              {m.ownedBy && (
                <span className="ml-2 text-[10px] text-text-dimmed">({m.ownedBy})</span>
              )}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
      <Button
        variant="ghost"
        size="sm"
        onClick={handleFetch}
        disabled={loading}
        title="Refresh models"
      >
        {loading ? (
          <Loader2 className="w-3.5 h-3.5 animate-spin" />
        ) : (
          <RefreshCw className="w-3.5 h-3.5" />
        )}
      </Button>
    </div>
  )
}
