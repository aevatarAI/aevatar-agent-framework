// ============================================================================
//  Delete Provider Confirmation Dialog
//  Warns about cascade effects (default promotion, session cleanup)
// ============================================================================

import React from 'react'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
} from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import { AlertTriangle } from 'lucide-react'
import type { UserLlmProviderDto } from '@/types/user-provider'

interface DeleteProviderConfirmProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  provider: UserLlmProviderDto | null
  onConfirm: () => Promise<void>
  busy: boolean
}

/** Confirmation dialog for deleting a user LLM provider. */
export const DeleteProviderConfirm: React.FC<DeleteProviderConfirmProps> = ({
  open,
  onOpenChange,
  provider,
  onConfirm,
  busy,
}) => {
  if (!provider) return null

  const isDefault = provider.isDefault
  const isCodex = provider.isCodexOAuth

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-sm" showCloseButton>
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2 text-neon-red">
            <AlertTriangle className="w-5 h-5" />
            Delete Provider
          </DialogTitle>
          <DialogDescription>
            This action cannot be undone.
          </DialogDescription>
        </DialogHeader>

        <div className="p-4 space-y-3">
          <p className="text-sm text-text-primary">
            Are you sure you want to delete{' '}
            <span className="font-semibold text-neon-cyan">{provider.name}</span>?
          </p>

          {/* Cascade warnings */}
          <div className="space-y-2">
            {isDefault && (
              <WarningBadge text="This is your default provider. The next oldest provider will be promoted to default." />
            )}
            {isCodex && (
              <WarningBadge text="This will also disconnect your Codex (ChatGPT) OAuth connection." />
            )}
            <WarningBadge text="Any session agent mappings using this provider will be removed." />
          </div>
        </div>

        <DialogFooter>
          <Button variant="ghost" onClick={() => onOpenChange(false)} disabled={busy}>
            Cancel
          </Button>
          <Button variant="destructive" onClick={onConfirm} disabled={busy}>
            {busy ? 'Deleting...' : 'Delete Provider'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

// === Warning Badge ===

const WarningBadge: React.FC<{ text: string }> = ({ text }) => (
  <div className="flex items-start gap-2 px-3 py-2 rounded-lg bg-neon-gold/5 border border-neon-gold/20">
    <AlertTriangle className="w-3.5 h-3.5 text-neon-gold flex-shrink-0 mt-0.5" />
    <span className="text-[11px] text-text-muted">{text}</span>
  </div>
)
