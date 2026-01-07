/**
 * Mock Claude Agent SDK runner (offline, deterministic, no deps).
 *
 * Reads ONE JSON request from stdin and prints marker-based output:
 * - AEVATAR_AGENT_SDK_STREAM:{text}
 * - AEVATAR_AGENT_SDK_OUTPUT:{json}
 *
 * This is a demo runner to showcase:
 * - projectRoot + .claude/* reading
 * - plugins loading
 * - allowedTools permission gating (filesystem_read / filesystem_write)
 * - streaming marker protocol
 *
 * NOTE: This does NOT call any network or real LLM.
 */

import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const STREAM_MARKER = "AEVATAR_AGENT_SDK_STREAM:";
const OUTPUT_MARKER = "AEVATAR_AGENT_SDK_OUTPUT:";

function readStdinAll() {
  return new Promise((resolve, reject) => {
    let data = "";
    process.stdin.setEncoding("utf8");
    process.stdin.on("data", (chunk) => (data += chunk));
    process.stdin.on("end", () => resolve(data));
    process.stdin.on("error", reject);
  });
}

function safeJsonParse(text, defaultValue) {
  try {
    return JSON.parse(text);
  } catch {
    return defaultValue;
  }
}

function normalizeList(v) {
  if (!v) return [];
  if (Array.isArray(v)) return v.filter((x) => typeof x === "string");
  if (typeof v === "string") return [v];
  return [];
}

function normalizeAllowedTools(v) {
  return new Set(normalizeList(v));
}

function resolvePath(p, baseDir) {
  if (!p) return "";
  if (path.isAbsolute(p)) return p;
  return path.resolve(baseDir, p);
}

function readClaudeSettings(projectRoot) {
  const settingsPath = path.join(projectRoot, ".claude", "demo_settings.json");
  if (!fs.existsSync(settingsPath)) {
    return { exists: false, settings: null, settingsPath };
  }

  const raw = fs.readFileSync(settingsPath, "utf8");
  const obj = safeJsonParse(raw, null);
  return { exists: true, settings: obj, settingsPath };
}

async function loadPlugins(pluginRoots) {
  // Each plugin root can be:
  // - a directory containing *.mjs plugins
  // - a direct file path to an ESM plugin
  const plugins = [];

  for (const root of pluginRoots) {
    const abs = path.resolve(root);
    if (!fs.existsSync(abs)) {
      throw new Error(`Plugin path not found: ${abs}`);
    }

    const stat = fs.statSync(abs);
    if (stat.isDirectory()) {
      const files = fs
        .readdirSync(abs)
        .filter((f) => f.endsWith(".mjs"))
        .sort();
      for (const f of files) {
        plugins.push(path.join(abs, f));
      }
    } else {
      plugins.push(abs);
    }
  }

  const loaded = [];
  for (const pluginPath of plugins) {
    // Use file URL for dynamic import.
    const mod = await import(pathToFileURL(pluginPath).href);
    if (typeof mod.transform !== "function") {
      throw new Error(`Plugin missing export "transform(text, ctx)": ${pluginPath}`);
    }
    loaded.push({ pluginPath, transform: mod.transform });
  }

  return loaded;
}

function applyPlugins(text, ctx, loadedPlugins) {
  let out = text;
  for (const p of loadedPlugins) {
    out = p.transform(out, { ...ctx, pluginPath: p.pluginPath });
  }
  return out;
}

function maybeReadContextFile(projectRoot, allowedTools) {
  const filePath = path.join(projectRoot, "data", "context.txt");
  if (!allowedTools.has("filesystem_read")) {
    return {
      ok: false,
      reason: "filesystem_read not allowed (default deny)",
      filePath,
      content: null,
    };
  }

  if (!fs.existsSync(filePath)) {
    return {
      ok: false,
      reason: "context file missing",
      filePath,
      content: null,
    };
  }

  return {
    ok: true,
    reason: null,
    filePath,
    content: fs.readFileSync(filePath, "utf8"),
  };
}

function maybeWriteOutputFile(projectRoot, allowedTools, content) {
  const outDir = path.join(projectRoot, "output");
  const outPath = path.join(outDir, "runner_output.txt");

  if (!allowedTools.has("filesystem_write")) {
    return { ok: false, reason: "filesystem_write not allowed (default deny)", outPath };
  }

  fs.mkdirSync(outDir, { recursive: true });
  fs.writeFileSync(outPath, content, "utf8");
  return { ok: true, reason: null, outPath };
}

function emitStreamLines(lines) {
  for (const line of lines) {
    process.stdout.write(`${STREAM_MARKER}${line}\n`);
  }
}

function emitFinal(content, meta) {
  const payload = {
    content,
    meta,
  };
  process.stdout.write(`${OUTPUT_MARKER}${JSON.stringify(payload)}\n`);
}

async function main() {
  const raw = await readStdinAll();
  const req = safeJsonParse(raw, {});

  const stream = !!req.stream;
  const projectRoot = typeof req.projectRoot === "string" ? req.projectRoot : "";
  const pluginsCfg = normalizeList(req.plugins);
  const allowedTools = normalizeAllowedTools(req.allowedTools);

  // Resolve plugin roots relative to projectRoot (if provided) to keep demo deterministic.
  const baseForPaths = projectRoot || process.cwd();
  const pluginRootsAbs = pluginsCfg.map((p) => resolvePath(p, baseForPaths));

  const claude = readClaudeSettings(projectRoot);
  const ctxRead = maybeReadContextFile(projectRoot, allowedTools);

  const loadedPlugins = await loadPlugins(pluginRootsAbs);

  const persona = claude.settings?.persona ?? "(no persona)";
  const strategy = claude.settings?.strategy ?? "(no strategy)";

  const baseText =
    [
      "== Mock Claude Agent SDK Runner ==",
      `projectRoot=${projectRoot}`,
      `claudeSettings=${claude.exists ? "FOUND" : "MISSING"} (${claude.settingsPath})`,
      `persona=${persona}`,
      `strategy=${strategy}`,
      `pluginsLoaded=${loadedPlugins.length}`,
      `allowedTools=${Array.from(allowedTools).sort().join(",") || "(none)"}`,
      `filesystem_read=${ctxRead.ok ? "ALLOWED" : "DENIED"} (${ctxRead.filePath})`,
      ctxRead.ok ? `contextPreview=${ctxRead.content.trim().slice(0, 60)}` : `denyReason=${ctxRead.reason}`,
    ].join("\n") + "\n";

  const transformed = applyPlugins(baseText, { request: req }, loadedPlugins);

  const write = maybeWriteOutputFile(projectRoot, allowedTools, transformed);

  const finalText =
    transformed +
    "\n" +
    [
      "== Write Result ==",
      `filesystem_write=${write.ok ? "ALLOWED" : "DENIED"}`,
      `outputPath=${write.outPath}`,
      write.ok ? "writeOk=true" : `denyReason=${write.reason}`,
    ].join("\n") +
    "\n";

  if (stream) {
    // Stream in a few deterministic chunks.
    const lines = [
      "[stream] hello",
      "[stream] .claude + plugins + permissions demo",
      "[stream] (this is mock, offline, deterministic)",
    ];
    emitStreamLines(lines);
  }

  emitFinal(finalText, {
    runner: path.join(__dirname, "mock_claude_agent_sdk_runner.mjs"),
    node: process.version,
  });
}

main().catch((err) => {
  // Keep stderr short and actionable.
  const msg = err && err.stack ? err.stack : String(err);
  process.stderr.write(`[mock-runner-error] ${msg}\n`);
  process.exitCode = 1;
});


