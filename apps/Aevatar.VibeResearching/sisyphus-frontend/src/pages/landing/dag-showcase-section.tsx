// ============================================================
//  DAG Showcase Section - Full Interactive DAG Viewer
// ============================================================

import { motion } from 'framer-motion'
import { LandingDagViewer } from './landing-dag-viewer'

export function DagShowcaseSection() {
  return (
    <section id="dag-showcase" className="py-24 px-6 bg-bg-base/50">
      <div className="max-w-7xl mx-auto">
        {/* Section Header */}
        <motion.div
          initial={{ opacity: 0, y: 20 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true }}
          transition={{ duration: 0.5 }}
          className="text-center mb-12"
        >
          <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full border border-neon-cyan/30 bg-neon-cyan/10 mb-6">
            <span className="size-2 rounded-full bg-neon-cyan animate-pulse" />
            <span className="text-xs font-mono text-neon-cyan tracking-wider">LIVE PREVIEW</span>
          </div>
          
          <h2 className="font-display text-3xl md:text-5xl font-bold mb-4">
            <span className="text-text-primary">Knowledge</span>{' '}
            <span className="text-neon-cyan text-glow-cyan">Graph</span>
          </h2>
          
          <p className="text-text-secondary text-lg max-w-3xl mx-auto leading-relaxed">
            Every fact is a node. Every derivation is an edge.
            <br />
            <span className="text-text-muted">
              Your research becomes a living, queryable graph that grows with every verified conclusion.
            </span>
          </p>
        </motion.div>

        {/* DAG Viewer Container */}
        <motion.div
          initial={{ opacity: 0, y: 30 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true }}
          transition={{ duration: 0.6, delay: 0.2 }}
          className="relative"
        >
          {/* Outer Glow */}
          <div className="absolute -inset-1 bg-gradient-to-r from-neon-cyan/20 via-neon-purple/20 to-neon-gold/20 rounded-3xl blur-xl opacity-50" />
          
          {/* Main Container */}
          <div className="relative rounded-2xl border border-neon-cyan/30 bg-bg-surface/80 backdrop-blur-md overflow-hidden">
            {/* Cyber Corners */}
            <div className="absolute top-0 left-0 w-16 h-16 border-l-2 border-t-2 border-neon-cyan/60 rounded-tl-2xl pointer-events-none z-20" />
            <div className="absolute top-0 right-0 w-16 h-16 border-r-2 border-t-2 border-neon-gold/60 rounded-tr-2xl pointer-events-none z-20" />
            <div className="absolute bottom-0 left-0 w-16 h-16 border-l-2 border-b-2 border-neon-purple/60 rounded-bl-2xl pointer-events-none z-20" />
            <div className="absolute bottom-0 right-0 w-16 h-16 border-r-2 border-b-2 border-neon-cyan/60 rounded-br-2xl pointer-events-none z-20" />

            {/* DAG Viewer */}
            <LandingDagViewer />
          </div>
        </motion.div>

        {/* Bottom Hint */}
        <motion.p
          initial={{ opacity: 0 }}
          whileInView={{ opacity: 1 }}
          viewport={{ once: true }}
          transition={{ duration: 0.5, delay: 0.4 }}
          className="text-center text-xs text-text-dimmed font-mono mt-6"
        >
          ↑ INTERACTIVE • DRAG TO PAN • SCROLL TO ZOOM • CLICK NODES FOR DETAILS ↑
        </motion.p>
      </div>
    </section>
  )
}
