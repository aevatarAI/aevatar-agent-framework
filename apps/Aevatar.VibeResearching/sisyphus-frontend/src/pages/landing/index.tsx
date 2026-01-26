// ============================================================
//  Sisyphus Landing Page - Entry Point
//  "The Boulder Never Stops. Neither Does Your Research."
// ============================================================

import { HeroSection } from './hero-section'
import { PillarsSection } from './pillars-section'
import { DagShowcaseSection } from './dag-showcase-section'
import { WorkflowSection } from './workflow-section'
import { CtaFooter } from './cta-footer'

export function LandingPage() {
  return (
    <div className="min-h-dvh bg-gradient-subtle overflow-x-hidden">
      {/* ─── Background Grid ─── */}
      <div className="fixed inset-0 bg-grid-animated pointer-events-none opacity-20 z-0" />
      
      {/* ─── Cyber Corner Decorations ─── */}
      <div className="fixed top-0 left-0 size-40 border-l-2 border-t-2 border-neon-cyan/20 pointer-events-none z-10" />
      <div className="fixed top-0 right-0 size-40 border-r-2 border-t-2 border-neon-gold/20 pointer-events-none z-10" />
      <div className="fixed bottom-0 left-0 size-40 border-l-2 border-b-2 border-neon-purple/20 pointer-events-none z-10" />
      <div className="fixed bottom-0 right-0 size-40 border-r-2 border-b-2 border-neon-cyan/20 pointer-events-none z-10" />

      {/* ─── Content ─── */}
      <div className="relative z-20">
        <HeroSection />
        <PillarsSection />
        <DagShowcaseSection />
        <WorkflowSection />
        <CtaFooter />
      </div>
    </div>
  )
}

export default LandingPage
