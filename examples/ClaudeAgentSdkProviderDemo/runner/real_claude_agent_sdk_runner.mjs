/**
 * Real Claude Agent SDK runner (requires network + auth; NOT used by default).
 *
 * Purpose:
 * - Invoke the official Claude Agent SDK (`@anthropic-ai/claude-agent-sdk`) in headless mode.
 * - Emit Aevatar marker protocol on stdout so `ClaudeAgentSdkProvider` can parse it:
 *   - AEVATAR_AGENT_SDK_STREAM:{text}
 *   - AEVATAR_AGENT_SDK_OUTPUT:{json}
 *
 * IMPORTANT:
 * - Do NOT print secrets.
 * - Do NOT bypass permissions by default.
 * - Do NOT open any ports.
 */

import fs from "node:fs";
import path from "node:path";

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

function normalizeStringList(v) {
  if (!v) return [];
  if (Array.isArray(v)) return v.filter((x) => typeof x === "string");
  if (typeof v === "string") return [v];
  return [];
}

function sanitizeForStream(text) {
  // Marker protocol is line-based; keep each STREAM line to a single line.
  // (Final OUTPUT JSON carries full content with newlines.)
  return String(text ?? "").replace(/\r?\n/g, " ");
}

function emitStream(text) {
  const s = sanitizeForStream(text);
  if (!s) return;
  process.stdout.write(`${STREAM_MARKER}${s}\n`);
}

function emitFinal(content, meta) {
  // Must be the last meaningful stdout output: protocol parser extracts from last marker to EOF.
  const payload = { content: String(content ?? ""), meta };
  process.stdout.write(`${OUTPUT_MARKER}${JSON.stringify(payload)}\n`);
}

function extractAssistantText(msg) {
  const blocks = msg?.message?.content;
  if (!Array.isArray(blocks)) return "";

  return blocks
    .filter((b) => b && b.type === "text" && typeof b.text === "string")
    .map((b) => b.text)
    .join("");
}

function extractTextDeltaFromStreamEvent(msg) {
  // Best-effort for Anthropic streaming event shapes.
  const ev = msg?.event;
  if (!ev || typeof ev !== "object") return "";

  const delta = ev.delta;
  if (delta && typeof delta === "object") {
    if (typeof delta.text === "string") return delta.text;
    if (delta.type === "text_delta" && typeof delta.text === "string") return delta.text;
  }

  if (typeof ev.text === "string") return ev.text;
  return "";
}

function mapAllowedTools(reqAllowedTools) {
  // Demo config uses filesystem_read/filesystem_write. Claude Agent SDK expects Claude Code tool names.
  // Pass through already-correct tool names, and map demo names best-effort.
  const raw = normalizeStringList(reqAllowedTools);
  const out = new Set();

  for (const t of raw) {
    const tool = String(t);
    if (!tool) continue;

    if (tool === "filesystem_read") {
      out.add("Read");
      continue;
    }
    if (tool === "filesystem_write") {
      out.add("Write");
      out.add("Edit");
      continue;
    }

    // Pass-through (e.g., Read/Write/Edit already set by user).
    out.add(tool);
  }

  return Array.from(out);
}

function buildPrompt(req) {
  const messages = Array.isArray(req?.messages) ? req.messages : [];

  // Prefer the last user message as the prompt.
  for (let i = messages.length - 1; i >= 0; i--) {
    const m = messages[i];
    if (m && m.role === "user" && typeof m.content === "string" && m.content.trim()) {
      return m.content;
    }
  }

  // Fallback: concatenate all message content.
  const parts = [];
  for (const m of messages) {
    const role = typeof m?.role === "string" ? m.role : "user";
    const content = typeof m?.content === "string" ? m.content : "";
    if (content) parts.push(`[${role}] ${content}`);
  }
  return parts.join("\n\n");
}

async function main() {
  // Lazy import so we can produce an actionable error if deps are missing.
  let query;
  try {
    ({ query } = await import("@anthropic-ai/claude-agent-sdk"));
  } catch (err) {
    const msg = err && err.stack ? err.stack : String(err);
    process.stderr.write(
      "[real-runner-error] Missing dependency: @anthropic-ai/claude-agent-sdk\n" +
        "Fix: cd examples/ClaudeAgentSdkProviderDemo/runner && npm install\n" +
        `Details: ${msg}\n`
    );
    process.exitCode = 1;
    return;
  }

  const raw = await readStdinAll();
  const req = safeJsonParse(raw, {});

  const stream = !!req.stream;
  const projectRoot = typeof req.projectRoot === "string" && req.projectRoot ? req.projectRoot : "";
  const settingSources = normalizeStringList(req.settingSources);
  const pluginsCfg = normalizeStringList(req.plugins);
  const permissionMode = typeof req.permissionMode === "string" ? req.permissionMode : undefined;

  const cwd = projectRoot || process.cwd();

  const plugins = [];
  for (const p of pluginsCfg) {
    const abs = path.isAbsolute(p) ? p : path.resolve(cwd, p);
    if (!fs.existsSync(abs)) {
      throw new Error(`Plugin path not found: ${abs}`);
    }
    plugins.push({ type: "local", path: abs });
  }

  const allowedTools = mapAllowedTools(req.allowedTools);

  // systemPrompt:
  // - If loading project settings, use claude_code preset and append Aevatar's extra system instructions.
  // - Otherwise, pass through as a raw string.
  const extraSystem = typeof req.systemPrompt === "string" ? req.systemPrompt : "";
  const wantsProject = settingSources.some((s) => String(s).toLowerCase() === "project");

  const options = {
    cwd,
    settingSources,
    plugins,
    allowedTools,
    includePartialMessages: stream,
  };

  if (typeof req.model === "string" && req.model) {
    options.model = req.model;
  }

  if (permissionMode) {
    // Safety: do not enable bypass unless user explicitly opts-in.
    if (permissionMode === "bypassPermissions") {
      const ok = process.env.AEVATAR_CLAUDE_AGENT_SDK_ALLOW_BYPASS === "1";
      if (!ok) {
        throw new Error(
          "permissionMode=bypassPermissions requires explicit opt-in. " +
            "Set AEVATAR_CLAUDE_AGENT_SDK_ALLOW_BYPASS=1 to allow (dangerous)."
        );
      }
      options.allowDangerouslySkipPermissions = true;
    }
    options.permissionMode = permissionMode;
  }

  if (wantsProject) {
    options.systemPrompt = {
      type: "preset",
      preset: "claude_code",
      append: extraSystem || undefined,
    };
  } else if (extraSystem) {
    options.systemPrompt = extraSystem;
  }

  const prompt = buildPrompt(req);
  if (!prompt) {
    throw new Error("Empty prompt: request.messages contained no usable user content.");
  }

  let finalTextFromDeltas = "";
  const assistantMessages = [];
  let sawStreamEvents = false;
  let emittedLen = 0;

  for await (const msg of query({ prompt, options })) {
    if (stream && msg?.type === "stream_event") {
      const delta = extractTextDeltaFromStreamEvent(msg);
      if (delta) {
        sawStreamEvents = true;
        finalTextFromDeltas += delta;
        emitStream(delta);
      }
      continue;
    }

    if (msg?.type === "assistant") {
      const text = extractAssistantText(msg);
      if (!text) continue;

      assistantMessages.push(text);

      // If we didn't get partial deltas, stream best-effort by diffing the concatenated assistant output.
      if (stream && !sawStreamEvents) {
        const combined = assistantMessages.join("\n");
        const delta = combined.slice(emittedLen);
        if (delta) emitStream(delta);
        emittedLen = combined.length;
      }
    }
  }

  const combinedAssistant = assistantMessages.join("\n");
  const finalText =
    combinedAssistant.length >= finalTextFromDeltas.length ? combinedAssistant : finalTextFromDeltas;

  emitFinal(finalText, {
    runner: "real_claude_agent_sdk_runner.mjs",
    node: process.version,
    mode: "real",
    cwd,
    settingSources,
    allowedTools,
  });
}

main().catch((err) => {
  const msg = err && err.stack ? err.stack : String(err);
  process.stderr.write(`[real-runner-error] ${msg}\n`);
  process.exitCode = 1;
});


