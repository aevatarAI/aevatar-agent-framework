# Sisyphus Research Platform - Architecture

## Directory Structure

```
src/
├── components/
│   ├── sisyphus/           # Core business components
│   │   ├── header.tsx      # Top navigation bar with session status
│   │   ├── sidebar.tsx     # Left navigation with sessions list & settings toggle
│   │   ├── status-bar.tsx  # Bottom system info bar
│   │   ├── interaction-stream.tsx  # Chat/dialog with markdown + tools + filter
│   │   ├── composer.tsx    # Enhanced input (mode switch, agent selector, files)
│   │   ├── workflow-topology.tsx   # DAG visualization (enhanced)
│   │   ├── agent-cards-view.tsx    # Multi-agent cards (vibe mode)
│   │   ├── worker-status.tsx       # Live worker cards
│   │   ├── settings-panel.tsx      # Full settings page (Tools, LLM, Advanced)
│   │   ├── skills-mp-modal.tsx     # SkillsMP Marketplace modal
│   │   ├── tool-output-display.tsx # Tool call results display
│   │   └── index.ts        # Barrel export
│   └── ui/                 # Base UI components (shadcn-style)
│       ├── button.tsx      # Cyberpunk-styled button
│       ├── dialog.tsx      # Modal dialog with Radix primitives
│       └── input.tsx       # Cyberpunk-styled input
├── hooks/
│   └── use-axiom-stream.ts # SSE connection to AG-UI backend
├── lib/
│   ├── axiom-client.ts     # API client (sessions, LLM, secrets, MCP)
│   └── utils.ts            # Utility functions (cn, format, etc.)
├── store/
│   └── sisyphus-store.ts   # Zustand global state management
├── types/
│   └── index.ts            # TypeScript type definitions
├── App.tsx                 # Main app with view switching (chat/settings)
├── main.tsx                # Entry point
└── index.css               # Tailwind + custom theme styles
```

## Application Views

### Chat View (default)
- Left panel: `InteractionStream` - Chat interface
  - **Markdown rendering** with syntax highlighting (ReactMarkdown + remark-gfm)
  - **Tool outputs** display with status, MCP badge, duration, result preview
  - **Agent filter** dropdown to show messages from specific agents
  - **Message collapse** for long agent responses
- Right panel: `WorkflowTopology` - DAG visualization
  - **Node search** by ID or label
  - **Refresh** button to reload DAG from API
  - **Node details panel** with explain (provable/cycle/missing), attestations, proof
  - **Knowledge chain** markdown rendering
  - **n=X e=Y** node/edge count display
  - **SSE integration**: `aevatar.vibe.dag_snapshot`, `aevatar.vibe.dag_updated`
- Panels are resizable via drag handle

### Composer (Enhanced Input)
- **Mode switch**: `chat` / `vibe` / `vibe_loop` (multi-agent research modes)
- **Agent selector**: Send to specific agent(s) or broadcast to all
- **File upload**: Attach files to messages (UI ready, API pending)
- Keyboard submit (Enter)

### Settings View
Activated via Settings button in sidebar. Contains three tabs:

1. **Tools & MCP**
   - Tool statistics (Total, MCP, Skills)
   - Reconnect MCP / Update Skills actions
   - SkillsMP Marketplace button → opens `skills-mp-modal.tsx`
   - Searchable tools list with source filter

2. **LLM Providers**
   - Provider list (Configured, Popular, Other)
   - Provider detail: endpoint, model, API key
   - Test connection, fetch models, save/remove key

3. **Advanced**
   - Raw secrets management (key/value pairs)
   - Direct access to encrypted user secrets store

### SkillsMP Marketplace Modal
- API key authentication status
- Skill search (keyword / AI-search modes)
- Search results with stars, description
- Git-based skill pack installation
- Configuration: packName, repoUrl, ref, skillsSubDir

## Core Modules

### State Management (Zustand)
- `sisyphus-store.ts`: Global state for sessions, workers, DAG, chat, tools
- Actions for real-time updates from event stream

### Event Stream (SSE)
- `use-axiom-stream.ts`: Connects to `/api/sessions/:id/agui/events`
- Handles AG-UI protocol events (TEXT_MESSAGE_*, RUN_*, STEP_*, CUSTOM)
- Auto-updates store on incoming events

### API Client
- `axiom-client.ts`: Unified API interface
  - Session management (create, list, connect)
  - LLM provider configuration (`/api/llm/*`)
  - Secrets management (`/api/secrets/*`)
  - MCP reconnect (`/api/sessions/:id/mcp/reconnect`)
  - Skills sync (`/api/skills/sync`)
  - Tools snapshot (`/api/sessions/:id/tools`)
  - Agent provider mapping (`/api/sessions/:id/agent-providers`)
  - **SkillsMP Marketplace** (`/api/skillsmp/*`)
    - `getSkillsMpStatus()` - API key status
    - `searchSkillsMp()` - keyword/AI search
    - `installSkillPack()` - install from Git repo
  - **DAG APIs**
    - `getDagSnapshot()` - fetch full DAG (`GET /api/sessions/:id/dag`)
    - `getDagNodeExplain()` - node provability (`GET /api/sessions/:id/dag/:nodeId/explain`)
    - `getKnowledgeChain()` - knowledge chain markdown (`GET /api/sessions/:id/graph/:nodeId/chain`)

## Design System

### Colors
- Background: `#0a0e1a` (deep space)
- Surface: `#0f1629`
- Panel: `#141b2d`
- Accent: `#22d3ee` (cyan glow)
- Status: blue (running), emerald (completed), violet (scraping), amber (blocked), red (error)

### Typography
- Primary: Inter (sans-serif)
- Mono: JetBrains Mono (code/metrics)

## Tech Stack
- **Framework**: React 19 + TypeScript
- **Build**: Vite 5
- **Styling**: TailwindCSS 3 + tailwindcss-animate
- **State**: Zustand
- **Animation**: Framer Motion (motion/react)
- **Icons**: Lucide React
- **Primitives**: Radix UI
- **Markdown**: react-markdown + remark-gfm
- **Utilities**: clsx + tailwind-merge (cn)

## UI Skills Constraints

Project follows [ibelick/ui-skills](https://github.com/ibelick/ui-skills) - opinionated constraints for AI agents.

See `.cursorrules` for full ruleset. Key principles:

- **Tailwind First**: Use defaults before custom values
- **Compositor-Only Animation**: Only animate `transform`, `opacity`
- **Accessible Primitives**: Use Radix UI for keyboard/focus behavior
- **No Arbitrary Z-Index**: Use fixed scale
- **h-dvh over h-screen**: Respect mobile viewport
- **prefers-reduced-motion**: Always respect
- **No Gradients/Glow by Default**: Only when explicitly requested
