# Aevatar.Agents.Sessions

Session management and HTTP APIs for cognitive workflows.

## Responsibilities
- Start workflow-based sessions.
- Persist and refresh session state.
- Expose session/agent status, history, memory, and execution trace via HTTP.

## Key features
- Minimal APIs under `/api/sessions` and `/api/workflows`.
- Protobuf-only payloads (`application/x-protobuf`).
- Optional memory and trace integration (graceful 404 when unavailable).

## Public API highlights
- `ServiceCollectionExtensions.AddAevatarCognitiveSessions(...)`
- `SessionApiEndpoints.MapAevatarSessionApi(...)`
- `CognitiveSessionService`

## Quick start

```csharp
using Aevatar.Agents.Cognitive;
using Aevatar.Agents.Sessions;

// Register workflow registry + cognitive agents.
services.AddCognitiveAgents();

// Register session services.
services.AddAevatarCognitiveSessions(options =>
{
    options.DefaultProviderName = "deepseek";
    options.DefaultWorkerCount = 5;
    options.EnableSessionMemory = true;
    options.EnableAgentMemory = false;
    options.RegisterAllWorkflows = true;
});

// Minimal API mapping.
app.MapAevatarSessionApi();
```

## Configuration
- `CognitiveSessionOptions.DefaultProviderName` (default: framework default)
- `CognitiveSessionOptions.DefaultWorkerCount` (default: 5)
- `CognitiveSessionOptions.EnableAgentMemory` (default: false)
- `CognitiveSessionOptions.EnableSessionMemory` (default: true)
- `CognitiveSessionOptions.RegisterAllWorkflows` (default: true)

## Dependencies
Required:
- `IGAgentActorManager`
- `IWorkflowRegistry`
- `IStateStore<SessionState>`

Optional:
- `IMemoryStore` (enables memory endpoints and session listing)
- `IExecutionTraceStore` (enables `/trace` endpoint)

## Documentation
- API reference: `docs/SESSION_API.md` (repo root)
- Frontend integration: `docs/SESSION_API_FRONTEND.md` (this module)

## Build
```bash
dotnet build src/Aevatar.Agents.Sessions/Aevatar.Agents.Sessions.csproj
```

## Notes
- Session APIs require Protobuf types for all cross-boundary data.
- Starting a session requires access to agent instances; some runtimes may not
  support `GetAgent()` and will fail on startup.
