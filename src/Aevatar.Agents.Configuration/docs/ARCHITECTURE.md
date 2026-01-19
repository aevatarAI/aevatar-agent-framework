# Aevatar.Agents.Configuration — Configuration Helpers

Goal: provide shared helpers for locating and enumerating `~/.aevatar` resources.

## Structure

```
src/Aevatar.Agents.Configuration/
├── AevatarConfigDirectory.cs  # enum for subdirectories
├── AevatarConfigFileHelper.cs # resolve/list files under ~/.aevatar
└── docs/ARCHITECTURE.md
```

## Responsibilities

- `AevatarConfigDirectory`: declares well-known config subdirectories.
- `AevatarConfigFileHelper`: resolves paths and lists files using the enum.

## Decisions

- Keep helpers free of AI/tool dependencies.
- Use enum-based selection to avoid string literals.

## Change Log

- 2026-01-18: split from `Aevatar.Agents.Core` into its own project.
