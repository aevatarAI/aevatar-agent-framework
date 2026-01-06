# Mock Claude Agent SDK Runner (Demo)

This folder contains a **dependency-free** Node.js runner used by `claude_agent_sdk` provider demo.

## What it does

- Reads one JSON request from stdin
- Loads demo project settings from `projectRoot/.claude/demo_settings.json` (best-effort)
- Loads plugins from `plugins` paths (directory or file paths)
- Enforces an allow-list via `allowedTools`:
  - `filesystem_read` controls reading `demo_project/data/context.txt`
  - `filesystem_write` controls writing `demo_project/output/runner_output.txt`
- Emits marker-based output for the Aevatar provider:
  - `AEVATAR_AGENT_SDK_STREAM:{text}` (optional, when `stream=true`)
  - `AEVATAR_AGENT_SDK_OUTPUT:{json}` (final output; includes `{ "content": "..." }`)

## Run manually

```bash
node mock_claude_agent_sdk_runner.mjs < request.json
```

## Notes

- Offline + deterministic: no network, no randomness.
- No ports are opened (and no `:5000` examples).


