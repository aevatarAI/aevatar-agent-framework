// ============================================================================
//  User Provider List - Displays the user's configured LLM providers
//  Cyberpunk-styled card list with status indicators and default badge
// ============================================================================

import React from 'react'
import { cn } from '@/lib/utils'
import { Star, Plug, Trash2, Pencil, Zap } from 'lucide-react'
import { Button } from '@/components/ui/button'
import type { UserLlmProviderDto } from '@/types/user-provider'

interface ProviderListProps {
  providers: UserLlmProviderDto[]
  loading: boolean
  onEdit: (provider: UserLlmProviderDto) => void
  onDelete: (provider: UserLlmProviderDto) => void
  onSetDefault: (provider: UserLlmProviderDto) => void
  onTest: (provider: UserLlmProviderDto) => void
}

/** Displays a list of user-configured LLM providers with actions. */
export const ProviderList: React.FC<ProviderListProps> = ({
  providers,
  loading,
  onEdit,
  onDelete,
  onSetDefault,
  onTest,
}) => {
  if (loading) {
    return (
      <div className="space-y-3">
        {[1, 2, 3].map((i) => (
          <div
            key={i}
            className="card p-4 animate-pulse"
          >
            <div className="h-4 bg-surface-elevated rounded w-1/3 mb-2" />
            <div className="h-3 bg-surface-elevated rounded w-1/2" />
          </div>
        ))}
      </div>
    )
  }

  if (providers.length === 0) {
    return (
      <div className="card p-8 text-center">
        <Plug className="w-10 h-10 mx-auto text-text-dimmed mb-3" />
        <h3 className="text-sm font-display font-semibold text-text-primary mb-1">
          No Providers Configured
        </h3>
        <p className="text-xs text-text-muted max-w-xs mx-auto">
          Add your own LLM API keys to use custom providers instead of the platform default.
        </p>
      </div>
    )
  }

  return (
    <div className="space-y-2">
      {providers.map((provider) => (
        <ProviderCard
          key={provider.id}
          provider={provider}
          onEdit={onEdit}
          onDelete={onDelete}
          onSetDefault={onSetDefault}
          onTest={onTest}
        />
      ))}
    </div>
  )
}

// === Provider Card ===

interface ProviderCardProps {
  provider: UserLlmProviderDto
  onEdit: (provider: UserLlmProviderDto) => void
  onDelete: (provider: UserLlmProviderDto) => void
  onSetDefault: (provider: UserLlmProviderDto) => void
  onTest: (provider: UserLlmProviderDto) => void
}

const ProviderCard: React.FC<ProviderCardProps> = ({
  provider,
  onEdit,
  onDelete,
  onSetDefault,
  onTest,
}) => (
  <div
    className={cn(
      'card p-4 transition-all duration-200 hover:border-border-strong group',
      provider.isDefault && 'border-neon-cyan/40 bg-neon-cyan/5'
    )}
  >
    <div className="flex items-start justify-between gap-3">
      {/* Left: Info */}
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-2 mb-1">
          <span className="text-sm font-display font-semibold text-text-primary truncate">
            {provider.name}
          </span>
          {provider.isDefault && (
            <span className="flex items-center gap-1 text-[9px] font-mono text-neon-gold bg-neon-gold/10 px-1.5 py-0.5 rounded">
              <Star className="w-2.5 h-2.5" />
              default
            </span>
          )}
          {provider.isCodexOAuth && (
            <span className="text-[9px] font-mono text-neon-purple bg-neon-purple/10 px-1.5 py-0.5 rounded">
              Codex
            </span>
          )}
        </div>
        <div className="flex items-center gap-3 text-[11px] font-mono text-text-muted">
          <span className="px-1.5 py-0.5 rounded bg-surface-elevated">
            {provider.providerType}
          </span>
          <span>{provider.defaultModel}</span>
        </div>
        {provider.maskedApiKey && (
          <div className="mt-1 text-[10px] font-mono text-text-dimmed">
            {provider.maskedApiKey}
          </div>
        )}
      </div>

      {/* Right: Actions */}
      <div className="flex items-center gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
        <Button
          variant="ghost"
          size="icon"
          className="h-7 w-7"
          onClick={() => onTest(provider)}
          title="Test connection"
        >
          <Zap className="w-3.5 h-3.5" />
        </Button>
        {!provider.isDefault && (
          <Button
            variant="ghost"
            size="icon"
            className="h-7 w-7"
            onClick={() => onSetDefault(provider)}
            title="Set as default"
          >
            <Star className="w-3.5 h-3.5" />
          </Button>
        )}
        <Button
          variant="ghost"
          size="icon"
          className="h-7 w-7"
          onClick={() => onEdit(provider)}
          title="Edit"
        >
          <Pencil className="w-3.5 h-3.5" />
        </Button>
        <Button
          variant="ghost"
          size="icon"
          className="h-7 w-7 text-neon-red hover:bg-neon-red/10"
          onClick={() => onDelete(provider)}
          title="Delete"
        >
          <Trash2 className="w-3.5 h-3.5" />
        </Button>
      </div>
    </div>
  </div>
)
