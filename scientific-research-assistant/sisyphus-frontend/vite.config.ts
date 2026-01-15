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
      // Proxy to backend
      '/api': {
        target: backendTarget,
        changeOrigin: true,
      },
    },
  },
  // Environment variables
  define: {
    'import.meta.env.VITE_AXIOM_API_BASE': JSON.stringify(process.env.VITE_AXIOM_API_BASE || ''),
  },
})
