# ClaudeAgentSdkProviderDemo Runner

This folder contains **two** runner entrypoints used by `ProviderType = "claude_agent_sdk"` demo:

- `mock_claude_agent_sdk_runner.mjs`: offline + deterministic, no deps
- `real_claude_agent_sdk_runner.mjs`: calls the official Claude Agent SDK (`@anthropic-ai/claude-agent-sdk`)

Both runners implement the same stdout marker protocol so the .NET provider can parse output:

- `AEVATAR_AGENT_SDK_STREAM:{text}` (optional, when `stream=true`)
- `AEVATAR_AGENT_SDK_OUTPUT:{json}` (final output; must include `{ "content": "..." }`)

## Mock runner (offline)

What it does:

- Reads one JSON request from stdin
- Loads demo project settings from `projectRoot/.claude/demo_settings.json` (best-effort)
- Loads demo plugins from `plugins` paths (directory or file paths)
- Enforces an allow-list via `allowedTools`:
  - `filesystem_read` controls reading `demo_project/data/context.txt`
  - `filesystem_write` controls writing `demo_project/output/runner_output.txt`

Run:

```bash
node mock_claude_agent_sdk_runner.mjs < request.json
```

## Real runner (Claude Agent SDK)

Prerequisites:

```bash
npm install -g @anthropic-ai/claude-code
cd examples/ClaudeAgentSdkProviderDemo/runner
npm install
```

Run:

```bash
ANTHROPIC_API_KEY=... node real_claude_agent_sdk_runner.mjs < request.json
```

Notes:

- Real runner is **non-deterministic** and may incur **API cost**.
- No ports are opened (and no `:5000` examples).

