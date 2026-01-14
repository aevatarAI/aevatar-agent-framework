/** @type {import('tailwindcss').Config} */
export default {
  darkMode: ["class"],
  content: [
    './pages/**/*.{ts,tsx}',
    './components/**/*.{ts,tsx}',
    './app/**/*.{ts,tsx}',
    './src/**/*.{ts,tsx}',
    './.cursor/**/*.{ts,tsx}',
  ],
  prefix: "",
  theme: {
    container: {
      center: true,
      padding: "2rem",
      screens: {
        "2xl": "1400px",
      },
    },
    extend: {
      colors: {
        background: "var(--bg-base)",
        foreground: "var(--text-primary)",
        
        surface: {
          DEFAULT: "var(--bg-surface)",
          elevated: "var(--bg-elevated)",
          accent: "var(--bg-accent)",
        },
        
        text: {
          primary: "var(--text-primary)",
          secondary: "var(--text-secondary)",
          muted: "var(--text-muted)",
          dimmed: "var(--text-dimmed)",
        },
        
        border: {
          DEFAULT: "var(--border-default)",
          subtle: "var(--border-subtle)",
          strong: "var(--border-strong)",
        },
        
        neon: {
          cyan: "var(--neon-cyan)",
          gold: "var(--neon-gold)",
          purple: "var(--neon-purple)",
          green: "var(--neon-green)",
          orange: "var(--neon-orange)",
          red: "var(--neon-red)",
        },
        
        /* Legacy mappings for compatibility */
        accent: {
          cyan: "var(--neon-cyan)",
          blue: "var(--neon-cyan)",
          violet: "var(--neon-purple)",
          magenta: "var(--neon-gold)",
          green: "var(--neon-green)",
          emerald: "var(--neon-green)",
          amber: "var(--neon-orange)",
          rose: "var(--neon-red)",
        },
        
        primary: {
          DEFAULT: "var(--neon-cyan)",
          foreground: "#030308",
        },
        secondary: {
          DEFAULT: "var(--bg-surface)",
          foreground: "var(--text-primary)",
        },
        muted: {
          DEFAULT: "var(--bg-accent)",
          foreground: "var(--text-muted)",
        },
        destructive: {
          DEFAULT: "var(--neon-red)",
          foreground: "#FFFFFF",
        },
        card: {
          DEFAULT: "var(--bg-surface)",
          foreground: "var(--text-primary)",
        },
        popover: {
          DEFAULT: "var(--bg-elevated)",
          foreground: "var(--text-primary)",
        },
        input: "var(--border-default)",
        ring: "var(--neon-cyan)",
      },
      
      borderRadius: {
        lg: "var(--radius-lg)",
        md: "var(--radius-md)",
        sm: "var(--radius-sm)",
        xl: "var(--radius-xl)",
      },
      
      fontFamily: {
        sans: ["Space Grotesk", "-apple-system", "BlinkMacSystemFont", "sans-serif"],
        display: ["Orbitron", "sans-serif"],
        mono: ["JetBrains Mono", "ui-monospace", "SF Mono", "monospace"],
      },
      
      boxShadow: {
        'sm': 'var(--shadow-sm)',
        'md': 'var(--shadow-md)',
        'lg': 'var(--shadow-lg)',
        'glow-cyan': 'var(--glow-cyan)',
        'glow-gold': 'var(--glow-gold)',
        'glow-purple': 'var(--glow-purple)',
        'glow-green': 'var(--glow-green)',
      },
      
      keyframes: {
        "accordion-down": {
          from: { height: "0" },
          to: { height: "var(--radix-accordion-content-height)" },
        },
        "accordion-up": {
          from: { height: "var(--radix-accordion-content-height)" },
          to: { height: "0" },
        },
        "fade-in": {
          from: { opacity: "0", transform: "translateY(12px)" },
          to: { opacity: "1", transform: "translateY(0)" },
        },
        "scan-line": {
          "0%": { transform: "translateX(-100%)" },
          "100%": { transform: "translateX(100%)" },
        },
        "pulse-glow": {
          "0%, 100%": { opacity: "1" },
          "50%": { opacity: "0.6" },
        },
      },
      
      animation: {
        "accordion-down": "accordion-down 0.2s ease-out",
        "accordion-up": "accordion-up 0.2s ease-out",
        "fade-in": "fade-in 0.4s ease-out",
        "scan-line": "scan-line 1.5s linear infinite",
        "pulse-glow": "pulse-glow 2s ease-in-out infinite",
      },
    },
  },
  plugins: [require("tailwindcss-animate")],
}
