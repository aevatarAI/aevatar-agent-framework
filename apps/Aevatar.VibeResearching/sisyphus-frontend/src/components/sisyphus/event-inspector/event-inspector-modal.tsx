// ============================================================
//  Event Inspector Drawer - Right-side sliding panel
//  Displays Timeline View and Flow Graph View with Tab switching
// ============================================================

import React, { useState, useCallback, useEffect, useRef } from 'react'
import { createPortal } from 'react-dom'
import { X, Activity, GitBranch, ChevronLeft, GripVertical } from 'lucide-react'
import { motion, AnimatePresence } from 'framer-motion'
import { cn } from '@/lib/utils'
import { useSisyphusStore } from '@/store/sisyphus-store'
import TimelineView from './timeline-view'
import AgentFlowGraph from './flow-graph/agent-flow-graph'
import type { ClassifiedEvent, VotingStatus } from '@/types'

// ============================================================
//  Resize Constants
// ============================================================
const MIN_WIDTH = 400
const MAX_WIDTH_RATIO = 0.9  // 90% of viewport
const DEFAULT_WIDTH = 896   // ~max-w-4xl

interface EventInspectorModalProps {
  isOpen: boolean
  onClose: () => void
  sessionId: string
  events?: ClassifiedEvent[]
  votingStatus?: VotingStatus | null
}

type TabId = 'flow-graph' | 'timeline'

const EventInspectorModal: React.FC<EventInspectorModalProps> = ({
  isOpen,
  onClose,
  sessionId,
  events: propEvents = [],
  votingStatus: propVotingStatus = null,
}) => {
  // Default to Flow Graph tab
  const [activeTab, setActiveTab] = useState<TabId>('flow-graph')
  
  // ============================================================
  //  Resizable Drawer State
  // ============================================================
  const [drawerWidth, setDrawerWidth] = useState(DEFAULT_WIDTH)
  const [isResizing, setIsResizing] = useState(false)
  const resizeRef = useRef<{ startX: number; startWidth: number } | null>(null)
  
  // Subscribe to store for real-time workflow event updates
  const storeEvents = useSisyphusStore((s) => s.workflowEvents)
  const storeVotingStatus = useSisyphusStore((s) => s.votingStatus)
  
  // Prefer store data (real-time updates), fallback to props
  const events = storeEvents.length > 0 ? storeEvents : propEvents
  const votingStatus = storeVotingStatus || propVotingStatus

  // ============================================================
  //  Resize Handlers (SES-safe implementation)
  // ============================================================
  const handleResizeStart = useCallback((e: React.MouseEvent) => {
    e.preventDefault()
    e.stopPropagation()
    resizeRef.current = { startX: e.clientX, startWidth: drawerWidth }
    setIsResizing(true)
  }, [drawerWidth])

  const handleResizeMove = useCallback((e: MouseEvent) => {
    if (!resizeRef.current) return
    const maxWidth = window.innerWidth * MAX_WIDTH_RATIO
    // Dragging left (negative delta) = increase width
    const delta = resizeRef.current.startX - e.clientX
    const newWidth = Math.min(maxWidth, Math.max(MIN_WIDTH, resizeRef.current.startWidth + delta))
    setDrawerWidth(newWidth)
  }, [])

  const handleResizeEnd = useCallback(() => {
    setIsResizing(false)
    resizeRef.current = null
  }, [])

  useEffect(() => {
    if (!isResizing) return

    // Use passive: false to allow preventDefault if needed
    window.addEventListener('mousemove', handleResizeMove)
    window.addEventListener('mouseup', handleResizeEnd)

    return () => {
      window.removeEventListener('mousemove', handleResizeMove)
      window.removeEventListener('mouseup', handleResizeEnd)
    }
  }, [isResizing, handleResizeMove, handleResizeEnd])

  // Handle escape key
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && isOpen) {
        onClose()
      }
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [isOpen, onClose])

  // Prevent body scroll when drawer is open
  useEffect(() => {
    if (isOpen) {
      document.body.style.overflow = 'hidden'
    } else {
      document.body.style.overflow = ''
    }
    return () => {
      document.body.style.overflow = ''
    }
  }, [isOpen])

  const handleBackdropClick = useCallback((e: React.MouseEvent) => {
    if (e.target === e.currentTarget) {
      onClose()
    }
  }, [onClose])

  if (!isOpen) return null

  const drawerContent = (
    <AnimatePresence>
      {isOpen && (
        <>
          {/* Backdrop */}
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.2 }}
            className="fixed inset-0 z-[100] bg-black/40 backdrop-blur-sm"
            onClick={handleBackdropClick}
          />

          {/* Drawer */}
          <motion.div
            initial={{ x: '100%' }}
            animate={{ x: 0 }}
            exit={{ x: '100%' }}
            transition={{ type: 'spring', damping: 30, stiffness: 300 }}
            style={{ width: drawerWidth }}
            className={cn(
              "fixed right-0 top-0 bottom-0 z-[101] bg-[#0a0a12] border-l border-border-subtle shadow-2xl flex flex-col",
              isResizing && "select-none"
            )}
          >
            {/* Resize Handle - Left Edge */}
            <div
              onMouseDown={handleResizeStart}
              className={cn(
                "absolute left-0 top-0 bottom-0 w-2 z-10 group",
                "cursor-ew-resize select-none",
                "hover:bg-neon-cyan/20 transition-colors",
                isResizing && "bg-neon-cyan/40"
              )}
            >
              {/* Visual grip indicator */}
              <div className={cn(
                "absolute left-0 top-1/2 -translate-y-1/2 w-4 h-10 -ml-1",
                "flex items-center justify-center rounded",
                "bg-bg-elevated border border-border-subtle",
                "opacity-0 group-hover:opacity-100 transition-opacity duration-150",
                isResizing && "opacity-100 bg-neon-cyan/20 border-neon-cyan/40"
              )}>
                <GripVertical className="w-3 h-3 text-text-muted" />
              </div>
            </div>
            {/* Header */}
            <div className="flex-shrink-0 flex items-center justify-between px-4 py-3 border-b border-border-subtle bg-gradient-to-r from-neon-cyan/5 to-transparent">
              {/* Left: Close + Title */}
              <div className="flex items-center gap-3">
                <button
                  onClick={onClose}
                  className="p-1.5 rounded-lg text-text-muted hover:text-neon-cyan hover:bg-neon-cyan/10 transition-colors"
                  aria-label="Close"
                >
                  <ChevronLeft className="w-5 h-5" />
                </button>
                
                <div className="relative">
                  <div className="absolute inset-0 rounded-lg bg-neon-cyan blur-md opacity-30 animate-pulse" />
                  <div className="relative flex h-8 w-8 items-center justify-center rounded-lg bg-neon-cyan/20 border border-neon-cyan/50">
                    <Activity className="h-4 w-4 text-neon-cyan" />
                  </div>
                </div>
                
                <div>
                  <h2 className="text-sm font-display font-semibold text-neon-cyan tracking-wide">
                    EVENT INSPECTOR
                  </h2>
                  <p className="text-[9px] text-text-muted font-mono">
                    {sessionId.slice(0, 8)}...
                  </p>
                </div>
              </div>

              {/* Tabs */}
              <div className="flex items-center gap-1 bg-bg-elevated rounded-lg p-1">
                <TabButton
                  active={activeTab === 'flow-graph'}
                  onClick={() => setActiveTab('flow-graph')}
                  icon={<GitBranch className="w-3.5 h-3.5" />}
                  label="Flow Graph"
                />
                <TabButton
                  active={activeTab === 'timeline'}
                  onClick={() => setActiveTab('timeline')}
                  icon={<Activity className="w-3.5 h-3.5" />}
                  label="Timeline"
                />
              </div>

              {/* Close button */}
              <button
                onClick={onClose}
                className="p-1.5 rounded-lg text-text-muted hover:text-text-primary hover:bg-bg-elevated transition-colors"
                aria-label="Close"
              >
                <X className="w-4 h-4" />
              </button>
            </div>

            {/* Content */}
            <div className="flex-1 overflow-hidden">
              <AnimatePresence mode="wait">
                {activeTab === 'flow-graph' ? (
                  <motion.div
                    key="flow-graph"
                    initial={{ opacity: 0 }}
                    animate={{ opacity: 1 }}
                    exit={{ opacity: 0 }}
                    transition={{ duration: 0.15 }}
                    className="h-full"
                  >
                    <AgentFlowGraph
                      sessionId={sessionId}
                      events={events}
                      votingStatus={votingStatus}
                    />
                  </motion.div>
                ) : (
                  <motion.div
                    key="timeline"
                    initial={{ opacity: 0 }}
                    animate={{ opacity: 1 }}
                    exit={{ opacity: 0 }}
                    transition={{ duration: 0.15 }}
                    className="h-full"
                  >
                    <TimelineView
                      events={events}
                      sessionId={sessionId}
                    />
                  </motion.div>
                )}
              </AnimatePresence>
            </div>
          </motion.div>
        </>
      )}
    </AnimatePresence>
  )

  return createPortal(drawerContent, document.body)
}

// Tab Button Component
interface TabButtonProps {
  active: boolean
  onClick: () => void
  icon: React.ReactNode
  label: string
}

const TabButton: React.FC<TabButtonProps> = ({ active, onClick, icon, label }) => (
  <button
    onClick={onClick}
    className={cn(
      "flex items-center gap-1.5 px-3 py-1.5 rounded-md text-xs font-mono transition-all",
      active
        ? "bg-neon-cyan/20 text-neon-cyan border border-neon-cyan/40"
        : "text-text-muted hover:text-text-primary hover:bg-bg-surface"
    )}
  >
    {icon}
    <span>{label}</span>
  </button>
)

export default EventInspectorModal
