## MemoryDemo Structure

Purpose: a minimal, end-to-end demo that shows Aevatar’s memory stack (History/Compaction/CQRS/MemoryStore/VectorIndex/Trace→Graph) with a small Web UI.

---

### Directory Tree

```
examples/MemoryDemo/
├── Program.cs
├── MemoryDemoInfrastructure.cs
├── MemoryDemoAgent.cs
├── InMemoryCqrs.cs
├── wwwroot/
│   ├── index.html
│   ├── app.js
│   └── styles.css
├── appsettings.json
├── appsettings.secrets.json
└── docs/
    └── STRUCTURE.md
```

---

### File Responsibilities

- **`Program.cs`**: Web host + HTTP API surface (chat, memory panels, vector search, trace/graph, tools). Keeps top-level startup wiring only.
- **`MemoryDemoInfrastructure.cs`**:
  - DTOs + runtime (`MemoryDemoRuntime`, `MemoryDemoPaths`)
  - Knowledge Base (book) APIs (`/api/kb/*`): ingest book → chunk → MemoryStore/VectorIndex, and select active book for the agent.
- **`MemoryDemoAgent.cs`**:
  - Demo agent built on `AIGAgentBase`
  - Enables `State.History` + compaction (rolling `history_summary`)
  - Keeps tool set focused: `search_memory` + `query_state`
  - Knowledge base selection via `State.Context["kb_memory_id"]`, injected into the system prompt so the agent retrieves from the selected book before answering.
- **`InMemoryCqrs.cs`**: demo CQRS projector/index/query service (in-memory) to simulate projected read-model lookup.
- **`wwwroot/*`**: minimal UI that calls the HTTP APIs and visualizes state/history summary/CQRS/long-term memory/vector/trace/graph.

---

### Key APIs (High-level)

- **Chat**
  - `POST /api/chat`: non-streaming (legacy/fallback)
  - `POST /api/chat/stream`: streaming NDJSON (`start`/`delta`/`end`)
- **Knowledge Base**
  - `POST /api/kb/ingest`: ingest a book into `tenant::<bookId>`
  - `POST /api/kb/select`: select/clear active book for chat
  - `GET /api/kb/status`: current selection
  - `GET /api/kb/resources`: list tenant-scoped memory resources


