// ============================================================
//  Hero Section - "Push Knowledge Uphill. Automatically."
// ============================================================

import { useNavigate } from 'react-router-dom'
import { Sparkles, Play, ChevronDown } from 'lucide-react'
import { motion } from 'framer-motion'
import { HeroDagBackground } from './hero-dag-background'

export function HeroSection() {
  const navigate = useNavigate()

  return (
    <section className="relative min-h-dvh flex flex-col items-center justify-center px-6 overflow-hidden">
      {/* ─── Animated DAG Background ─── */}
      <HeroDagBackground />

      {/* ─── Gradient Overlay ─── */}
      <div className="absolute inset-0 bg-gradient-to-b from-transparent via-bg-base/50 to-bg-base pointer-events-none" />

      {/* ─── Content ─── */}
      <div className="relative z-10 text-center max-w-4xl mx-auto">
        {/* Logo / Brand */}
        <motion.div
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.6 }}
          className="mb-8"
        >
          <div className="inline-flex items-center gap-3 px-4 py-2 rounded-full border border-neon-cyan/30 bg-bg-surface/50 backdrop-blur-sm">
            <div className="size-2 rounded-full bg-neon-cyan animate-pulse" />
            <span className="font-mono text-sm text-neon-cyan tracking-wider">SISYPHUS RESEARCH PLATFORM</span>
          </div>
        </motion.div>

        {/* Main Headline */}
        <motion.h1
          initial={{ opacity: 0, y: 30 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.6, delay: 0.1 }}
          className="font-display text-5xl md:text-7xl font-bold mb-6"
        >
          <span className="text-text-primary">Push Knowledge</span>
          <br />
          <span className="text-neon-cyan text-glow-cyan">Uphill.</span>
          <br />
          <span className="text-neon-gold text-glow-gold">Automatically.</span>
        </motion.h1>

        {/* Subtitle */}
        <motion.p
          initial={{ opacity: 0, y: 30 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.6, delay: 0.2 }}
          className="text-lg md:text-xl text-text-secondary max-w-2xl mx-auto mb-10 leading-relaxed"
        >
          Multi-agent research orchestration powered by verifiable reasoning.
          <br />
          <span className="text-text-muted">From hypothesis to publication. Every step grounded in facts.</span>
        </motion.p>

        {/* CTA Buttons */}
        <motion.div
          initial={{ opacity: 0, y: 30 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.6, delay: 0.3 }}
          className="flex flex-wrap items-center justify-center gap-4"
        >
          <button
            onClick={() => navigate('/app')}
            className="group relative inline-flex items-center gap-3 px-8 py-4 rounded-xl bg-neon-cyan text-bg-base font-semibold text-lg transition-all hover:shadow-glow-cyan hover:-translate-y-1"
          >
            <Sparkles className="size-5" />
            <span>Start Research</span>
            <div className="absolute inset-0 rounded-xl bg-white/20 opacity-0 group-hover:opacity-100 transition-opacity" />
          </button>

          <button
            onClick={() => {
              document.getElementById('dag-showcase')?.scrollIntoView({ behavior: 'smooth' })
            }}
            className="group inline-flex items-center gap-3 px-8 py-4 rounded-xl border border-neon-gold/50 text-neon-gold font-semibold text-lg transition-all hover:bg-neon-gold/10 hover:border-neon-gold hover:-translate-y-1"
          >
            <Play className="size-5" />
            <span>Explore DAG</span>
          </button>
        </motion.div>

        {/* Stats Preview */}
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          transition={{ duration: 0.6, delay: 0.5 }}
          className="mt-16 flex items-center justify-center gap-8 md:gap-16"
        >
          <StatItem label="Agents" value="5+" color="cyan" />
          <StatItem label="Knowledge Nodes" value="∞" color="gold" />
          <StatItem label="Verifiable" value="100%" color="purple" />
        </motion.div>
      </div>

      {/* ─── Scroll Indicator ─── */}
      <motion.div
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        transition={{ duration: 0.6, delay: 0.7 }}
        className="absolute bottom-8 left-1/2 -translate-x-1/2"
      >
        <button
          onClick={() => {
            document.getElementById('pillars')?.scrollIntoView({ behavior: 'smooth' })
          }}
          className="flex flex-col items-center gap-2 text-text-muted hover:text-neon-cyan transition-colors"
        >
          <span className="text-xs font-mono tracking-wider">SCROLL</span>
          <ChevronDown className="size-5 animate-bounce" />
        </button>
      </motion.div>
    </section>
  )
}

// ─── Stat Item Component ───
function StatItem({ label, value, color }: { label: string; value: string; color: 'cyan' | 'gold' | 'purple' }) {
  const colorClasses = {
    cyan: 'text-neon-cyan',
    gold: 'text-neon-gold',
    purple: 'text-neon-purple',
  }

  return (
    <div className="text-center">
      <div className={`font-display text-3xl md:text-4xl font-bold ${colorClasses[color]}`}>{value}</div>
      <div className="text-xs font-mono text-text-muted tracking-wider mt-1">{label.toUpperCase()}</div>
    </div>
  )
}
