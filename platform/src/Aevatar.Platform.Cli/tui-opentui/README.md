## Aevatar OpenTUI Frontend

This folder is shipped with the `aevatar` dotnet tool and is executed by `bun` when you run `aevatar tui`.

### Prerequisites

- `bun`
- `zig` (required to build OpenTUI native parts)

See OpenTUI: `https://github.com/anomalyco/opentui`

### Environment contract (provided by the .NET launcher)

- `AEVATAR_CONFIG_DIR`
- `AEVATAR_SESSION_ID`
- `AEVATAR_WORKFLOW`
- `AEVATAR_PROFILE`
- `AEVATAR_CWD`
- `AEVATAR_TUI_BACKEND_URL`

### Run locally (from repo)

```bash
cd platform/src/Aevatar.Platform.Cli/tui-opentui
bun install
bun run start
```

