# Aevatar.Agents.AGUI

AG-UI protocol integration (standard events + snapshot-first SSE) for building web UIs around agent runs.

## Responsibilities
- Define and implement the AG-UI event stream contract for UI integration.
- Provide snapshot-first reconnect helpers and standard event types.

## Key features
- Standard AG-UI event types.
- Snapshot-first reconnect helpers.
- SSE-friendly JSON defaults.

## Public API highlights
- `AgUiEvent`
- `RunStartedEvent`
- `RunFinishedEvent`
- `RunErrorEvent`
- `StepStartedEvent`
- `StepFinishedEvent`
- `TextMessageStartEvent`
- `TextMessageContentEvent`
- `TextMessageEndEvent`
- `StateSnapshotEvent`

## NuGet packaging
- **Recommended**: Yes (as an independent NuGet package).
- **Why**: This is a foundational module that downstream systems commonly reference.
- **Packaging note**: keep optional integrations (databases/providers) in separate packages.

## Build

```bash
dotnet build src/Aevatar.Agents.AGUI/Aevatar.Agents.AGUI.csproj
```

## Tests

- See `docs/Tests.md` for a coverage review and entry points.

## Documentation

- Project docs: `docs/`
- Repository docs: `docs/` at repo root

## Notes

- Cross-boundary data (state/events/config) should be defined with **Protocol Buffers**.
