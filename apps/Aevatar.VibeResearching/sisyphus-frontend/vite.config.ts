import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'path'

const backendPort = process.env.BACKEND_PORT || '5678'
const backendTarget = process.env.VITE_API_BASE_URL || `http://localhost:${backendPort}`

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  server: {
    port: parseInt(process.env.PORT || '5173', 10),
    proxy: {
      // All API requests go to Backend (includes /api/auth, /api/account, etc.)
      '/api': {
        target: backendTarget,
        changeOrigin: true,
      },
      // OpenIddict endpoints (for token mode)
      '/connect': {
        target: backendTarget,
        changeOrigin: true,
      },
      // OIDC Discovery
      '/.well-known': {
        target: backendTarget,
        changeOrigin: true,
      },
    },
  },
})
