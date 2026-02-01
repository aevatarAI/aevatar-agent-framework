# Aevatar.VibeResearching

An intelligent, privacy-focused research companion powered by the Aevatar Framework and Claude Scientific Skills (MCP).

## 🚀 Overview

This project provides a unified interface for scientific research, allowing you to perform literature reviews, bioinformatics analysis, and data visualization using AI agents.

### Key Features
- **AI Agent**: Specialized `ResearchAgent` capable of using scientific tools.
- **Vibe Researching (NEW)**: Multi-agent inference grounded in **DAG knowledge nodes**, with optional Python verification.
- **Paper Collaboration (File-SSoT)**: Markdown paper writing with `facts_proposed → consensus/verify → DAG`, and file-only agent communication via mailbox.
- **Web Interface ("Lab Notebook")**: React/Tailwind UI for interactive research.
- **CLI**: Rapid prototyping console.
- **Local Privacy**: optional Docker-based execution of scientific tools on your own machine.
- **Real-time**: **AG-UI (snapshot-first SSE)** streaming of messages, runs, steps, and tool usage.

## 📂 Project Structure

- `sisyphus-frontend/`: Frontend (React + Vite).
- `src/VibeResearching/`: Core Agent logic (CLI + agent class).
- `src/VibeResearching.Api/`: Backend API (ASP.NET Core 10.0, AG-UI SSE).
- `src/VibeResearching.Contracts/`: Protobuf contracts for file-based collaboration (mailbox / fact proposals / paper patches).
- `workspace/`: (runtime) session-scoped collaboration workspace (paper/, facts_proposed/, decisions/, mailbox/, runs/, artifacts/).
- `workspace/dags/`: DAG snapshot mirrors for review/debug (artifacts/dag/*).
- `docs/`: Deployment and Integration guides.

## 🏁 Getting Started

### Prerequisites
- .NET 10.0 SDK
- Node.js & npm
- Docker (optional, for local MCP)
- LLM Provider API Key (configured via `LLMProviders`)
  - Recommended: user secrets (encrypted) at `~/.aevatar/secrets.json` (use `src/Aevatar.Agents.SecretsCli`)
  - Optional: per-app override `src/VibeResearching.Api/appsettings.secrets.json` (gitignored)

### Quick Start

1.  **Configure LLM Providers**:
    You have 3 options (recommended order):
    - **In UI (quickest)**: click **"Set API Key"** in the left sidebar after you open the web UI
    - **Secrets App (web)**: `dotnet run --project apps/Aevatar.Config/Aevatar.Config.csproj` then open `http://localhost:6677` (browser-safe; `6667` may be blocked by Chrome)
    - **CLI**: use `src/Aevatar.Agents.SecretsCli` (from env / stdin)
    
    All of them write into user secrets (default `~/.aevatar/secrets.json`, encrypted). You can still override per-app via `src/VibeResearching.Api/appsettings.secrets.json` (see example file).

2.  **(Optional) Enable persistence (MongoDB / SQLite)**:
    - Configure either `MongoDB:*` or `SQLite:*` in `src/VibeResearching.Api/appsettings.json`
    - For SQLite, `SQLite:Enabled=true` will auto-create `workspace/.data/vibe.db`
    - See `docs/CONFIGURATION.md` → "持久化（MongoDB / SQLite）"

3.  **Workflows (Cognitive Session API)**:
    - Project-local workflows live in `src/VibeResearching.Api/workflows/`.
    - Default workflows: `vibe_researching` (planner → reasoner → librarian → verifier → dag_builder → consensus → paper_editor) and `maker`.
    - Mode mapping (POST `/api/sessions/{id}/input`):
      - `chat` -> `vibe_chat`
      - `vibe` / `milestone` / `research` -> `vibe_milestone`
      - `vibe_loop` / `vibe_goal_loop` -> `vibe_goal_loop`
      - `single` -> `vibe_single`
      - `axiom` -> `vibe_axiom`
    - List them via `GET /api/workflows`.
    - Full mapping reference: `src/Aevatar.VibeResearching.Api/workflows/docs/README.md`.
    - Mesh config (`default_mesh.yaml` / `mesh.json`) is separate from Cognitive workflows.

4.  **(Optional) Seed DAG knowledge**:
    - Use the UI or DAG mutation APIs to add initial knowledge nodes.

5.  **(Optional) Choose MCP mode**:
    `src/VibeResearching.Api/appsettings.json` controls whether Claude Scientific Skills runs via hosted MCP or local Docker.

6.  **One-Click Start**:
    ```bash
    ./boot.sh
    ```
    This will launch both the Backend API and the Frontend Web Interface.

7.  **Manual Start (Alternative)**:
    - **Backend**: `cd src/VibeResearching.Api && ASPNETCORE_URLS=http://localhost:5678 dotnet run`
    - **Frontend**: `cd sisyphus-frontend && VITE_API_BASE_URL=http://localhost:5678 npm run dev -- --port 5173`

8.  **Aspire AppHost (Alternative)**:
    - `cd VibeResearching.AppHost && dotnet run`

9.  **Access**: Open `http://localhost:5173`.

### Core Endpoints (AG-UI)

- `GET  /health`
- `GET  /api/info`
- `POST /api/sessions`
- `GET  /api/sessions`
- `POST /api/sessions/{id}/input`
  - body: `{ "message": "...", "mode": "chat" | "vibe" }` (`mode` optional, default `chat`)
- `POST /api/sessions/{id}/facts` (optional; requires `Materials:AllowWrite=true`)
  - creates a **fact proposal** under `workspace/sessions/{id}/facts_proposed/` (NOT directly into DAG)
- `GET  /api/sessions/{id}/workspace` (bounded file-backed snapshot)
- `POST /api/sessions/{id}/facts/{factId}/votes`
- `POST /api/sessions/{id}/facts/{factId}/verifications`
- `POST /api/sessions/{id}/facts/{factId}/promote`
- `GET  /api/sessions/{id}/agui/events` (SSE, snapshot-first)
- `GET  /api/sessions/{id}/agents` (persisted agent list)
- `GET  /api/sessions/{id}/agents/states?includeHistory=false&historyLimit=50`
- `GET  /api/sessions/{id}/agents/{agentId}/history?limit=50`

Frontend integration: `docs/FRONTEND_SESSION_API.md`

## 📚 Documentation

- [Integration Guide (Claude Scientific Skills)](docs/INTEGRATION_GUIDE.md)
- [Architecture Overview](docs/ARCHITECTURE.md)
- [Vibe Researching (Platform Design)](docs/VIBE_RESEARCHING.md)
- [Configuration](docs/CONFIGURATION.md)
- [Development](docs/DEVELOPMENT.md)
