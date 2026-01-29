# Aevatar.Agents.Sessions

Session management and HTTP APIs for workflow YAML sessions.

## Responsibilities
- Load workflow YAML from disk and resolve roles (Mesh DSL or Cognitive Workflow).
- Persist session state and role mapping.
- Expose session/agent status, history, memory, and trace via HTTP.

## Key features
- Minimal APIs under `/api/sessions` and `/api/workflows`.
- Protobuf-only payloads (`application/x-protobuf`).
- Lazy role agent loading to reduce startup cost.
- Optional memory and trace integration (graceful 404 when unavailable).
- Runtime/stream abstraction in `Aevatar.Agents.Sessions.Runtime`.
- Workflow execution via `SessionRuntime.RunWorkflowAsync`.
- Snapshot-first SSE bootstrap via `ISessionAgUiBootstrapper`.
- Tooling abstraction in `Aevatar.Agents.Tooling`.

## Public API highlights
- `ServiceCollectionExtensions.AddAevatarCognitiveSessions(...)`
- `ServiceCollectionExtensions.AddAevatarSessionRuntime(...)`
- `ServiceCollectionExtensions.AddAevatarSessionTooling(...)`
- `SessionApiEndpoints.MapAevatarSessionApi(...)`
- `SessionUiEndpoints.MapSessionUiEndpoints(...)`
- `CognitiveSessionService`

## Quick start

```csharp
using Aevatar.Agents.Sessions;
using Aevatar.Agents.Sessions.Endpoints;

// Register session services.
services.AddAevatarCognitiveSessions(options =>
{
    options.WorkflowsDirectory = "~/.aevatar/workflows";
    options.LazyLoadRoles = true;
});

// Optional runtime + tooling for AG-UI/SSE.
services.AddAevatarSessionRuntime();
services.AddAevatarSessionTooling();

// Minimal API mapping.
app.MapAevatarSessionApi();
```

## Configuration
- `CognitiveSessionOptions.WorkflowsDirectory` (default: `~/.aevatar/workflows`)
- `CognitiveSessionOptions.LazyLoadRoles` (default: true)

## Dependencies
Required:
- `IGAgentActorManager`
- `IStateStore<SessionState>`
- `RoleAgentFactory`
- `GlobalAgentYamlRegistry`

Optional:
- `IMemoryStore` (enables memory endpoints and session listing)
- `IExecutionTraceStore` (enables `/trace` endpoint if tags contain execution_id)

## Documentation
- API reference: `docs/SESSION_API.md` (repo root)
- Frontend integration: `docs/SESSION_API_FRONTEND.md` (this module)
- Runtime flow: `docs/SESSION_RUNTIME_FLOW.md` (this module)
- Source walkthrough: `docs/SOURCE_WALKTHROUGH.md` (this module)

## Build
```bash
dotnet build src/Aevatar.Agents.Sessions/Aevatar.Agents.Sessions.csproj
```

## Notes
- Session APIs require Protobuf types for all cross-boundary data.
- Workflow YAML supports both Cognitive Mesh DSL (nodes → roles) and Cognitive Workflow (`steps`).
- In non-local runtime, role initialization may not be accessible (best-effort).
