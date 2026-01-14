import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'path'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  server: {
    port: 3000,
    proxy: {
      // Proxy to backend (port 5678)
      '/api': {
        target: 'http://localhost:5678',
        changeOrigin: true,
      },
    },
  },
  // Environment variables
  define: {
    'import.meta.env.VITE_AXIOM_API_BASE': JSON.stringify(process.env.VITE_AXIOM_API_BASE || ''),
  },
})
