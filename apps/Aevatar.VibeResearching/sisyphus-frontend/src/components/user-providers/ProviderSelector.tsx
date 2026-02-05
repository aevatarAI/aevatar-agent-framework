// ============================================================================
//  Provider Selector Dropdown
//  Merged dropdown of user + platform providers with namespace labels
//  Used in the agent-provider mapping panel
// ============================================================================

import React from 'react'
import { cn } from '@/lib/utils'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Star } from 'lucide-react'
import type { AvailableProviderDto } from '@/types/user-provider'

interface ProviderSelectorProps {
  value: string
  onChange: (namespace: string) => void
  providers: AvailableProviderDto[]
  disabled?: boolean
  className?: string
}

/**
 * Dropdown selector showing both user and platform providers.
 * Values are provider namespaces (e.g., "user:abc123" or "platform:openai-gpt4").
 * Includes a "Default (auto)" option that clears the mapping.
 */
export const ProviderSelector: React.FC<ProviderSelectorProps> = ({
  value,
  onChange,
  providers,
  disabled = false,
  className,
}) => {
  const userProviders = providers.filter((p) => p.source === 'user' || p.source === 'codex')
  const platformProviders = providers.filter((p) => p.source === 'platform')

  return (
    <Select
      value={value || '__auto__'}
      onValueChange={(v) => onChange(v === '__auto__' ? '' : v)}
      disabled={disabled}
    >
      <SelectTrigger className={cn('text-sm', className)}>
        <SelectValue placeholder="Select provider..." />
      </SelectTrigger>
      <SelectContent>
        {/* Auto/Default option */}
        <SelectItem value="__auto__">
          <span className="text-text-muted">Default (auto-resolve)</span>
        </SelectItem>

        {/* User providers section */}
        {userProviders.length > 0 && (
          <>
            <div className="px-3 py-1.5 text-[9px] font-mono text-text-dimmed uppercase tracking-wider border-t border-border-subtle mt-1">
              Your Providers
            </div>
            {userProviders.map((p) => (
              <SelectItem key={p.namespace} value={p.namespace}>
                <div className="flex items-center gap-2">
                  <SourceBadge source={p.source} />
                  <span>{p.name}</span>
                  <span className="text-[10px] text-text-dimmed">({p.defaultModel})</span>
                  {p.isDefault && <Star className="w-2.5 h-2.5 text-neon-gold" />}
                </div>
              </SelectItem>
            ))}
          </>
        )}

        {/* Platform providers section */}
        {platformProviders.length > 0 && (
          <>
            <div className="px-3 py-1.5 text-[9px] font-mono text-text-dimmed uppercase tracking-wider border-t border-border-subtle mt-1">
              Platform Providers
            </div>
            {platformProviders.map((p) => (
              <SelectItem key={p.namespace} value={p.namespace}>
                <div className="flex items-center gap-2">
                  <SourceBadge source={p.source} />
                  <span>{p.name}</span>
                  <span className="text-[10px] text-text-dimmed">({p.defaultModel})</span>
                </div>
              </SelectItem>
            ))}
          </>
        )}
      </SelectContent>
    </Select>
  )
}

// === Source Badge ===

const SourceBadge: React.FC<{ source: string }> = ({ source }) => {
  const config = SOURCE_BADGE_CONFIG[source] ?? SOURCE_BADGE_CONFIG['platform']
  return (
    <span
      className={cn(
        'text-[8px] font-mono px-1 py-0.5 rounded flex-shrink-0',
        config.className
      )}
    >
      {config.label}
    </span>
  )
}

const SOURCE_BADGE_CONFIG: Record<string, { label: string; className: string }> = {
  user: { label: 'USER', className: 'bg-neon-cyan/10 text-neon-cyan' },
  codex: { label: 'CODEX', className: 'bg-neon-purple/10 text-neon-purple' },
  platform: { label: 'SYS', className: 'bg-surface-elevated text-text-dimmed' },
}
