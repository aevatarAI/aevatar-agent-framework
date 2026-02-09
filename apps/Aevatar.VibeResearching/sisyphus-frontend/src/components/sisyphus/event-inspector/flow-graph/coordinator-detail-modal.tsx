// ============================================================
//  Coordinator Detail Modal - Shows Coordinator event details
//  Displays events sent by the coordinator (no worker_id)
// ============================================================

import React, { useMemo } from 'react'
import { createPortal } from 'react-dom'
import { motion, AnimatePresence } from 'framer-motion'
import { X, Cpu, Clock, ChevronRight, Send, Users, Flag } from 'lucide-react'
import { cn } from '@/lib/utils'
import type { ClassifiedEvent } from '@/types'

interface CoordinatorDetailModalProps {
  isOpen: boolean
  onClose: () => void
  events: ClassifiedEvent[]
  totalEventCount: number
}

const CoordinatorDetailModal: React.FC<CoordinatorDetailModalProps> = ({
  isOpen,
  onClose,
  events,
  totalEventCount,
}) => {
  // Filter coordinator events (events WITHOUT worker_id)
  const coordinatorEvents = useMemo(() => {
    return events.filter(event => {
      const workerId = event.raw.fields.worker_id as string | undefined
      // Coordinator events have no worker_id
      return !workerId
    }).sort((a, b) => {
      const timeA = a.raw.timestamp ? new Date(a.raw.timestamp).getTime() : 0
      const timeB = b.raw.timestamp ? new Date(b.raw.timestamp).getTime() : 0
      return timeB - timeA
    })
  }, [events])

  if (!isOpen) return null

  const modalContent = (
    <AnimatePresence>
      {isOpen && (
        <div className="fixed inset-0 z-[200] flex items-center justify-center">
          {/* Backdrop */}
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.15 }}
            className="absolute inset-0 bg-black/60 backdrop-blur-sm"
            onClick={onClose}
          />

          {/* Modal */}
          <motion.div
            initial={{ opacity: 0, scale: 0.95, y: 20 }}
            animate={{ opacity: 1, scale: 1, y: 0 }}
            exit={{ opacity: 0, scale: 0.95, y: 20 }}
            transition={{ type: 'spring', damping: 25, stiffness: 300 }}
            className="relative z-10 w-[90%] max-w-2xl max-h-[80vh] overflow-hidden
                       bg-bg-surface border border-border-subtle rounded-xl shadow-2xl"
          >
            {/* Header */}
            <div
              className="flex items-center justify-between px-5 py-4 border-b border-border-subtle"
              style={{ background: 'linear-gradient(135deg, rgba(0,240,255,0.1), transparent)' }}
            >
              <div className="flex items-center gap-3">
                <div className="w-10 h-10 rounded-lg flex items-center justify-center border-2 border-neon-cyan bg-neon-cyan/20">
                  <Cpu className="w-5 h-5 text-neon-cyan" />
                </div>
                <div>
                  <h3 className="text-sm font-mono font-semibold text-neon-cyan">
                    COORDINATOR
                  </h3>
                  <div className="flex items-center gap-3 text-[10px] text-text-muted font-mono mt-0.5">
                    <span className="flex items-center gap-1">
                      <Send className="w-3 h-3" />
                      {coordinatorEvents.length} sent
                    </span>
                    <span className="flex items-center gap-1">
                      <Users className="w-3 h-3" />
                      {totalEventCount} total
                    </span>
                  </div>
                </div>
              </div>
              <button
                onClick={onClose}
                className="p-1.5 rounded-lg hover:bg-white/5 transition-colors"
              >
                <X className="w-4 h-4 text-text-muted" />
              </button>
            </div>

            {/* Content */}
            <div className="p-5 overflow-y-auto max-h-[calc(80vh-80px)]">
              {/* Stats Summary */}
              <div className="grid grid-cols-3 gap-3 mb-4">
                <StatCard
                  label="Sent Events"
                  value={coordinatorEvents.length}
                  icon={<Send className="w-3.5 h-3.5" />}
                  color="text-neon-cyan"
                />
                <StatCard
                  label="Total Events"
                  value={totalEventCount}
                  icon={<Users className="w-3.5 h-3.5" />}
                  color="text-neon-purple"
                />
                <StatCard
                  label="Worker Events"
                  value={totalEventCount - coordinatorEvents.length}
                  icon={<Flag className="w-3.5 h-3.5" />}
                  color="text-neon-green"
                />
              </div>

              {/* Events List */}
              <div className="space-y-2">
                <div className="flex items-center gap-2 text-xs font-mono text-text-muted mb-3">
                  <Clock className="w-3.5 h-3.5" />
                  <span>COORDINATOR EVENTS ({coordinatorEvents.length})</span>
                </div>

                {coordinatorEvents.length === 0 ? (
                  <div className="text-center py-8 text-text-muted text-sm">
                    No coordinator events found
                  </div>
                ) : (
                  <div className="space-y-2">
                    {coordinatorEvents.map((event) => (
                      <EventListItem key={event.id} event={event} />
                    ))}
                  </div>
                )}
              </div>
            </div>
          </motion.div>
        </div>
      )}
    </AnimatePresence>
  )

  return createPortal(modalContent, document.body)
}

// ============================================================
//  Stat Card Component
// ============================================================
const StatCard: React.FC<{
  label: string
  value: number
  icon: React.ReactNode
  color: string
}> = ({ label, value, icon, color }) => (
  <div className="bg-bg-elevated/50 rounded-lg p-3 border border-border-subtle">
    <div className={cn("flex items-center gap-1.5 text-[10px] font-mono mb-1", color)}>
      {icon}
      {label}
    </div>
    <div className="text-lg font-mono font-semibold text-text-primary">
      {value}
    </div>
  </div>
)

// ============================================================
//  Event List Item Component
// ============================================================
const EventListItem: React.FC<{ event: ClassifiedEvent }> = ({ event }) => {
  const [isExpanded, setIsExpanded] = React.useState(false)

  const getCategoryColor = (category: string) => {
    switch (category) {
      case 'consensus': return 'text-neon-cyan bg-neon-cyan/10 border-neon-cyan/30'
      case 'vote': return 'text-neon-purple bg-neon-purple/10 border-neon-purple/30'
      case 'red_flag': return 'text-neon-red bg-neon-red/10 border-neon-red/30'
      case 'task_starting': return 'text-neon-green bg-neon-green/10 border-neon-green/30'
      default: return 'text-text-muted bg-white/5 border-border-subtle'
    }
  }

  const getPreview = () => {
    // Try different sources for preview text
    return event.message ||
           (event.raw.fields.assistant_response as string | undefined) ||
           event.raw.phase ||
           'No content'
  }

  const formatTime = (timestamp: string | number | undefined) => {
    if (!timestamp) return '--:--'
    const date = new Date(timestamp)
    return date.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
  }

  return (
    <div
      className="bg-bg-elevated/30 rounded-lg border border-border-subtle overflow-hidden
                 hover:border-neon-cyan/30 transition-colors cursor-pointer"
      onClick={() => setIsExpanded(!isExpanded)}
    >
      {/* Header */}
      <div className="flex items-center justify-between px-3 py-2">
        <div className="flex items-center gap-2">
          <ChevronRight
            className={cn(
              "w-3.5 h-3.5 text-text-muted transition-transform",
              isExpanded && "rotate-90"
            )}
          />
          <span className={cn(
            "text-[9px] font-mono px-1.5 py-0.5 rounded border",
            getCategoryColor(event.category)
          )}>
            {event.category.toUpperCase()}
          </span>
          <span className="text-[10px] font-mono text-text-muted">
            {event.raw.phase || event.id.slice(0, 8)}
          </span>
        </div>
        <span className="text-[9px] font-mono text-text-muted">
          {formatTime(event.raw.timestamp)}
        </span>
      </div>

      {/* Preview (always visible) */}
      <div className="px-3 pb-2">
        <p className="text-[11px] text-text-secondary line-clamp-2">
          {getPreview()}
        </p>
      </div>

      {/* Expanded Details */}
      <AnimatePresence>
        {isExpanded && (
          <motion.div
            initial={{ height: 0, opacity: 0 }}
            animate={{ height: 'auto', opacity: 1 }}
            exit={{ height: 0, opacity: 0 }}
            transition={{ duration: 0.2 }}
            className="border-t border-border-subtle"
          >
            <div className="p-3 space-y-2">
              {/* Event ID */}
              <div className="text-[9px] font-mono">
                <span className="text-text-muted">ID:</span>{' '}
                <span className="text-text-secondary">{event.id}</span>
              </div>
              
              {/* Fields */}
              {Object.entries(event.raw.fields).length > 0 && (
                <div className="text-[9px] font-mono">
                  <span className="text-text-muted mb-1 block">Fields:</span>
                  <div className="bg-bg-base rounded p-2 max-h-32 overflow-y-auto">
                    <pre className="text-text-secondary whitespace-pre-wrap break-all">
                      {JSON.stringify(event.raw.fields, null, 2)}
                    </pre>
                  </div>
                </div>
              )}

              {/* Message */}
              {event.message && (
                <div className="text-[9px] font-mono">
                  <span className="text-text-muted mb-1 block">Message:</span>
                  <div className="bg-bg-base rounded p-2 max-h-32 overflow-y-auto">
                    <p className="text-text-secondary whitespace-pre-wrap">
                      {event.message}
                    </p>
                  </div>
                </div>
              )}
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  )
}

export default CoordinatorDetailModal
