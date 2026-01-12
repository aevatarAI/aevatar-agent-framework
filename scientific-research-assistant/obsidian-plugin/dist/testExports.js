"use strict";
var __defProp = Object.defineProperty;
var __getOwnPropDesc = Object.getOwnPropertyDescriptor;
var __getOwnPropNames = Object.getOwnPropertyNames;
var __hasOwnProp = Object.prototype.hasOwnProperty;
var __export = (target, all) => {
  for (var name in all)
    __defProp(target, name, { get: all[name], enumerable: true });
};
var __copyProps = (to, from, except, desc) => {
  if (from && typeof from === "object" || typeof from === "function") {
    for (let key of __getOwnPropNames(from))
      if (!__hasOwnProp.call(to, key) && key !== except)
        __defProp(to, key, { get: () => from[key], enumerable: !(desc = __getOwnPropDesc(from, key)) || desc.enumerable });
  }
  return to;
};
var __toCommonJS = (mod) => __copyProps(__defProp({}, "__esModule", { value: true }), mod);

// src/testExports.ts
var testExports_exports = {};
__export(testExports_exports, {
  SseParser: () => SseParser,
  joinVaultPath: () => joinVaultPath,
  normalizeVaultRoot: () => normalizeVaultRoot,
  runDir: () => runDir,
  runEventsPath: () => runEventsPath,
  safeIdForPath: () => safeIdForPath,
  sessionDir: () => sessionDir
});
module.exports = __toCommonJS(testExports_exports);

// src/sse/sseParser.ts
var SseParser = class {
  constructor() {
    this.buffer = "";
  }
  push(chunk) {
    if (!chunk)
      return [];
    this.buffer += chunk;
    this.buffer = this.buffer.replace(/\r\n/g, "\n");
    const out = [];
    while (true) {
      const idx = this.buffer.indexOf("\n\n");
      if (idx < 0)
        break;
      const frame = this.buffer.slice(0, idx);
      this.buffer = this.buffer.slice(idx + 2);
      const data = extractData(frame);
      if (data.length > 0)
        out.push(data);
    }
    return out;
  }
  flush() {
    if (!this.buffer)
      return [];
    const rest = this.buffer;
    this.buffer = "";
    const data = extractData(rest.replace(/\r\n/g, "\n"));
    return data.length > 0 ? [data] : [];
  }
};
function extractData(frame) {
  const lines = (frame != null ? frame : "").split("\n");
  const parts = [];
  for (const line of lines) {
    if (!line)
      continue;
    if (line.startsWith("data:")) {
      const raw = line.slice("data:".length);
      parts.push(raw.startsWith(" ") ? raw.slice(1) : raw);
    }
  }
  return parts.join("\n").trim();
}

// src/vault/paths.ts
function normalizeVaultRoot(input) {
  const raw = (input != null ? input : "").replace(/\\/g, "/").trim();
  if (!raw)
    return "SRA";
  if (raw.startsWith("/"))
    throw new Error("vaultRoot must be a vault-relative path");
  const parts = raw.split("/").filter(Boolean);
  if (parts.length === 0)
    return "SRA";
  for (const p of parts) {
    if (p === "." || p === "..")
      throw new Error("vaultRoot contains invalid path segments");
  }
  return parts.join("/");
}
function safeIdForPath(id) {
  const s = (id != null ? id : "").trim();
  if (!s)
    return "unknown";
  return s.replace(/[^a-zA-Z0-9._-]+/g, "-").replace(/-+/g, "-").replace(/^-+/, "").replace(/-+$/, "") || "unknown";
}
function joinVaultPath(...parts) {
  const segs = [];
  for (const part of parts) {
    const s = (part != null ? part : "").replace(/\\/g, "/").trim();
    if (!s)
      continue;
    if (s.startsWith("/"))
      throw new Error("absolute path is not allowed");
    for (const seg of s.split("/")) {
      const t = seg.trim();
      if (!t)
        continue;
      if (t === "." || t === "..")
        throw new Error("path traversal is not allowed");
      segs.push(t);
    }
  }
  return segs.join("/");
}
function sessionDir(vaultRoot, sessionId) {
  return joinVaultPath(normalizeVaultRoot(vaultRoot), "sessions", safeIdForPath(sessionId));
}
function runDir(vaultRoot, sessionId, runId) {
  return joinVaultPath(sessionDir(vaultRoot, sessionId), "runs", safeIdForPath(runId));
}
function runEventsPath(vaultRoot, sessionId, runId) {
  return joinVaultPath(runDir(vaultRoot, sessionId, runId), "events.jsonl");
}
// Annotate the CommonJS export names for ESM import in node:
0 && (module.exports = {
  SseParser,
  joinVaultPath,
  normalizeVaultRoot,
  runDir,
  runEventsPath,
  safeIdForPath,
  sessionDir
});
//# sourceMappingURL=data:application/json;base64,ewogICJ2ZXJzaW9uIjogMywKICAic291cmNlcyI6IFsiLi4vc3JjL3Rlc3RFeHBvcnRzLnRzIiwgIi4uL3NyYy9zc2Uvc3NlUGFyc2VyLnRzIiwgIi4uL3NyYy92YXVsdC9wYXRocy50cyJdLAogICJzb3VyY2VzQ29udGVudCI6IFsiLy8gPT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09XG4vLyAgVGVzdCBleHBvcnRzIChOb2RlLW9ubHkpXG4vL1xuLy8gIFdoeTpcbi8vICAtIE5vZGUgdGVzdCBydW5uZXIgY2Fubm90IHJlcXVpcmUgT2JzaWRpYW4gcnVudGltZSBtb2R1bGVzLlxuLy8gIC0gRXhwb3J0IG9ubHkgcHVyZSBoZWxwZXJzIHRoYXQgZG8gTk9UIGltcG9ydCBcIm9ic2lkaWFuXCIuXG4vL1xuLy8gIFRoaXMgZmlsZSBpcyBidW5kbGVkIHRvIGRpc3QvdGVzdEV4cG9ydHMuanMgYnkgZXNidWlsZC5tanMuXG4vLyA9PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT1cblxuZXhwb3J0IHsgU3NlUGFyc2VyIH0gZnJvbSBcIi4vc3NlL3NzZVBhcnNlclwiO1xuZXhwb3J0IHtcbiAgbm9ybWFsaXplVmF1bHRSb290LFxuICBzYWZlSWRGb3JQYXRoLFxuICBqb2luVmF1bHRQYXRoLFxuICBzZXNzaW9uRGlyLFxuICBydW5EaXIsXG4gIHJ1bkV2ZW50c1BhdGgsXG59IGZyb20gXCIuL3ZhdWx0L3BhdGhzXCI7XG5cblxuIiwgIi8vID09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PVxuLy8gIFNTRSBwYXJzZXIgKE5vZGUtZnJpZW5kbHkpXG4vL1xuLy8gIFBhcnNlcyBTZXJ2ZXItU2VudCBFdmVudHMgc3RyZWFtcyBpbnRvIGBkYXRhOmAgcGF5bG9hZCBibG9ja3MuXG4vLyAgLSBTdXBwb3J0cyBgXFxuXFxuYCBhbmQgYFxcclxcblxcclxcbmAgZGVsaW1pdGVyc1xuLy8gIC0gU3VwcG9ydHMgbXVsdGktbGluZSBgZGF0YTpgIChqb2luZWQgd2l0aCAnXFxuJylcbi8vID09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PVxuXG5leHBvcnQgY2xhc3MgU3NlUGFyc2VyIHtcbiAgcHJpdmF0ZSBidWZmZXIgPSBcIlwiO1xuXG4gIHB1c2goY2h1bms6IHN0cmluZyk6IHN0cmluZ1tdIHtcbiAgICBpZiAoIWNodW5rKSByZXR1cm4gW107XG5cbiAgICB0aGlzLmJ1ZmZlciArPSBjaHVuaztcbiAgICAvLyBOb3JtYWxpemUgQ1JMRiB0byBMRiBmb3IgZWFzaWVyIHBhcnNpbmcuXG4gICAgdGhpcy5idWZmZXIgPSB0aGlzLmJ1ZmZlci5yZXBsYWNlKC9cXHJcXG4vZywgXCJcXG5cIik7XG5cbiAgICBjb25zdCBvdXQ6IHN0cmluZ1tdID0gW107XG4gICAgd2hpbGUgKHRydWUpIHtcbiAgICAgIGNvbnN0IGlkeCA9IHRoaXMuYnVmZmVyLmluZGV4T2YoXCJcXG5cXG5cIik7XG4gICAgICBpZiAoaWR4IDwgMCkgYnJlYWs7XG5cbiAgICAgIGNvbnN0IGZyYW1lID0gdGhpcy5idWZmZXIuc2xpY2UoMCwgaWR4KTtcbiAgICAgIHRoaXMuYnVmZmVyID0gdGhpcy5idWZmZXIuc2xpY2UoaWR4ICsgMik7XG5cbiAgICAgIGNvbnN0IGRhdGEgPSBleHRyYWN0RGF0YShmcmFtZSk7XG4gICAgICBpZiAoZGF0YS5sZW5ndGggPiAwKSBvdXQucHVzaChkYXRhKTtcbiAgICB9XG5cbiAgICByZXR1cm4gb3V0O1xuICB9XG5cbiAgZmx1c2goKTogc3RyaW5nW10ge1xuICAgIC8vIElmIHRoZSBzdHJlYW0gZW5kcyB3aXRob3V0IGRlbGltaXRlciwgYmVzdC1lZmZvcnQgcGFyc2UgcmVtYWluZGVyLlxuICAgIGlmICghdGhpcy5idWZmZXIpIHJldHVybiBbXTtcbiAgICBjb25zdCByZXN0ID0gdGhpcy5idWZmZXI7XG4gICAgdGhpcy5idWZmZXIgPSBcIlwiO1xuICAgIGNvbnN0IGRhdGEgPSBleHRyYWN0RGF0YShyZXN0LnJlcGxhY2UoL1xcclxcbi9nLCBcIlxcblwiKSk7XG4gICAgcmV0dXJuIGRhdGEubGVuZ3RoID4gMCA/IFtkYXRhXSA6IFtdO1xuICB9XG59XG5cbmZ1bmN0aW9uIGV4dHJhY3REYXRhKGZyYW1lOiBzdHJpbmcpOiBzdHJpbmcge1xuICBjb25zdCBsaW5lcyA9IChmcmFtZSA/PyBcIlwiKS5zcGxpdChcIlxcblwiKTtcbiAgY29uc3QgcGFydHM6IHN0cmluZ1tdID0gW107XG4gIGZvciAoY29uc3QgbGluZSBvZiBsaW5lcykge1xuICAgIGlmICghbGluZSkgY29udGludWU7XG4gICAgaWYgKGxpbmUuc3RhcnRzV2l0aChcImRhdGE6XCIpKSB7XG4gICAgICAvLyBLZWVwIGxlYWRpbmcgc3BhY2VzIGluIHBheWxvYWQgKFNTRSBzcGVjIGFsbG93cyB0aGVtKSwgYnV0IHRyaW0gb25lIG9wdGlvbmFsIHNwYWNlLlxuICAgICAgY29uc3QgcmF3ID0gbGluZS5zbGljZShcImRhdGE6XCIubGVuZ3RoKTtcbiAgICAgIHBhcnRzLnB1c2gocmF3LnN0YXJ0c1dpdGgoXCIgXCIpID8gcmF3LnNsaWNlKDEpIDogcmF3KTtcbiAgICB9XG4gIH1cbiAgcmV0dXJuIHBhcnRzLmpvaW4oXCJcXG5cIikudHJpbSgpO1xufVxuXG5cbiIsICIvLyA9PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT1cbi8vICBWYXVsdCBwYXRoIGhlbHBlcnMgKFZhdWx0L1NSQS8qKVxuLy9cbi8vICBHb2Fsczpcbi8vICAtIEtlZXAgYSBkZXRlcm1pbmlzdGljIGxheW91dCB1bmRlciBhIHNpbmdsZSB2YXVsdFJvb3QgZm9sZGVyLlxuLy8gIC0gUHJldmVudCBwYXRoIHRyYXZlcnNhbCAvIGFic29sdXRlIHBhdGggd3JpdGVzLlxuLy8gIC0gTWFrZSBydW5JZCBzYWZlIG9uIGFsbCBkZXNrdG9wIE9TZXMgKGUuZy4sIFwiOlwiIGlzIGludmFsaWQgb24gV2luZG93cykuXG4vLyA9PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT1cblxuZXhwb3J0IGZ1bmN0aW9uIG5vcm1hbGl6ZVZhdWx0Um9vdChpbnB1dDogc3RyaW5nKTogc3RyaW5nIHtcbiAgY29uc3QgcmF3ID0gKGlucHV0ID8/IFwiXCIpLnJlcGxhY2UoL1xcXFwvZywgXCIvXCIpLnRyaW0oKTtcbiAgaWYgKCFyYXcpIHJldHVybiBcIlNSQVwiO1xuXG4gIGlmIChyYXcuc3RhcnRzV2l0aChcIi9cIikpIHRocm93IG5ldyBFcnJvcihcInZhdWx0Um9vdCBtdXN0IGJlIGEgdmF1bHQtcmVsYXRpdmUgcGF0aFwiKTtcblxuICBjb25zdCBwYXJ0cyA9IHJhdy5zcGxpdChcIi9cIikuZmlsdGVyKEJvb2xlYW4pO1xuICBpZiAocGFydHMubGVuZ3RoID09PSAwKSByZXR1cm4gXCJTUkFcIjtcblxuICBmb3IgKGNvbnN0IHAgb2YgcGFydHMpIHtcbiAgICBpZiAocCA9PT0gXCIuXCIgfHwgcCA9PT0gXCIuLlwiKSB0aHJvdyBuZXcgRXJyb3IoXCJ2YXVsdFJvb3QgY29udGFpbnMgaW52YWxpZCBwYXRoIHNlZ21lbnRzXCIpO1xuICB9XG5cbiAgcmV0dXJuIHBhcnRzLmpvaW4oXCIvXCIpO1xufVxuXG5leHBvcnQgZnVuY3Rpb24gc2FmZUlkRm9yUGF0aChpZDogc3RyaW5nKTogc3RyaW5nIHtcbiAgLy8gQWxsb3cgYSBjb25zZXJ2YXRpdmUgc2V0IG9mIGNoYXJhY3RlcnMgdG8gc3RheSBjcm9zcy1wbGF0Zm9ybS5cbiAgLy8gUmVwbGFjZSBldmVyeXRoaW5nIGVsc2Ugd2l0aCAnLScuXG4gIGNvbnN0IHMgPSAoaWQgPz8gXCJcIikudHJpbSgpO1xuICBpZiAoIXMpIHJldHVybiBcInVua25vd25cIjtcbiAgcmV0dXJuIHNcbiAgICAucmVwbGFjZSgvW15hLXpBLVowLTkuXy1dKy9nLCBcIi1cIilcbiAgICAucmVwbGFjZSgvLSsvZywgXCItXCIpXG4gICAgLnJlcGxhY2UoL14tKy8sIFwiXCIpXG4gICAgLnJlcGxhY2UoLy0rJC8sIFwiXCIpIHx8IFwidW5rbm93blwiO1xufVxuXG5leHBvcnQgZnVuY3Rpb24gam9pblZhdWx0UGF0aCguLi5wYXJ0czogc3RyaW5nW10pOiBzdHJpbmcge1xuICBjb25zdCBzZWdzOiBzdHJpbmdbXSA9IFtdO1xuICBmb3IgKGNvbnN0IHBhcnQgb2YgcGFydHMpIHtcbiAgICBjb25zdCBzID0gKHBhcnQgPz8gXCJcIikucmVwbGFjZSgvXFxcXC9nLCBcIi9cIikudHJpbSgpO1xuICAgIGlmICghcykgY29udGludWU7XG4gICAgaWYgKHMuc3RhcnRzV2l0aChcIi9cIikpIHRocm93IG5ldyBFcnJvcihcImFic29sdXRlIHBhdGggaXMgbm90IGFsbG93ZWRcIik7XG5cbiAgICBmb3IgKGNvbnN0IHNlZyBvZiBzLnNwbGl0KFwiL1wiKSkge1xuICAgICAgY29uc3QgdCA9IHNlZy50cmltKCk7XG4gICAgICBpZiAoIXQpIGNvbnRpbnVlO1xuICAgICAgaWYgKHQgPT09IFwiLlwiIHx8IHQgPT09IFwiLi5cIikgdGhyb3cgbmV3IEVycm9yKFwicGF0aCB0cmF2ZXJzYWwgaXMgbm90IGFsbG93ZWRcIik7XG4gICAgICBzZWdzLnB1c2godCk7XG4gICAgfVxuICB9XG4gIHJldHVybiBzZWdzLmpvaW4oXCIvXCIpO1xufVxuXG5leHBvcnQgZnVuY3Rpb24gc2Vzc2lvbkRpcih2YXVsdFJvb3Q6IHN0cmluZywgc2Vzc2lvbklkOiBzdHJpbmcpOiBzdHJpbmcge1xuICByZXR1cm4gam9pblZhdWx0UGF0aChub3JtYWxpemVWYXVsdFJvb3QodmF1bHRSb290KSwgXCJzZXNzaW9uc1wiLCBzYWZlSWRGb3JQYXRoKHNlc3Npb25JZCkpO1xufVxuXG5leHBvcnQgZnVuY3Rpb24gcnVuRGlyKHZhdWx0Um9vdDogc3RyaW5nLCBzZXNzaW9uSWQ6IHN0cmluZywgcnVuSWQ6IHN0cmluZyk6IHN0cmluZyB7XG4gIHJldHVybiBqb2luVmF1bHRQYXRoKHNlc3Npb25EaXIodmF1bHRSb290LCBzZXNzaW9uSWQpLCBcInJ1bnNcIiwgc2FmZUlkRm9yUGF0aChydW5JZCkpO1xufVxuXG5leHBvcnQgZnVuY3Rpb24gcnVuRXZlbnRzUGF0aCh2YXVsdFJvb3Q6IHN0cmluZywgc2Vzc2lvbklkOiBzdHJpbmcsIHJ1bklkOiBzdHJpbmcpOiBzdHJpbmcge1xuICByZXR1cm4gam9pblZhdWx0UGF0aChydW5EaXIodmF1bHRSb290LCBzZXNzaW9uSWQsIHJ1bklkKSwgXCJldmVudHMuanNvbmxcIik7XG59XG5cbmV4cG9ydCBmdW5jdGlvbiBydW5NZXRhUGF0aCh2YXVsdFJvb3Q6IHN0cmluZywgc2Vzc2lvbklkOiBzdHJpbmcsIHJ1bklkOiBzdHJpbmcpOiBzdHJpbmcge1xuICByZXR1cm4gam9pblZhdWx0UGF0aChydW5EaXIodmF1bHRSb290LCBzZXNzaW9uSWQsIHJ1bklkKSwgXCJydW4uanNvblwiKTtcbn1cblxuZXhwb3J0IGZ1bmN0aW9uIGRlbGl2ZXJhYmxlc0Rpcih2YXVsdFJvb3Q6IHN0cmluZywgc2Vzc2lvbklkOiBzdHJpbmcpOiBzdHJpbmcge1xuICByZXR1cm4gam9pblZhdWx0UGF0aChzZXNzaW9uRGlyKHZhdWx0Um9vdCwgc2Vzc2lvbklkKSwgXCJkZWxpdmVyYWJsZXNcIik7XG59XG5cbmV4cG9ydCBmdW5jdGlvbiBkZWxpdmVyYWJsZXNKc29uUGF0aCh2YXVsdFJvb3Q6IHN0cmluZywgc2Vzc2lvbklkOiBzdHJpbmcpOiBzdHJpbmcge1xuICByZXR1cm4gam9pblZhdWx0UGF0aChkZWxpdmVyYWJsZXNEaXIodmF1bHRSb290LCBzZXNzaW9uSWQpLCBcImRlbGl2ZXJhYmxlcy5qc29uXCIpO1xufVxuXG5leHBvcnQgZnVuY3Rpb24gZGVsaXZlcmFibGVzQnJpZWZQYXRoKHZhdWx0Um9vdDogc3RyaW5nLCBzZXNzaW9uSWQ6IHN0cmluZyk6IHN0cmluZyB7XG4gIHJldHVybiBqb2luVmF1bHRQYXRoKGRlbGl2ZXJhYmxlc0Rpcih2YXVsdFJvb3QsIHNlc3Npb25JZCksIFwiYnJpZWYubWRcIik7XG59XG5cbmV4cG9ydCBmdW5jdGlvbiBkZWxpdmVyYWJsZXNEZWxpdmVyeVBhdGgodmF1bHRSb290OiBzdHJpbmcsIHNlc3Npb25JZDogc3RyaW5nKTogc3RyaW5nIHtcbiAgcmV0dXJuIGpvaW5WYXVsdFBhdGgoZGVsaXZlcmFibGVzRGlyKHZhdWx0Um9vdCwgc2Vzc2lvbklkKSwgXCJkZWxpdmVyeS5tZFwiKTtcbn1cblxuXG4iXSwKICAibWFwcGluZ3MiOiAiOzs7Ozs7Ozs7Ozs7Ozs7Ozs7OztBQUFBO0FBQUE7QUFBQTtBQUFBO0FBQUE7QUFBQTtBQUFBO0FBQUE7QUFBQTtBQUFBO0FBQUE7OztBQ1FPLElBQU0sWUFBTixNQUFnQjtBQUFBLEVBQWhCO0FBQ0wsU0FBUSxTQUFTO0FBQUE7QUFBQSxFQUVqQixLQUFLLE9BQXlCO0FBQzVCLFFBQUksQ0FBQztBQUFPLGFBQU8sQ0FBQztBQUVwQixTQUFLLFVBQVU7QUFFZixTQUFLLFNBQVMsS0FBSyxPQUFPLFFBQVEsU0FBUyxJQUFJO0FBRS9DLFVBQU0sTUFBZ0IsQ0FBQztBQUN2QixXQUFPLE1BQU07QUFDWCxZQUFNLE1BQU0sS0FBSyxPQUFPLFFBQVEsTUFBTTtBQUN0QyxVQUFJLE1BQU07QUFBRztBQUViLFlBQU0sUUFBUSxLQUFLLE9BQU8sTUFBTSxHQUFHLEdBQUc7QUFDdEMsV0FBSyxTQUFTLEtBQUssT0FBTyxNQUFNLE1BQU0sQ0FBQztBQUV2QyxZQUFNLE9BQU8sWUFBWSxLQUFLO0FBQzlCLFVBQUksS0FBSyxTQUFTO0FBQUcsWUFBSSxLQUFLLElBQUk7QUFBQSxJQUNwQztBQUVBLFdBQU87QUFBQSxFQUNUO0FBQUEsRUFFQSxRQUFrQjtBQUVoQixRQUFJLENBQUMsS0FBSztBQUFRLGFBQU8sQ0FBQztBQUMxQixVQUFNLE9BQU8sS0FBSztBQUNsQixTQUFLLFNBQVM7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
