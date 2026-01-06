import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// ============================================================
//  Vite config (Learning)
//
//  Port policy:
//  - frontend dev port MUST be stable (5173) to avoid drift
//  - backend url MUST NOT be hardcoded (use env injection)
// ============================================================

const backendUrl = process.env.VITE_LEARNING_API_URL || 'http://localhost:5678'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      // Keep frontend code simple: call /api/* and let dev proxy forward.
      '/api': {
        target: backendUrl,
        changeOrigin: true,
        secure: false,
      },
      '/health': {
        target: backendUrl,
        changeOrigin: true,
        secure: false,
      },
    },
  },
})


