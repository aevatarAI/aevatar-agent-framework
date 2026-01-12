# Aevatar SRA — Obsidian Desktop Plugin (MVP)

This plugin lets you drive **Scientific Research Assistant (SRA)** from inside **Obsidian Desktop**:

- Talks to the SRA backend via **HTTP + AG-UI SSE** (default `http://localhost:5678`)
- Writes artifacts into your current vault under **`SRA/`** (default)
- **Does not store API keys** in plugin settings (use the existing secrets workflows)

## Requirements

- **Obsidian Desktop only** (see `manifest.json`: `isDesktopOnly: true`)
- **Obsidian >= 1.5.0** (see `manifest.json`: `minAppVersion: 1.5.0`)
- Community plugins enabled (disable “Safe mode”) and this plugin enabled

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

Build outputs (important):

- `dist/main.js` (bundled entry)
- `main.js` (copied from `dist/main.js` so Obsidian can load the plugin)
- `styles.css` (Tailwind output; required for the Workbench UI to look correct)

### 3) Install into your vault (recommended: copy only built files)

Target folder (must exist under your vault):

`<YourVault>/.obsidian/plugins/aevatar-sra/`

Copy these files into that folder:

- `manifest.json`
- `main.js`
- `styles.css` (recommended)

Example (macOS/Linux):

```bash
VAULT="/absolute/path/to/YourVault"
PLUGIN_DIR="$VAULT/.obsidian/plugins/aevatar-sra"
mkdir -p "$PLUGIN_DIR"
cp manifest.json main.js styles.css "$PLUGIN_DIR/"
```

> Why not copy the whole repo folder? It contains `node_modules/` and source code which bloats the vault and can break sync setups. Obsidian only needs the built artifacts.

Then open Obsidian → Settings → Community plugins → enable **Aevatar Scientific Research Assistant**.

### 4) Open the UI (this is why you “see nothing” by default)

This plugin **does not auto-open any panel**. You can open it via Command Palette, or click the **left ribbon icon** (test-tube).

Open Obsidian Command Palette (Cmd/Ctrl+P) and run one of:

- **`SRA: Open Workbench`** (recommended, React UI)
- **`SRA: Open Panel`** (simple MVP panel)

The view opens as a tab in the **right sidebar**.

### 5) Configure (optional)

In plugin settings:

- `baseUrl`: default `http://localhost:5678`
- `vaultRoot`: default `SRA`
- `Test Connection`: calls `/health` then `/api/info`

## What the plugin writes into your vault

Default `vaultRoot` is `SRA/`. Layout:

- `SRA/sessions/<sessionId>/runs/<runId>/events.jsonl` (raw AG-UI events)
- `SRA/sessions/<sessionId>/runs/<runId>/run.json` (run metadata)
- `SRA/sessions/<sessionId>/deliverables/deliverables.json`
- `SRA/sessions/<sessionId>/deliverables/brief.md`
- `SRA/sessions/<sessionId>/deliverables/delivery.md`

## Troubleshooting: “Why can’t I see any UI?”

Use this checklist (in order):

1) **You are on Obsidian Mobile** → won’t load (desktop-only plugin).
2) **Plugin not enabled / Safe mode on** → Settings → Community plugins.
3) **Plugin not loaded (wrong files copied)**:
   - Ensure `<YourVault>/.obsidian/plugins/aevatar-sra/manifest.json` exists
   - Ensure `<YourVault>/.obsidian/plugins/aevatar-sra/main.js` exists (not `dist/main.js`)
   - Restart Obsidian and re-enable the plugin
4) **You expect a UI to appear automatically** → it won’t:
   - Cmd/Ctrl+P → run `SRA: Open Workbench` / `SRA: Open Panel`
5) **Commands exist but panel still doesn’t show**:
   - Look at the **right sidebar** (it opens there)
   - Open Developer Tools (Cmd/Ctrl+Opt+I) and check Console for plugin errors

## Dev workflow (watch mode)

You usually want two watchers:

```bash
cd scientific-research-assistant/obsidian-plugin
npm run dev       # JS watch
npm run dev:css   # CSS watch (in another terminal)
```

Then reload Obsidian (Cmd/Ctrl+R) or use a hot-reload plugin.

## Secrets (API keys)

This plugin intentionally does **not** persist API keys.

Use one of:

- Secrets Web UI: run `apps/Aevatar.Secrets.Api` and open `http://localhost:6677`
- Secrets CLI: `src/Aevatar.Agents.SecretsCli`


