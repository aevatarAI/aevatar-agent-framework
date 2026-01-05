# Scientific Research Assistant

An intelligent, privacy-focused research companion powered by the Aevatar Framework and Claude Scientific Skills (MCP).

## 🚀 Overview

This project provides a unified interface for scientific research, allowing you to perform literature reviews, bioinformatics analysis, and data visualization using AI agents.

### Key Features
- **AI Agent**: Specialized `ResearchAgent` capable of using scientific tools.
- **Vibe Researching (NEW)**: Multi-agent inference grounded in your local **facts + sources**, with optional Python verification.
- **Web Interface ("Lab Notebook")**: React/Tailwind UI for interactive research.
- **CLI**: Rapid prototyping console.
- **Local Privacy**: optional Docker-based execution of scientific tools on your own machine.
- **Real-time**: **AG-UI (snapshot-first SSE)** streaming of messages, runs, steps, and tool usage.

## 📂 Project Structure

- `frontend/`: Frontend (React + Vite).
- `src/ScientificResearchAssistant/`: Core Agent logic (CLI + agent class).
- `src/ScientificResearchAssistant.Api/`: Backend API (ASP.NET Core 10.0, AG-UI SSE).
- `facts/`: Verified conclusions (write-back target).
- `sources/`: Citable sources (raw materials).
- `docs/`: Deployment and Integration guides.

## 🏁 Getting Started

### Prerequisites
- .NET 10.0 SDK
- Node.js & npm
- Docker (optional, for local MCP)
- LLM Provider API Key (configured via `LLMProviders` in `appsettings.secrets.json`)

### Quick Start

1.  **Configure LLM Providers**:
    Put your keys in `src/ScientificResearchAssistant.Api/appsettings.secrets.json` (see example file).

2.  **(Optional) Add facts / sources**:
    - Put `.md` / `.txt` under `sources/` (subfolders are fine)
    - Put verified conclusions under `facts/`

3.  **(Optional) Choose MCP mode**:
    `src/ScientificResearchAssistant.Api/appsettings.json` controls whether Claude Scientific Skills runs via hosted MCP or local Docker.

4.  **One-Click Start**:
    ```bash
    ./start.sh
    ```
    This will launch both the Backend API and the Frontend Web Interface.

5.  **Manual Start (Alternative)**:
    - **Backend**: `cd src/ScientificResearchAssistant.Api && ASPNETCORE_URLS=http://localhost:5678 dotnet run`
    - **Frontend**: `cd frontend && SRA_API_PROXY_TARGET=http://localhost:5678 npm run dev -- --port 5173`

6.  **Aspire AppHost (Alternative)**:
    - `cd ScientificResearchAssistant.AppHost && dotnet run`

7.  **Access**: Open `http://localhost:5173`.

### Core Endpoints (AG-UI)

- `GET  /health`
- `GET  /api/info`
- `POST /api/sessions`
- `GET  /api/sessions`
- `POST /api/sessions/{id}/input`
  - body: `{ "message": "...", "mode": "chat" | "vibe" }` (`mode` optional, default `chat`)
- `POST /api/sessions/{id}/facts` (optional; requires `Materials:AllowWrite=true`)
- `GET  /api/sessions/{id}/agui/events` (SSE, snapshot-first)

## 📚 Documentation

- [Integration Guide (Claude Scientific Skills)](docs/INTEGRATION_GUIDE.md)
- [Architecture Overview](docs/ARCHITECTURE.md)
- [Vibe Researching (Platform Design)](docs/VIBE_RESEARCHING.md)
- [Configuration](docs/CONFIGURATION.md)
- [Development](docs/DEVELOPMENT.md)
