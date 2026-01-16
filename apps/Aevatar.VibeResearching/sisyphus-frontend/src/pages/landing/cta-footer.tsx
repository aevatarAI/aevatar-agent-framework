// ============================================================
//  CTA Footer - "Join the Eternal Climb"
// ============================================================

import { useNavigate } from 'react-router-dom'
import { motion } from 'framer-motion'
import { Sparkles, Github, FileText, ExternalLink } from 'lucide-react'

export function CtaFooter() {
  const navigate = useNavigate()

  return (
    <footer className="relative py-24 px-6 overflow-hidden">
      {/* Background Gradient */}
      <div className="absolute inset-0 bg-gradient-to-t from-neon-cyan/5 via-transparent to-transparent pointer-events-none" />

      <div className="relative max-w-4xl mx-auto text-center">
        {/* Main CTA */}
        <motion.div
          initial={{ opacity: 0, y: 20 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true }}
          transition={{ duration: 0.5 }}
        >
          {/* Decorative Element */}
          <div className="flex items-center justify-center gap-2 mb-8">
            <div className="h-px w-16 bg-gradient-to-r from-transparent to-neon-cyan/50" />
            <div className="size-2 rounded-full bg-neon-cyan animate-pulse" />
            <div className="h-px w-16 bg-gradient-to-l from-transparent to-neon-cyan/50" />
          </div>

          {/* Quote */}
          <blockquote className="font-display text-2xl md:text-4xl font-bold text-text-primary mb-6 leading-tight">
            "Every great discovery began with a question.
            <br />
            <span className="text-neon-cyan text-glow-cyan">Ask yours.</span>"
          </blockquote>

          <p className="text-lg text-text-secondary mb-10 max-w-xl mx-auto">
            Let Sisyphus push the boulder. You focus on the summit.
          </p>

          {/* CTA Button */}
          <motion.button
            whileHover={{ scale: 1.02 }}
            whileTap={{ scale: 0.98 }}
            onClick={() => navigate('/app')}
            className="group relative inline-flex items-center gap-3 px-10 py-5 rounded-2xl bg-neon-cyan text-bg-base font-bold text-xl transition-all hover:shadow-glow-cyan"
          >
            <Sparkles className="size-6" />
            <span>Begin Your Research</span>
            <div className="absolute inset-0 rounded-2xl bg-white/20 opacity-0 group-hover:opacity-100 transition-opacity" />
          </motion.button>
        </motion.div>

        {/* Secondary Links */}
        <motion.div
          initial={{ opacity: 0 }}
          whileInView={{ opacity: 1 }}
          viewport={{ once: true }}
          transition={{ duration: 0.5, delay: 0.2 }}
          className="mt-16 flex flex-wrap items-center justify-center gap-6"
        >
          <a
            href="https://github.com/aevatarAI/aevatar-agent-framework"
            target="_blank"
            rel="noopener noreferrer"
            className="inline-flex items-center gap-2 px-4 py-2 rounded-lg border border-border-subtle text-text-muted hover:text-text-primary hover:border-border-default transition-all"
          >
            <Github className="size-4" />
            <span className="text-sm">GitHub</span>
            <ExternalLink className="size-3" />
          </a>
          <a
            href="#"
            className="inline-flex items-center gap-2 px-4 py-2 rounded-lg border border-border-subtle text-text-muted hover:text-text-primary hover:border-border-default transition-all"
          >
            <FileText className="size-4" />
            <span className="text-sm">Documentation</span>
          </a>
        </motion.div>

        {/* Footer Bottom */}
        <motion.div
          initial={{ opacity: 0 }}
          whileInView={{ opacity: 1 }}
          viewport={{ once: true }}
          transition={{ duration: 0.5, delay: 0.3 }}
          className="mt-16 pt-8 border-t border-border-subtle"
        >
          <div className="flex flex-col md:flex-row items-center justify-between gap-4 text-sm text-text-dimmed">
            <div className="flex items-center gap-2">
              <span className="font-display text-neon-cyan">SISYPHUS</span>
              <span>•</span>
              <span>Aevatar Agent Framework</span>
            </div>
            <div className="flex items-center gap-4">
              <span>Built with</span>
              <span className="text-neon-cyan">React</span>
              <span>+</span>
              <span className="text-neon-gold">.NET 10</span>
              <span>+</span>
              <span className="text-neon-purple">AI Agents</span>
            </div>
          </div>

          {/* Sisyphus Quote */}
          <p className="mt-6 text-xs text-text-dimmed font-mono italic">
            "The struggle itself toward the heights is enough to fill a man's heart." — Albert Camus
          </p>
        </motion.div>
      </div>
    </footer>
  )
}
