# Aevatar.Agents.AI.MEAI

Microsoft.Extensions.AI based provider integration (OpenAI-compatible endpoints, DeepSeek compatibility, streaming).

## Responsibilities
- Implement a concrete LLM provider integration on top of AI.Abstractions/Core.
- Translate framework chat/tool messages to provider-specific payloads (including streaming).

## Key features
- Concrete provider integration (chat + streaming).
- Protocol guardrails for tool calling.

## Public API highlights
- `MEAILLMProviderFactory`
- `MEAIEmbeddingFactory`
- `MEAILLMProvider`
- `LLMTelemetry`
- `ServiceCollectionExtensions`

## NuGet packaging
- **Recommended**: Optional.
- **Why**: This is an adapter/integration module; publish it if you want consumers to opt in without pulling extra dependencies.
- **Packaging note**: keep external dependencies isolated here; core packages should not depend on it.

## Build

```bash
dotnet build src/Aevatar.Agents.AI.MEAI/Aevatar.Agents.AI.MEAI.csproj
```

## Tests

- See `docs/Tests.md` for a coverage review and entry points.

## Documentation

- Project docs: `docs/`
- Repository docs: `docs/` at repo root

## Notes

- Cross-boundary data (state/events/config) should be defined with **Protocol Buffers**.
