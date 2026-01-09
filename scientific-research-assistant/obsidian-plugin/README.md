# Aevatar SRA — Obsidian Desktop Plugin (MVP)

This plugin lets you drive **Scientific Research Assistant (SRA)** from inside Obsidian Desktop:

- Talks to the SRA backend via **HTTP + AG-UI SSE** (default `http://localhost:5678`)
- Writes artifacts into your current vault under **`SRA/`** (default)
- **Does not store API keys** in plugin settings (use the existing secrets workflows)

## Quickstart (Desktop)

### 1) Start the SRA backend (sidecar)

From repo root:

```bash
cd scientific-research-assistant/src/ScientificResearchAssistant.Api
ASPNETCORE_URLS=http://localhost:5678 dotnet run
```

Verify:

- `http://localhost:5678/health`
- `http://localhost:5678/api/info`

> Repo policy: do not use `:5000` in defaults/examples.

### 2) Build the plugin

```bash
cd scientific-research-assistant/obsidian-plugin
npm install
npm run build
```

The build outputs:

- `dist/main.js`
- `main.js` (copied from `dist/main.js` so Obsidian can load the plugin)

### 3) Install into your vault

Copy (or symlink) this folder into:

`<YourVault>/.obsidian/plugins/aevatar-sra/`

Minimum required files in that target folder:

- `manifest.json`
- `main.js`

Then open Obsidian → Settings → Community plugins → enable **Aevatar Scientific Research Assistant**.

### 4) Configure

In plugin settings:

- `baseUrl`: default `http://localhost:5678`
- `vaultRoot`: default `SRA`

## Secrets (API keys)

This plugin intentionally does **not** persist API keys.

Use one of:

- Secrets Web UI: run `apps/Aevatar.Secrets.Api` and open `http://localhost:6677`
- Secrets CLI: `src/Aevatar.Agents.SecretsCli`


