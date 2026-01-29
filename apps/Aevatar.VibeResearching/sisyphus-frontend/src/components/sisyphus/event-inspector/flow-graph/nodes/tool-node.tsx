// ============================================================
//  Tool Node - External tool execution node
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

const ToolNode: React.FC<NodeProps> = ({ data, selected }) => {
  const nodeData = data as unknown as ToolNodeData
  const { label, callCount, status } = nodeData
  const icon = TOOL_ICONS[label] || <Wrench className="w-4 h-4" />
  const isHighUsage = callCount > 5

  return (
    <div className="relative">
      {/* High usage glow */}
      {isHighUsage && (
        <motion.div
          className="absolute -inset-2 rounded-lg bg-neon-rose/20 blur-md"
          animate={{ opacity: [0.3, 0.5, 0.3] }}
          transition={{ repeat: Infinity, duration: 1.5 }}
        />
      )}

      {/* Main node */}
      <motion.div
        className={cn(
          "relative w-28 h-14 rounded-lg border flex flex-col items-center justify-center gap-0.5",
          "bg-gradient-to-br from-bg-surface to-bg-elevated",
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

        {/* Label */}
        <span className="text-[9px] font-mono text-neon-rose tracking-wider">
          {label.replace(/_/g, ' ').toUpperCase()}
        </span>
      </motion.div>

      {/* Call count badge */}
      {callCount > 0 && (
        <motion.div
          className={cn(
            "absolute -top-1.5 -right-1.5 min-w-[18px] h-[18px] px-1 rounded-full flex items-center justify-center",
            "bg-neon-rose text-bg-base text-[9px] font-mono font-bold",
            "border-2 border-bg-base"
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
        className="!w-2.5 !h-2.5 !bg-neon-rose !border-2 !border-bg-base"
      />
    </div>
  )
}

export default memo(ToolNode)
