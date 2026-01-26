// ============================================================
//  Workflow Section - Research Process Timeline
// ============================================================

import { motion } from 'framer-motion'
import { Target, BookOpen, Brain, Zap, FileText, RefreshCw } from 'lucide-react'

const steps = [
  {
    icon: Target,
    title: 'Define Goal',
    description: 'Set your research objective',
    color: 'cyan' as const,
  },
  {
    icon: BookOpen,
    title: 'Gather Materials',
    description: 'Query the knowledge DAG',
    color: 'gold' as const,
  },
  {
    icon: Brain,
    title: 'Plan & Reason',
    description: 'Multi-agent orchestration',
    color: 'purple' as const,
  },
  {
    icon: Zap,
    title: 'Verify',
    description: 'Python compute sandbox',
    color: 'green' as const,
  },
  {
    icon: FileText,
    title: 'Synthesize',
    description: 'Generate paper draft',
    color: 'cyan' as const,
  },
  {
    icon: RefreshCw,
    title: 'Iterate',
    description: 'Or publish results',
    color: 'gold' as const,
  },
]

const colorMap = {
  cyan: {
    bg: 'bg-neon-cyan/20',
    text: 'text-neon-cyan',
    border: 'border-neon-cyan/50',
    glow: 'shadow-glow-cyan',
  },
  gold: {
    bg: 'bg-neon-gold/20',
    text: 'text-neon-gold',
    border: 'border-neon-gold/50',
    glow: 'shadow-glow-gold',
  },
  purple: {
    bg: 'bg-neon-purple/20',
    text: 'text-neon-purple',
    border: 'border-neon-purple/50',
    glow: 'shadow-glow-purple',
  },
  green: {
    bg: 'bg-neon-green/20',
    text: 'text-neon-green',
    border: 'border-neon-green/50',
    glow: 'shadow-glow-green',
  },
}

export function WorkflowSection() {
  return (
    <section className="py-24 px-6">
      <div className="max-w-6xl mx-auto">
        {/* Section Header */}
        <motion.div
          initial={{ opacity: 0, y: 20 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true }}
          transition={{ duration: 0.5 }}
          className="text-center mb-16"
        >
          <h2 className="font-display text-3xl md:text-4xl font-bold text-text-primary mb-4">
            The Research <span className="text-neon-gold text-glow-gold">Loop</span>
          </h2>
          <p className="text-text-secondary text-lg max-w-2xl mx-auto">
            From hypothesis to publication. Every step orchestrated by AI agents.
          </p>
        </motion.div>

        {/* Timeline */}
        <div className="relative">
          {/* Connection Line (Desktop) */}
          <div className="hidden md:block absolute top-1/2 left-0 right-0 h-0.5 bg-gradient-to-r from-neon-cyan/30 via-neon-gold/30 to-neon-purple/30 -translate-y-1/2" />

          {/* Steps Grid */}
          <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-6 gap-6">
            {steps.map((step, index) => {
              const colors = colorMap[step.color]
              const Icon = step.icon

              return (
                <motion.div
                  key={step.title}
                  initial={{ opacity: 0, y: 30 }}
                  whileInView={{ opacity: 1, y: 0 }}
                  viewport={{ once: true }}
                  transition={{ duration: 0.5, delay: index * 0.1 }}
                  className="relative flex flex-col items-center text-center group"
                >
                  {/* Step Number */}
                  <div className="absolute -top-3 left-1/2 -translate-x-1/2 px-2 py-0.5 rounded-full bg-bg-surface border border-border-subtle text-xs font-mono text-text-dimmed z-10">
                    {String(index + 1).padStart(2, '0')}
                  </div>

                  {/* Icon Circle */}
                  <div className={`relative size-16 rounded-2xl ${colors.bg} ${colors.border} border flex items-center justify-center transition-all group-hover:${colors.glow}`}>
                    <Icon className={`size-7 ${colors.text}`} />
                  </div>

                  {/* Title */}
                  <h3 className="font-display text-sm font-bold text-text-primary mt-4 mb-1">
                    {step.title}
                  </h3>

                  {/* Description */}
                  <p className="text-xs text-text-muted">
                    {step.description}
                  </p>

                  {/* Arrow (between steps, desktop only) */}
                  {index < steps.length - 1 && (
                    <div className="hidden lg:block absolute top-8 -right-3 text-text-dimmed">
                      →
                    </div>
                  )}
                </motion.div>
              )
            })}
          </div>
        </div>

        {/* Bottom Note */}
        <motion.div
          initial={{ opacity: 0 }}
          whileInView={{ opacity: 1 }}
          viewport={{ once: true }}
          transition={{ duration: 0.5, delay: 0.6 }}
          className="mt-16 text-center"
        >
          <p className="text-sm text-text-muted">
            <span className="text-neon-cyan">File-SSoT:</span> Every step is logged. Every fact is versioned. Every result is reproducible.
          </p>
        </motion.div>
      </div>
    </section>
  )
}
