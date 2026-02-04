import React from "react"
import { Link } from "react-router-dom"
import { ShieldCheck, Users, Sparkles, Zap } from "lucide-react"

// ============================================================
//  Auth Layout - Split Screen with Branding
// ============================================================

interface FeatureItem {
  icon: React.ReactNode
  iconBg: string
  title: string
  description: string
}

interface AuthLayoutProps {
  children: React.ReactNode
  title: string
  subtitle?: string
  variant?: "login" | "register"
}

// Feature configs per variant
const loginFeatures: FeatureItem[] = [
  {
    icon: <ShieldCheck className="w-6 h-6 text-neon-purple" />,
    iconBg: "bg-neon-purple/10",
    title: "Secure Authentication",
    description: "Enterprise-grade security with MFA support",
  },
  {
    icon: <Users className="w-6 h-6 text-neon-gold" />,
    iconBg: "bg-neon-gold/10",
    title: "Team Collaboration",
    description: "Manage teams with role-based access control",
  },
]

const registerFeatures: FeatureItem[] = [
  {
    icon: <Sparkles className="w-6 h-6 text-neon-cyan" />,
    iconBg: "bg-neon-cyan/10",
    title: "Get Started Free",
    description: "No credit card required to start",
  },
  {
    icon: <Zap className="w-6 h-6 text-neon-gold" />,
    iconBg: "bg-neon-gold/10",
    title: "Instant Access",
    description: "Start using AI agents immediately",
  },
]

export const AuthLayout: React.FC<AuthLayoutProps> = ({
  children,
  title,
  subtitle,
  variant = "login",
}) => {
  const features = variant === "register" ? registerFeatures : loginFeatures

  return (
    <div className="min-h-screen flex bg-background">
      {/* Left Panel - Branding */}
      <div className="hidden lg:flex lg:flex-1 flex-col items-center justify-center bg-surface p-16 relative overflow-hidden">
        {/* Background Grid */}
        <div className="absolute inset-0 bg-grid-animated opacity-20 pointer-events-none" />

        {/* Gradient Overlays */}
        <div className="absolute top-0 left-0 w-96 h-96 bg-neon-cyan/10 rounded-full blur-3xl" />
        <div className="absolute bottom-0 right-0 w-96 h-96 bg-neon-gold/10 rounded-full blur-3xl" />

        {/* Cyber Corners */}
        <div className="absolute top-0 left-0 w-32 h-32 border-l-2 border-t-2 border-neon-cyan/30" />
        <div className="absolute bottom-0 right-0 w-32 h-32 border-r-2 border-b-2 border-neon-gold/30" />

        {/* Content */}
        <div className="relative z-10 text-center space-y-4">
          <Link to="/" className="block">
            <div className="inline-flex items-center justify-center w-20 h-20 rounded-xl bg-neon-cyan mb-4 cyber-corners">
              <span className="text-4xl font-display font-bold text-bg-base">S</span>
            </div>
          </Link>

          <h1 className="text-4xl font-display font-bold text-neon-cyan text-glow-cyan tracking-wider">
            SISYPHUS
          </h1>

          <p className="text-text-muted text-lg max-w-sm">
            AI-Powered Research Platform
          </p>

          {/* Feature List */}
          <div className="flex flex-col gap-6 pt-12 text-left">
            {features.map((feature, index) => (
              <div key={index} className="flex items-center gap-4">
                <div className={`w-12 h-12 rounded-xl ${feature.iconBg} flex items-center justify-center flex-shrink-0`}>
                  {feature.icon}
                </div>
                <div className="space-y-1">
                  <p className="text-sm font-semibold text-text-primary">
                    {feature.title}
                  </p>
                  <p className="text-[13px] text-text-muted">
                    {feature.description}
                  </p>
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* Right Panel - Form */}
      <div className="flex-1 lg:max-w-[480px] flex flex-col justify-center p-12">
        {/* Mobile Logo */}
        <div className="lg:hidden text-center mb-8">
          <Link to="/" className="inline-flex items-center gap-3">
            <div className="w-10 h-10 rounded-lg bg-neon-cyan flex items-center justify-center">
              <span className="text-lg font-display font-bold text-bg-base">S</span>
            </div>
            <span className="text-xl font-display font-semibold text-neon-cyan">
              SISYPHUS
            </span>
          </Link>
        </div>

        {/* Form Header */}
        <div className="mb-8">
          <h2 className="text-2xl font-display font-bold text-text-primary tracking-wide">
            {title}
          </h2>
          {subtitle && (
            <p className="mt-2 text-sm text-text-muted">{subtitle}</p>
          )}
        </div>

        {/* Form Content */}
        <div className="space-y-6">
          {children}
        </div>
      </div>
    </div>
  )
}

export default AuthLayout
