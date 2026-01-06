# Claude Agent SDK Provider Demo (mock by default, real optional)

This demo showcases what makes `ProviderType = "claude_agent_sdk"` different from a typical in-process LLM provider:

- **Project-level settings**: `projectRoot` + project files (mock: `.claude/*`; real: `CLAUDE.md` via `settingSources=['project']`)
- **Permissions**: allow-list gating (`allowedTools`) for filesystem read/write
- **Process isolation**: external runner (`node`) instead of in-process model API calls
- **Streaming**: best-effort token streaming via marker protocol
- **Tool-loop boundary**: ignores `Functions` and never returns `AevatarFunctionCall`

Default behavior is **mock/offline** (no network, deterministic). You can switch to **real mode** to run the official Claude Agent SDK.

## Prerequisites

- .NET 10 SDK
- Node.js (needed for both mock and real runner)

## Run (mock / offline default)

```bash
dotnet run --project examples/ClaudeAgentSdkProviderDemo/ClaudeAgentSdkProviderDemo.csproj
```

## What to look for (mock mode)

The demo prints three sections:

1) **BASELINE** (in-process, deterministic)
   - Does not read project settings
   - No runner permissions model

2) **claude_agent_sdk_minimal**
   - `allowedTools` is empty => `filesystem_read` / `filesystem_write` are denied

3) **claude_agent_sdk_full** + **STREAMING**
   - `allowedTools` includes read/write => context read is allowed, and an output file is written
   - Streaming section prints incremental deltas coming from runner markers

In mock mode you should see:
- demo project settings reported as **FOUND**
- `filesystem_write=DENIED` in minimal and `ALLOWED` in full
- a file created at:
  - `demo_project/output/runner_output.txt` (inside the build output directory)

## Switch to real Claude Agent SDK (optional)

Real mode runs `runner/real_claude_agent_sdk_runner.mjs`, which calls the official `@anthropic-ai/claude-agent-sdk` `query()` API.

### 1) Install prerequisites

- Install Claude Code CLI:

```bash
npm install -g @anthropic-ai/claude-code
```

- Install the Agent SDK (in the runner directory):

```bash
cd examples/ClaudeAgentSdkProviderDemo/runner
npm install
```

### 2) Configure auth

Provide **either**:
- `ANTHROPIC_API_KEY` (recommended for CI/headless), **or**
- a Claude Code authenticated session (your local setup)

Never commit secrets.

### 3) Run in real mode

```bash
cd <repo-root>
CLAUDE_AGENT_SDK_DEMO_MODE=real ANTHROPIC_API_KEY=... dotnet run --project examples/ClaudeAgentSdkProviderDemo/ClaudeAgentSdkProviderDemo.csproj
```

### 4) Verify `CLAUDE.md` was loaded

In real mode, `demo_project/CLAUDE.md` forces an observable marker:

- The `claude_agent_sdk_*` output should begin with:
  - `CLAUDE_MD_LOADED`

> Real mode is **non-deterministic** and may incur **API cost**.

## Runner protocol (markers)

Both runners emit:
- `AEVATAR_AGENT_SDK_STREAM:{text}` (optional, when `stream=true`)
- `AEVATAR_AGENT_SDK_OUTPUT:{json}` (final output; includes `{ "content": "..." }`)

See `runner/README.md`.

## Troubleshooting (real mode)

- **`Missing dependency: @anthropic-ai/claude-agent-sdk`**:
  - Run `npm install` in `examples/ClaudeAgentSdkProviderDemo/runner`
- **Auth errors / 401**:
  - Ensure `ANTHROPIC_API_KEY` is set (or Claude Code is logged-in)
- **Permissions hang / prompts**:
  - The demo sets `permissionMode=acceptEdits` for the `claude_agent_sdk_full` provider in real mode
- **RepoRoot auto-detect failed**:
  - Real mode expects running from the repo (so it can locate `examples/ClaudeAgentSdkProviderDemo/runner`)

## Security + Port Policy

- The demo does **not** start any listening service.
- Do **not** use `:5000` in any default config/example. If you ever add a sidecar in the future, use `:5678` by default.

