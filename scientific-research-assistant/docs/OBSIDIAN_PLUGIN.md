# Obsidian Desktop Plugin (Aevatar SRA)

## What it is

The **Aevatar SRA Obsidian plugin** is a thin desktop host that renders the **shared SRA Workbench UI** (React) inside Obsidian, connects to `ScientificResearchAssistant.Api` (SRA backend) via **HTTP + AG-UI SSE**, and writes all artifacts back into your current Obsidian vault under `SRA/`.

Design goals:

- Desktop-first (Electron)
- Future remote-friendly: plugin uses a configurable `baseUrl` and avoids local-only backend file APIs
- No API key storage in the plugin (use existing secrets workflows)

## Shared UI Core (UI parity)

This repo uses a **shared UI core** to keep Web and Obsidian behavior identical:

- `scientific-research-assistant/ui/`
  - `SraWorkbenchApp`: the full workbench UI (chat + panels + files/dag views)
  - `SraTransport`: host abstraction (HTTP/SSE/uploads + capability gating)
- Web host: `scientific-research-assistant/frontend/` injects `WebTransport`
- Obsidian host: `scientific-research-assistant/obsidian-plugin/` injects `ObsidianTransport`

## Folder layout in vault (write-back target)

All files are created under:

`<YourVault>/SRA/`

Example layout (MVP):

```
SRA/
  sessions/
    {sessionId}/
      runs/
        {runId}/
          events.jsonl
          run.json
      deliverables/
        deliverables.json
        brief.md
        delivery.md
```

## Running the sidecar (SRA backend)

```bash
cd scientific-research-assistant/src/ScientificResearchAssistant.Api
ASPNETCORE_URLS=http://localhost:5678 dotnet run
```

Verify:

- `http://localhost:5678/health`
- `http://localhost:5678/api/info`

> Repo policy reminder: do not use `:5000` in defaults/examples.

## Installing the plugin

Build:

```bash
cd scientific-research-assistant/obsidian-plugin
npm install
npm run build
```

Install:

- Copy (or symlink) `scientific-research-assistant/obsidian-plugin/` to:
  - `<YourVault>/.obsidian/plugins/aevatar-sra/`
- Ensure the folder contains:
  - `manifest.json`
  - `main.js`

Enable it in Obsidian Desktop:

- Settings → Community plugins → enable **Aevatar Scientific Research Assistant**

## Secrets (API keys)

The plugin does **not** store API keys.

Use one of:

- Secrets Web UI: `apps/Aevatar.Secrets.Api` → `http://localhost:6677`
- Secrets CLI: `src/Aevatar.Agents.SecretsCli`

## Capability gating (local-only vs remote baseUrl)

Some backend APIs are intentionally **localhost-only** for safety (e.g. secrets, local file editing). The shared UI reads `transport.capabilities` to decide what to show/enable:

- **filesApi**: enables Files UI (local workspace file browsing/editing)
- **secretsEnabled / revealApiKey**: enables provider/key management + key reveal UX

When `baseUrl` is not loopback, these features are disabled and the UI shows a clear “local-only” message.


