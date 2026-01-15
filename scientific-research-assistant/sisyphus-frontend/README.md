# Sisyphus Research Platform

> Multi-Agent Research Orchestration Dashboard

A cyberpunk-styled frontend for the Sisyphus multi-agent research system. Features real-time DAG visualization, streaming worker status, and interactive chat interface.

## Screenshot

![Sisyphus Platform](/.playwright-mcp/sisyphus-platform.png)

## Features

- 🌌 **Deep Space Theme** - Cyberpunk dark aesthetic with cyan glow accents
- 📊 **Workflow Topology** - Interactive DAG visualization with live status updates
- 💬 **Interaction Stream** - Chat interface for directing agents and viewing responses
- 🤖 **Live Worker Status** - Real-time streaming output from SearchBot, PDF Reader, etc.
- 📈 **System Metrics** - Compute load, network I/O, latency indicators
- 🔌 **AG-UI Protocol** - Server-Sent Events for real-time backend communication

## Tech Stack

- **React 18** + **TypeScript**
- **Vite 5** - Fast HMR development
- **TailwindCSS 3** - Utility-first styling
- **Zustand** - Lightweight state management
- **Framer Motion** - Smooth animations
- **Lucide React** - Icon library

## Getting Started

```bash
# Install dependencies
npm install

# Start development server
npm run dev
# → http://localhost:3000 (or next available port)

# Build for production
npm run build
```

## Project Structure

```
src/
├── components/
│   ├── sisyphus/           # Business components
│   │   ├── header.tsx
│   │   ├── sidebar.tsx
│   │   ├── interaction-stream.tsx
│   │   ├── workflow-topology.tsx
│   │   └── worker-status.tsx
│   └── ui/                 # Base UI components
├── hooks/
│   └── use-event-stream.ts # AG-UI SSE connection
├── store/
│   └── sisyphus-store.ts   # Zustand state
├── types/
│   └── index.ts            # TypeScript definitions
└── App.tsx
```

## Backend Integration

Expects AG-UI compatible backend at `/api/sessions/:id/agui/events` (SSE endpoint).

Supported events:
- `TEXT_MESSAGE_START`, `TEXT_MESSAGE_CONTENT`, `TEXT_MESSAGE_END`
- `RUN_STARTED`, `RUN_FINISHED`, `RUN_ERROR`
- `STEP_STARTED`, `STEP_FINISHED`
- `CUSTOM` (for domain-specific events)

## License

MIT
