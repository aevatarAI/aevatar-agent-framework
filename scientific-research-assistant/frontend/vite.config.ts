import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'node:path'

// https://vite.dev/config/
export default defineConfig({
    plugins: [react()],
    resolve: {
        alias: {
            // Local shim for @agui/sdk (keeps dev offline-friendly; can be replaced with real package later)
            '@agui/sdk': path.resolve(__dirname, 'src/lib/agui-sdk.ts'),
            // Shared UI core (used by both Web and Obsidian hosts)
            '@sra/ui': path.resolve(__dirname, '../ui/src'),
        },
    },
    server: {
        // Allow importing shared sources outside the frontend root (monorepo).
        fs: {
            allow: [path.resolve(__dirname, '..')],
        },
        host: true,
        port: 5173,
        proxy: {
            '/api': {
                target: process.env.SRA_API_PROXY_TARGET || 'http://localhost:5678',
                changeOrigin: true,
            },
            '/health': {
                target: process.env.SRA_API_PROXY_TARGET || 'http://localhost:5678',
                changeOrigin: true,
            }
        },
    }
})
