// ============================================================================
//  Settings Panel - Advanced Tab (Raw Secrets)
// ============================================================================

import React, { useState, useCallback } from 'react'
import { cn } from '@/lib/utils'
import { setSecret, removeSecret } from '@/lib/axiom-client'

export const AdvancedTab: React.FC = () => {
  const [key, setKey] = useState('')
  const [value, setValue] = useState('')
  const [shown, setShown] = useState(false)
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<{ kind: 'ok' | 'err'; text: string } | null>(null)

  const handleSave = useCallback(async () => {
    if (!key.trim() || !value.trim() || busy) return
    setBusy(true)
    setMsg(null)
    try {
      await setSecret(key.trim(), value.trim())
      setMsg({ kind: 'ok', text: `Saved: ${key}` })
      setKey('')
      setValue('')
    } catch (e) {
      const err = e as Error
      setMsg({ kind: 'err', text: err?.message || 'Save failed' })
    }
    setBusy(false)
  }, [key, value, busy])

  const handleRemove = useCallback(async () => {
    if (!key.trim() || busy) return
    setBusy(true)
    setMsg(null)
    try {
      await removeSecret(key.trim())
      setMsg({ kind: 'ok', text: `Removed: ${key}` })
      setKey('')
      setValue('')
    } catch (e) {
      const err = e as Error
      setMsg({ kind: 'err', text: err?.message || 'Remove failed' })
    }
    setBusy(false)
  }, [key, busy])

  return (
    <div className="max-w-2xl">
      <div className="card p-6 space-y-6">
        <div>
          <h3 className="text-lg font-display font-semibold text-neon-gold">Advanced Settings</h3>
          <p className="text-xs text-text-muted mt-1">
            Directly manage encrypted user secrets. Use with caution.
          </p>
        </div>

        <div>
          <label className="text-xs font-mono text-text-muted block mb-1.5">Secret Key</label>
          <input
            type="text"
            value={key}
            onChange={(e) => setKey(e.target.value)}
            placeholder="e.g. OpenAI:ApiKey or SkillsMP:ApiKey"
            className="input w-full font-mono"
          />
        </div>

        <div>
          <label className="text-xs font-mono text-text-muted block mb-1.5">Secret Value</label>
          <div className="flex gap-2">
            <input
              type={shown ? 'text' : 'password'}
              value={value}
              onChange={(e) => setValue(e.target.value)}
              placeholder="Enter secret value..."
              className="input flex-1 font-mono"
            />
            <button onClick={() => setShown(!shown)} className="btn-ghost px-3 text-xs">
              {shown ? 'Hide' : 'Show'}
            </button>
          </div>
        </div>

        {msg && (
          <div
            className={cn(
              'px-3 py-2 rounded-lg text-xs font-mono',
              msg.kind === 'ok'
                ? 'bg-neon-green/10 text-neon-green border border-neon-green/30'
                : 'bg-neon-red/10 text-neon-red border border-neon-red/30'
            )}
          >
            {msg.text}
          </div>
        )}

        <div className="flex gap-3">
          <button
            onClick={handleSave}
            disabled={busy || !key.trim() || !value.trim()}
            className="btn-primary px-6"
          >
            Save Secret
          </button>
          <button
            onClick={handleRemove}
            disabled={busy || !key.trim()}
            className="btn-ghost px-4 text-neon-red hover:bg-neon-red/10"
          >
            Remove Secret
          </button>
        </div>

        <div className="pt-4 border-t border-border-subtle">
          <p className="text-[10px] text-text-dimmed font-mono">
            Secrets are stored encrypted in user-specific storage. Keys follow pattern: Provider:Type
            (e.g. OpenAI:ApiKey, Claude:Endpoint).
          </p>
        </div>
      </div>
    </div>
  )
}
