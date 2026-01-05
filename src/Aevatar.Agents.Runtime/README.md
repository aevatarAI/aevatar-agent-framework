# Aevatar.Agents.Runtime

Shared runtime layer and primitives that multiple runtimes (Local/Orleans/ProtoActor) build upon.

## Responsibilities
- Shared runtime primitives used by runtime adapters.
- Common stream/subscription building blocks and lifecycle helpers.

## Key features
- Runtime primitives shared across runtimes.

## Public API highlights
- `GAgentActorFactoryBase`

## NuGet packaging
- **Recommended**: Yes (as an independent NuGet package).
- **Why**: This is a foundational module that downstream systems commonly reference.
- **Packaging note**: keep optional integrations (databases/providers) in separate packages.

## Build

```bash
dotnet build src/Aevatar.Agents.Runtime/Aevatar.Agents.Runtime.csproj
```

## Tests

- See `docs/Tests.md` for a coverage review and entry points.

## Documentation

- Project docs: `docs/`
- Repository docs: `docs/` at repo root

## Notes

- Cross-boundary data (state/events/config) should be defined with **Protocol Buffers**.
