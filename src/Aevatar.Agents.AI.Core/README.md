# Aevatar.Agents.AI.Core

Core AI agent implementation (AIGAgentBase) with tool calling, hooks, and memory integration.

## Responsibilities
- Provide the core AI agent base (AIGAgentBase) and request building.
- Implement tool calling loop, streaming support, and guardrails.
- Integrate memory layers and hooks.

## Key features
- AIGAgentBase for chat + streaming.
- Tool calling loop with safety controls.
- Hooks pipeline and memory integration.

## Public API highlights
- `AIGAgentBase`
- `ConversationHistoryManager`
- `AIGAgentFactory`
- `IAIAgentEmbeddingFactory`
- `ToolArgumentsJson`
- `AIGAgentKeys`
- `LLMResponseParser`
- `AevatarAgentHookContext`
- `struct`
- `AevatarAgentHookPipeline`

## NuGet packaging
- **Recommended**: Yes (as an independent NuGet package).
- **Why**: This is a foundational module that downstream systems commonly reference.
- **Packaging note**: keep optional integrations (databases/providers) in separate packages.

## Build

```bash
dotnet build src/Aevatar.Agents.AI.Core/Aevatar.Agents.AI.Core.csproj
```

## Tests

- See `docs/Tests.md` for a coverage review and entry points.

## Documentation

- Project docs: `docs/`
- Repository docs: `docs/` at repo root

## Notes

- Cross-boundary data (state/events/config) should be defined with **Protocol Buffers**.
