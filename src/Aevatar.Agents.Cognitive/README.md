# Aevatar.Agents.Cognitive

Cognitive agent and reasoning utilities built on Aevatar Agent Framework.

## Responsibilities
- Provide higher-level cognitive reasoning modules built on top of core agents.
- Offer reusable reasoning utilities and patterns.

## Key features
- Reasoning utilities built on the agent framework.
- Higher-level cognitive building blocks.

## Public API highlights
- `IOutputParser`
- `OutputParserFactory`
- `TextOutputParser`
- `FirstLineOutputParser`
- `CodeBlockOutputParser`
- `JsonOutputParser`
- `JsonArrayOutputParser`
- `RegexOutputParser`
- `FallbackOutputParser`
- `TemplateEngine`

## NuGet packaging
- **Recommended**: Optional.
- **Why**: This is an adapter/integration module; publish it if you want consumers to opt in without pulling extra dependencies.
- **Packaging note**: keep external dependencies isolated here; core packages should not depend on it.

## Build

```bash
dotnet build src/Aevatar.Agents.Cognitive/Aevatar.Agents.Cognitive.csproj
```

## Tests

- See `docs/Tests.md` for a coverage review and entry points.

## Documentation

- Project docs: `docs/`
- Repository docs: `docs/` at repo root

## Notes

- Cross-boundary data (state/events/config) should be defined with **Protocol Buffers**.
