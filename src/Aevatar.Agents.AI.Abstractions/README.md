# Aevatar.Agents.AI.Abstractions

AI contracts and configuration (LLMProviders, chat/tool messages) used across AI integrations.

## Responsibilities
- Define AI contracts: provider config (LLMProviders) and chat/tool message schemas.
- Provide stable cross-boundary types (Protobuf-first).

## Key features
- LLMProviders configuration model.
- Chat/Tool message schema for history + tool calls.

## Public API highlights
- `AevatarAIDefaults`
- `AevatarAIAgentConfiguration`
- `LLMProviderConfig`
- `LLMProvidersConfig`
- `LLMEmbeddingConfig`
- `ILLMProviderFactory`
- `LLMProviderFactoryBase`
- `ChatRequest`
- `AevatarParameterDefinition`
- `AevatarLLMProviderBase`

## NuGet packaging
- **Recommended**: Yes (as an independent NuGet package).
- **Why**: This is a foundational module that downstream systems commonly reference.
- **Packaging note**: keep optional integrations (databases/providers) in separate packages.

## Build

```bash
dotnet build src/Aevatar.Agents.AI.Abstractions/Aevatar.Agents.AI.Abstractions.csproj
```

## Tests

- See `docs/Tests.md` for a coverage review and entry points.

## Documentation

- Project docs: `docs/`
- Repository docs: `docs/` at repo root

## Notes

- Cross-boundary data (state/events/config) should be defined with **Protocol Buffers**.
