// ============================================================
//  Tool Node - External tool execution node
//  Supports detailMode for simplified/detailed display
// ============================================================

import React, { memo } from 'react'
import { Handle, Position } from '@xyflow/react'
import type { NodeProps } from '@xyflow/react'
import { motion } from 'framer-motion'
import { Search, Terminal, Globe, FileText, Database, Wrench } from 'lucide-react'
import { cn } from '@/lib/utils'

interface ToolNodeData {
  label: string
  callCount: number
  status: 'idle' | 'running' | 'error'
  detailMode?: boolean
}

// Tool icon mapping
const TOOL_ICONS: Record<string, React.ReactNode> = {
  web_search: <Search className="w-4 h-4" />,
  code_execute: <Terminal className="w-4 h-4" />,
  api_call: <Globe className="w-4 h-4" />,
  file_read: <FileText className="w-4 h-4" />,
  file_write: <FileText className="w-4 h-4" />,
  database_query: <Database className="w-4 h-4" />,
}

// Compact icons
const TOOL_ICONS_COMPACT: Record<string, React.ReactNode> = {
  web_search: <Search className="w-3 h-3" />,
  code_execute: <Terminal className="w-3 h-3" />,
  api_call: <Globe className="w-3 h-3" />,
  file_read: <FileText className="w-3 h-3" />,
  file_write: <FileText className="w-3 h-3" />,
  database_query: <Database className="w-3 h-3" />,
}

const ToolNode: React.FC<NodeProps> = ({ data, selected }) => {
  const nodeData = data as unknown as ToolNodeData
  const { label, callCount, status } = nodeData
  const detailMode = nodeData.detailMode !== false // Default to true
  const icon = detailMode 
    ? (TOOL_ICONS[label] || <Wrench className="w-4 h-4" />)
    : (TOOL_ICONS_COMPACT[label] || <Wrench className="w-3 h-3" />)
  const isHighUsage = callCount > 5

  return (
    <div className="relative">
      {/* High usage glow - only in detail mode */}
      {isHighUsage && detailMode && (
        <motion.div
          className="absolute -inset-2 rounded-lg bg-neon-rose/20 blur-md"
          animate={{ opacity: [0.3, 0.5, 0.3] }}
          transition={{ repeat: Infinity, duration: 1.5 }}
        />
      )}

      {/* Main node */}
      <motion.div
        className={cn(
          "relative rounded-lg border flex flex-col items-center justify-center gap-0.5",
          "bg-gradient-to-br from-bg-surface to-bg-elevated",
          // Size based on detail mode
          detailMode ? "w-28 h-14" : "w-8 h-8 rounded-full",
          selected
            ? "border-neon-rose shadow-[0_0_15px_rgba(244,63,94,0.5)]"
            : isHighUsage
              ? "border-neon-rose/70 border-2"
              : "border-neon-rose/40",
          status === 'running' && "animate-pulse",
          status === 'error' && "border-red-500"
        )}
        whileHover={{
          scale: 1.05,
          boxShadow: '0 0 20px rgba(244,63,94,0.4)',
        }}
        transition={{ duration: 0.2 }}
      >
        {/* Icon */}
        <div className="text-neon-rose">{icon}</div>

        {/* Label - only in detail mode */}
        {detailMode && (
          <span className="text-[9px] font-mono text-neon-rose tracking-wider">
            {label.replace(/_/g, ' ').toUpperCase()}
          </span>
        )}
      </motion.div>

      {/* Call count badge */}
      {callCount > 0 && (
        <motion.div
          className={cn(
            "absolute rounded-full flex items-center justify-center",
            "bg-neon-rose text-bg-base font-mono font-bold",
            "border-2 border-bg-base",
            detailMode 
              ? "-top-1.5 -right-1.5 min-w-[18px] h-[18px] px-1 text-[9px]"
              : "-top-1 -right-1 min-w-[14px] h-[14px] px-0.5 text-[7px]"
          )}
          initial={{ scale: 0 }}
          animate={{ scale: 1 }}
          transition={{ type: 'spring', stiffness: 400 }}
        >
          {callCount}
        </motion.div>
      )}

      {/* Handle */}
      <Handle
        type="target"
        position={Position.Top}
        className={cn(
          "!bg-neon-rose !border-2 !border-bg-base",
          detailMode ? "!w-2.5 !h-2.5" : "!w-1.5 !h-1.5"
        )}
      />
    </div>
  )
}

export default memo(ToolNode)
