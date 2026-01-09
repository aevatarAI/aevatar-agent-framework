# Obsidian Desktop Plugin (Aevatar SRA)

## What it is

The **Aevatar SRA Obsidian plugin** is a thin desktop UI that connects to `ScientificResearchAssistant.Api` (SRA backend) via **HTTP + AG-UI SSE**, and writes all artifacts back into your current Obsidian vault under `SRA/`.

Design goals:

- Desktop-first (Electron)
- Future remote-friendly: plugin uses a configurable `baseUrl` and avoids local-only backend file APIs
- No API key storage in the plugin (use existing secrets workflows)

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


