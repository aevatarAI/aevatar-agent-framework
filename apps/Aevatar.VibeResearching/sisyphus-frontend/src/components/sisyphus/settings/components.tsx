// ============================================================================
//  Settings Panel - Shared Helper Components
// ============================================================================

import React from 'react'
import { cn } from '@/lib/utils'
import type { LlmProvider } from '@/types'

// === Stat Box ===

interface StatBoxProps {
  label: string
  value: number
  accent?: 'cyan' | 'gold'
}

export const StatBox: React.FC<StatBoxProps> = ({ label, value, accent }) => (
  <div className="text-center">
    <div
      className={cn(
        'text-2xl font-mono font-bold tabular-nums',
        accent === 'cyan'
          ? 'text-neon-cyan'
          : accent === 'gold'
            ? 'text-neon-gold'
            : 'text-text-primary'
      )}
    >
      {value}
    </div>
    <div className="text-[10px] text-text-dimmed uppercase tracking-wider">{label}</div>
  </div>
)

// === Provider Group ===

interface ProviderGroupProps {
  label: string
  providers: LlmProvider[]
  selected: string | null
  onSelect: (name: string) => void
  defaultProvider?: string
}

export const ProviderGroup: React.FC<ProviderGroupProps> = ({
  label,
  providers,
  selected,
  onSelect,
  defaultProvider,
}) => (
  <div>
    <div className="px-3 py-1.5 text-[10px] font-mono text-text-dimmed uppercase tracking-wider bg-surface-elevated">
      {label} ({providers.length})
    </div>
    {providers.map((p) => (
      <button
        key={p.id}
        onClick={() => onSelect(p.id)}
        className={cn(
          'w-full px-3 py-2.5 text-left transition-colors',
          selected === p.id
            ? 'bg-neon-cyan/10 border-l-2 border-neon-cyan'
            : 'hover:bg-surface-elevated'
        )}
      >
        <div className="flex items-center gap-2">
          <span className="text-sm font-mono text-text-primary">{p.displayName || p.id}</span>
          {p.id === defaultProvider && (
            <span className="text-[9px] font-mono text-neon-gold bg-neon-gold/10 px-1.5 py-0.5 rounded">
              default
            </span>
          )}
          {p.apiKeyConfigured && <span className="text-[9px] font-mono text-neon-green">✓</span>}
        </div>
      </button>
    ))}
  </div>
)
