# Claude Agent SDK Provider (`claude_agent_sdk`)

This document explains how to enable **Claude Agent SDK** integration in Aevatar via a dedicated `IAevatarLLMProvider` implementation (`ClaudeAgentSdkProvider`) inside `Aevatar.Agents.AI.LLMTornado`.

> This is **NOT** the same as calling Claude model APIs (Anthropic) via LlmTornado.  
> If you only need Claude model access, use `ProviderType = "anthropic"` / `"claude"` in LLMTornado instead.

## Design boundary (tool-loop)

- Aevatar’s `AIGAgentBase` tool-loop is **not** mapped into Claude Agent SDK.
- `ClaudeAgentSdkProvider` **ignores** `AevatarLLMRequest.Functions` and **never** returns `AevatarFunctionCall`.

Reason: avoid “two orchestrators stacking” (double tool-loop complexity).

## Configuration (`LLMProviders`)

Add a provider with:

- `ProviderType = "claude_agent_sdk"`
- `ProviderSpecificSettings.runnerCommand` + `runnerArgs` (required)

Example (`appsettings.secrets.json` / user-secrets) — **no secrets in repo**:

```json
{
  "LLMProviders": {
    "Default": "claude_agent_sdk_default",
    "Providers": {
      "claude_agent_sdk_default": {
        "Name": "claude_agent_sdk_default",
        "ProviderType": "claude_agent_sdk",
        "Model": "sonnet",
        "TimeoutMilliseconds": 600000,
        "ProviderSpecificSettings": {
          "runnerCommand": "node",
          "runnerArgs": [
            "/abs/path/to/claude_agent_sdk_runner.mjs"
          ],
          "projectRoot": "/abs/path/to/your/project",
          "plugins": [
            "/abs/path/to/plugins"
          ],
          "allowedTools": [
            "filesystem_read",
            "filesystem_write"
          ],
          "permissionMode": "minimal",
          "timeoutMs": 600000,
          "maxOutputChars": 262144
        }
      }
    }
  }
}
```

### Authentication (API key)

Preferred: set `ANTHROPIC_API_KEY` via environment (or secret store).

Also supported (best-effort): if you set `ApiKey` in `LLMProviderConfig`, `ClaudeAgentSdkProvider` will pass it to the runner process as `ANTHROPIC_API_KEY` (and will **not** log it).

### Settings keys

All keys below are read from `ProviderSpecificSettings` (case-insensitive; best-effort conversion from `string/int/bool/JsonElement/list/dict`).

- **runnerCommand** (required): e.g. `node` / `python`
- **runnerArgs** (required): string array of args
- **workingDirectory** (optional): runner process working dir; defaults to `projectRoot` or current dir
- **projectRoot** (optional): forwarded to runner for Claude Agent SDK “project setting source” (loads `.claude/*`)
- **plugins** (optional): array of plugin paths
- **settingSources** (optional): array of setting sources (runner-defined)
- **allowedTools** (optional): allow-list (runner-defined); default empty (minimal permission)
- **permissionMode** (optional): runner-defined permission mode string
- **env** (optional): dictionary of extra env vars for the runner (do not log / do not commit secrets)
- **timeoutMs** (optional): hard timeout for each runner execution
- **maxOutputChars** (optional): tail capture limit for stdout/stderr (runner output is always drained to avoid deadlocks)

## Runner protocol (markers)

The provider is process-based. It sends **one JSON payload** to runner stdin, then reads stdout/stderr.

The runner should print these markers to stdout:

- Streaming delta line (optional):
  - `AEVATAR_AGENT_SDK_STREAM:{text}`
- Final JSON envelope (recommended):
  - `AEVATAR_AGENT_SDK_OUTPUT:{json}`

Minimal JSON envelope contract:

```json
{ "content": "final answer text" }
```

If streaming deltas are present, the provider will forward those deltas and does **not** re-emit the final content to avoid duplication.

## Troubleshooting

- **Runner not found / module missing**: ensure `runnerCommand` exists on PATH and `runnerArgs[0]` points to an existing script/module.
- **Timeout**: increase `timeoutMs` / `TimeoutMilliseconds`; ensure runner is not blocked on interactive permissions.
- **No streaming**: if your runner does not emit `AEVATAR_AGENT_SDK_STREAM:*`, Aevatar will degrade to one chunk (still drains output safely).
- **Port policy**: do NOT use `:5000` in any default config/example. If you ever host a sidecar in the future, **recommend** `:5678` (or any available port).


