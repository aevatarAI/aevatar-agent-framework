# Aevatar.Agents.CreativeReasoning

Creative reasoning modules and supporting utilities for higher-level cognitive agents.

## Responsibilities
- Provide higher-level cognitive reasoning modules built on top of core agents.
- Offer reusable reasoning utilities and patterns.

## Key features
- Reasoning utilities built on the agent framework.
- Higher-level cognitive building blocks.

## Public API highlights
- `UoTServiceCollectionExtensions`
- `UoTResult`
- `UoTResultTrace`
- `TUoTResult`
- `TUoTResultTrace`
- `UoTExecutionTraceExtensions`
- `UoTMode`
- `UoTOptions`
- `UoTProgress`
- `UoTPhase`

## NuGet packaging
- **Recommended**: Optional.
- **Why**: This is an adapter/integration module; publish it if you want consumers to opt in without pulling extra dependencies.
- **Packaging note**: keep external dependencies isolated here; core packages should not depend on it.

## Build

```bash
dotnet build src/Aevatar.Agents.CreativeReasoning/Aevatar.Agents.CreativeReasoning.csproj
```

## Tests

- See `docs/Tests.md` for a coverage review and entry points.

## Documentation

- Project docs: `docs/`
- Repository docs: `docs/` at repo root

## Notes

- Cross-boundary data (state/events/config) should be defined with **Protocol Buffers**.
