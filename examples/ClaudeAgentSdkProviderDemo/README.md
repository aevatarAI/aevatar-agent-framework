# Claude Agent SDK Provider Demo (offline/mock)

This demo showcases what makes `ProviderType = "claude_agent_sdk"` different from a typical in-process LLM provider:

- **Project-level settings**: `projectRoot + .claude/*` (File-SSoT style)
- **Plugins**: plugin loading + deterministic transforms
- **Permissions**: allow-list gating (`allowedTools`) for filesystem read/write
- **Process isolation**: external runner (`node`) instead of in-process model API calls
- **Streaming**: best-effort token streaming via marker protocol
- **Tool-loop boundary**: ignores `Functions` and never returns `AevatarFunctionCall`

> This demo is **offline** and does **not** call any real network / model API.

## Prerequisites

- .NET 10 SDK
- Node.js (for the mock runner)

If Node.js is missing, the baseline section will still run, and the demo will print an actionable error for `claude_agent_sdk`.

## Run

```bash
cd examples/ClaudeAgentSdkProviderDemo
dotnet run
```

You can also run from repo root:

```bash
dotnet run --project examples/ClaudeAgentSdkProviderDemo/ClaudeAgentSdkProviderDemo.csproj
```

## What to look for in output

The demo prints three sections:

1) **BASELINE** (in-process, deterministic)
   - Does not read `.claude`
   - Does not load plugins
   - No runner permissions model

2) **claude_agent_sdk_minimal**
   - `allowedTools` is empty => `filesystem_read` / `filesystem_write` are denied
   - Shows deny reasons

3) **claude_agent_sdk_full** + **STREAMING**
   - `allowedTools` includes read/write => context read is allowed, and an output file is written
   - Streaming section prints incremental deltas coming from runner markers

You should see:
- `.claude` settings reported as **FOUND**
- `pluginsLoaded=2`
- `filesystem_write=DENIED` in minimal and `ALLOWED` in full
- a file created under:
  - `demo_project/output/runner_output.txt` (inside the build output directory)

## Runner protocol (markers)

The mock runner emits:
- `AEVATAR_AGENT_SDK_STREAM:{text}` (when `stream=true`)
- `AEVATAR_AGENT_SDK_OUTPUT:{json}` (always; contains `{ "content": "..." }`)

See `runner/README.md` and `runner/mock_claude_agent_sdk_runner.mjs`.

## Swap to a real Claude Agent SDK runner (optional)

This demo uses a mock runner. To use a real runner:

- Keep `ProviderType = "claude_agent_sdk"`
- Update `ProviderSpecificSettings.runnerCommand` / `runnerArgs` to your real runner entrypoint
- Provide `ANTHROPIC_API_KEY` via environment or secret store
- Do **not** commit secrets to the repo

## Security + Port Policy

- The demo does **not** start any listening service.
- Do **not** use `:5000` in any default config/example. If you ever add a sidecar in the future, use `:5678` by default.


