// ============================================================
//  Pillars Section - Three Core Features
// ============================================================

import { motion } from 'framer-motion'
import { GitBranch, Brain, Terminal } from 'lucide-react'

const pillars = [
  {
    icon: GitBranch,
    title: 'Knowledge DAG',
    description: 'Facts are nodes. Derivations are edges. Your research becomes a living, queryable graph that grows with every verified conclusion.',
    color: 'cyan' as const,
    features: ['File-SSoT Architecture', 'Version-controlled facts', 'Cross-session reuse'],
  },
  {
    icon: Brain,
    title: 'Multi-Agent Reasoning',
    description: 'Planner, Reasoner, Librarian, Verifier — a team of specialized agents orchestrated to push your research forward.',
    color: 'gold' as const,
    features: ['Autonomous planning', 'Evidence-based reasoning', 'Consensus verification'],
  },
  {
    icon: Terminal,
    title: 'Verifiable Compute',
    description: 'If it can be calculated, we calculate. Python sandbox for mathematical proofs, statistical analysis, and numerical verification.',
    color: 'purple' as const,
    features: ['Sandboxed execution', 'Reproducible results', 'Safe by default'],
  },
]

const colorMap = {
  cyan: {
    bg: 'bg-neon-cyan/10',
    border: 'border-neon-cyan/30',
    hoverBorder: 'hover:border-neon-cyan/60',
    text: 'text-neon-cyan',
    glow: 'group-hover:shadow-glow-cyan',
    badge: 'bg-neon-cyan/20 text-neon-cyan border-neon-cyan/30',
  },
  gold: {
    bg: 'bg-neon-gold/10',
    border: 'border-neon-gold/30',
    hoverBorder: 'hover:border-neon-gold/60',
    text: 'text-neon-gold',
    glow: 'group-hover:shadow-glow-gold',
    badge: 'bg-neon-gold/20 text-neon-gold border-neon-gold/30',
  },
  purple: {
    bg: 'bg-neon-purple/10',
    border: 'border-neon-purple/30',
    hoverBorder: 'hover:border-neon-purple/60',
    text: 'text-neon-purple',
    glow: 'group-hover:shadow-glow-purple',
    badge: 'bg-neon-purple/20 text-neon-purple border-neon-purple/30',
  },
}

export function PillarsSection() {
  return (
    <section id="pillars" className="py-24 px-6">
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
            Three Pillars of <span className="text-neon-cyan">Verifiable Research</span>
          </h2>
          <p className="text-text-secondary text-lg max-w-2xl mx-auto">
            Not just another chatbot. A complete system for pushing knowledge forward.
          </p>
        </motion.div>

        {/* Pillars Grid */}
        <div className="grid md:grid-cols-3 gap-6">
          {pillars.map((pillar, index) => {
            const colors = colorMap[pillar.color]
            const Icon = pillar.icon
            
            return (
              <motion.div
                key={pillar.title}
                initial={{ opacity: 0, y: 30 }}
                whileInView={{ opacity: 1, y: 0 }}
                viewport={{ once: true }}
                transition={{ duration: 0.5, delay: index * 0.1 }}
                className={`group relative p-6 rounded-2xl border ${colors.border} ${colors.hoverBorder} bg-bg-surface/50 backdrop-blur-sm transition-all duration-300 ${colors.glow}`}
              >
                {/* Icon */}
                <div className={`inline-flex items-center justify-center size-14 rounded-xl ${colors.bg} ${colors.text} mb-5`}>
                  <Icon className="size-7" />
                </div>

                {/* Title */}
                <h3 className="font-display text-xl font-bold text-text-primary mb-3">
                  {pillar.title}
                </h3>

                {/* Description */}
                <p className="text-text-secondary text-sm leading-relaxed mb-5">
                  {pillar.description}
                </p>

                {/* Features */}
                <div className="flex flex-wrap gap-2">
                  {pillar.features.map((feature) => (
                    <span
                      key={feature}
                      className={`px-2 py-1 text-xs font-mono rounded border ${colors.badge}`}
                    >
                      {feature}
                    </span>
                  ))}
                </div>

                {/* Corner Decoration */}
                <div className={`absolute top-0 right-0 size-8 border-t-2 border-r-2 ${colors.border} rounded-tr-2xl`} />
                <div className={`absolute bottom-0 left-0 size-8 border-b-2 border-l-2 ${colors.border} rounded-bl-2xl`} />
              </motion.div>
            )
          })}
        </div>
      </div>
    </section>
  )
}
