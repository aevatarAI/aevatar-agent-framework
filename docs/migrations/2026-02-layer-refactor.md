# 2026-02 Layer Refactor (Work-in-progress)

This note summarizes the main "building blocks" introduced during the layered refactor.

## Key changes

- `Aevatar.Agents.Runtime.Abstractions`
  - Extracted runtime contracts and `GAgentActorFactoryBase` from `Aevatar.Agents.Runtime`.
  - Added type-forward in `Aevatar.Agents.Runtime` for compatibility.

- `Aevatar.Agents.Sessions.Abstractions`
  - Introduced workflow compilation contracts: `IWorkflowCompiler` + stable DTOs.
  - `Aevatar.Agents.Sessions` no longer references `Aevatar.CognitiveMesh.Dsl` directly.

- `Aevatar.CognitiveMesh.Dsl.WorkflowCompiler`
  - Added `CognitiveMeshWorkflowCompiler` implementing `IWorkflowCompiler`.

- `IAIGAgent`
  - Added `Aevatar.Agents.AI.Abstractions.IAIGAgent` and implemented by `AIGAgentBase`.
  - Sessions runtime now uses `IAIGAgent` where possible (reduces concrete casts).

- Streaming sink registry (replacing static `StreamingContext`)
  - Replaced global static registry with DI-friendly `IStreamChunkSinkRegistry` + `InMemoryStreamChunkSinkRegistry`.
  - Injected into AI agents via `AIGAgentFactory` (best-effort reflection injector).

- Cognitive domain split
  - Extracted strategy/model surface into `Aevatar.Agents.Cognitive.Strategies`.
  - Extracted Vibe Researching domain into `Aevatar.Agents.Cognitive.Researching`.
  - Extracted pivot orchestration into `Aevatar.Agents.Cognitive.Researching.Pivot` (compiled from former app sources).
  - `Aevatar.Agents.Cognitive.Core` no longer compiles the researching domain nor references app projects.

## Notes

- Solution view is reorganized into a layered grouping for navigation; physical paths are not fully migrated yet.
- Boundary assertions are enforced in tests via csproj reference checks.

