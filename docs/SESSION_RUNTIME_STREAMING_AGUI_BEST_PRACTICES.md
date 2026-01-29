# Session Runtime Streaming + AG-UI Best Practices (Workshop + Vibe)

This document describes the baseline Workshop path and the Vibe-grade
enhancements for snapshot-first streaming and workflow execution. It is the
recommended reference for apps using `SessionRuntime` and JSON endpoints.

## Scope

- `examples/Aevatar.Workshop`
- `apps/Aevatar.VibeResearching`
- `src/Aevatar.Agents.Sessions/Runtime/*`
- JSON APIs under `/api/chat/*`
- SSE under `/api/chat/sessions/{sessionId}/agui/events`

## End-to-end flow (Workshop baseline)

1. List workflows:
   - `GET /api/chat/workflows`
2. Create session:
   - `GET /api/chat/sessions/new?workflow={name}`
   - `SessionRuntime.CreateSessionAsync` ensures context + stream binding.
3. Open SSE (before sending chat):
   - `GET /api/chat/sessions/{sessionId}/agui/events`
   - Server emits snapshot-first bootstrap (e.g., `MESSAGES_SNAPSHOT`),
     then `CUSTOM:SSE_CONNECTED` as a legacy handshake.
4. Send chat:
   - `POST /api/chat/sessions/{sessionId}/input`
   - `SessionRuntime.SendChatAsync` dispatches in background.
5. Receive AG-UI events:
   - `SessionAgUiStream` subscribes to agent stream and projects events.
6. Render UI:
   - `TEXT_MESSAGE_*` for streaming messages.
   - `RUN_*`, `STEP_*`, `TOOL_CALL_*`, `CUSTOM` for timeline and status.

## Agent access and inspection

Workshop UI uses session-scoped endpoints that resolve the primary agent:

- `GET /api/agent/handlers?sessionId=...`
- `GET /api/agent/state?sessionId=...`
- `GET /api/agent/memory?sessionId=...`
- `POST /api/agent/settings`

These APIs call `SessionRuntime.GetPrimaryAgentAsync` internally, so the UI does
not need a direct agent id.

## Chat request payload (JSON)

`POST /api/chat/sessions/{sessionId}/input`

```json
{
  "requestId": "web:session:timestamp",
  "userId": "workshop-ui",
  "message": "hello",
  "context": { "sessionId": "..." },
  "timestamp": "2026-01-01T00:00:00Z",
  "temperature": 0.7,
  "maxTokens": 1024,
  "streamChunkEveryN": 4
}
```

Notes:
- `streamChunkEveryN` is clamped to 1..64 by `SessionRuntime`.
- `requestId` is used to build `messageId` and run correlation ids.
- `context.sessionId` is forwarded to the agent for session binding.

## Backend best practices (Baseline)

- Use `SessionRuntime.SendChatAsync` to decouple chat from HTTP cancellation.
- Publish minimal `CUSTOM:SESSION_STATUS` milestones for UI observability.
- Prefer snapshot-first bootstrap (`MESSAGES_SNAPSHOT`).
- Keep `SSE_CONNECTED` as a legacy readiness handshake.
- Prefer `SessionAgUiStream` for projection (ExecutionTrace -> AG-UI).
- SSE headers should include:
  - `Content-Type: text/event-stream; charset=utf-8`
  - `Cache-Control: no-store`
  - `X-Accel-Buffering: no`

## Front-end best practices (Baseline)

- Treat `MESSAGES_SNAPSHOT` as ready; fall back to `CUSTOM:SSE_CONNECTED`.
- Use `messageId` as the stable key and accumulate deltas per message.
- Keep the backend status log bounded (Workshop keeps last 120 lines).
- Use `streamChunkEveryN` to control UI update frequency.
- On session switch, close the current SSE connection and reset buffers.

## AG-UI events to handle

Core:
- `TEXT_MESSAGE_START`, `TEXT_MESSAGE_CONTENT`, `TEXT_MESSAGE_END`
- `RUN_STARTED`, `RUN_FINISHED`, `RUN_ERROR`
- `STEP_STARTED`, `STEP_FINISHED`
- `TOOL_CALL_START`, `TOOL_CALL_ARGS`, `TOOL_CALL_RESULT`, `TOOL_CALL_END`

Custom:
- `SSE_CONNECTED` (handshake)
- `SESSION_STATUS` / `WORKSHOP_STATUS` (backend status log)
- `aevatar.llm.trace` (LLM trace info)
- `aevatar.workflow.execution_event` (workflow step payloads)

## Workflow execution (Vibe-grade)

When running Cognitive workflows:

- `POST /api/chat/sessions/{sessionId}/workflow/run`
- `SessionRuntime.RunWorkflowAsync` orchestrates `CognitiveCoordinatorGAgent`
  and emits `RUN_*`, `STEP_*`, and `CUSTOM` (via `ExecutionTraceEvent`).
- Workflow YAML should define `steps` and optional `agent` overrides
  (e.g., `vibe_researching` in VibeResearching).

## Snapshot-first bootstrap (Vibe-grade)

Use `ISessionAgUiBootstrapper` to push rich snapshots before live SSE:

- Default: `DefaultSessionAgUiBootstrapper` emits `MESSAGES_SNAPSHOT`
  from primary agent history.
- Vibe: `VibeAgUiBootstrapper` emits:
  - `aevatar.vibe.message_meta_snapshot`
  - `aevatar.ui.tools_snapshot`
  - `aevatar.ui.run_steps_snapshot`
  - `aevatar.vibe.brief_snapshot`
  - `aevatar.vibe.dag_snapshot`
  - `aevatar.vibe.trace_snapshot`
  - `aevatar.vibe.agents_snapshot`
  - `aevatar.vibe.agent_providers_snapshot`

## App-defined status events

Two supported patterns:

1. Publish `CustomEvent` directly from the app layer (endpoint/service).
2. Publish `ExecutionTraceEvent` from an event module and let
   `AgUiTraceProjector` map it to AG-UI.

For event modules, use `extensions.event_modules` and `extensions.event_routes`
in role YAML (see `src/Aevatar.Agents.AI.Core/docs/EVENT_MODULES.md`).

## References

- `examples/Aevatar.Workshop/wwwroot/app-sessions.js`
- `src/Aevatar.Agents.Sessions/Endpoints/SessionUiEndpoints.cs`
- `src/Aevatar.Agents.Sessions/Runtime/SessionRuntime.cs`
- `src/Aevatar.Agents.Sessions/Runtime/SessionAgUiStream.cs`
- `src/Aevatar.Agents.Sessions/Runtime/SessionAgUiBootstrapper.cs`
- `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching.Api/Sessions/VibeAgUiBootstrapper.cs`
- `src/Aevatar.Agents.Sessions/docs/SESSION_RUNTIME_FLOW.md`
