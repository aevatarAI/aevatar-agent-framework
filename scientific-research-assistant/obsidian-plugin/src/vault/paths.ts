// ============================================================
//  Vault path helpers (Vault/SRA/*)
//
//  Goals:
//  - Keep a deterministic layout under a single vaultRoot folder.
//  - Prevent path traversal / absolute path writes.
//  - Make runId safe on all desktop OSes (e.g., ":" is invalid on Windows).
// ============================================================

export function normalizeVaultRoot(input: string): string {
  const raw = (input ?? "").replace(/\\/g, "/").trim();
  if (!raw) return "SRA";

  if (raw.startsWith("/")) throw new Error("vaultRoot must be a vault-relative path");

  const parts = raw.split("/").filter(Boolean);
  if (parts.length === 0) return "SRA";

  for (const p of parts) {
    if (p === "." || p === "..") throw new Error("vaultRoot contains invalid path segments");
  }

  return parts.join("/");
}

export function safeIdForPath(id: string): string {
  // Allow a conservative set of characters to stay cross-platform.
  // Replace everything else with '-'.
  const s = (id ?? "").trim();
  if (!s) return "unknown";
  return s
    .replace(/[^a-zA-Z0-9._-]+/g, "-")
    .replace(/-+/g, "-")
    .replace(/^-+/, "")
    .replace(/-+$/, "") || "unknown";
}

export function joinVaultPath(...parts: string[]): string {
  const segs: string[] = [];
  for (const part of parts) {
    const s = (part ?? "").replace(/\\/g, "/").trim();
    if (!s) continue;
    if (s.startsWith("/")) throw new Error("absolute path is not allowed");

    for (const seg of s.split("/")) {
      const t = seg.trim();
      if (!t) continue;
      if (t === "." || t === "..") throw new Error("path traversal is not allowed");
      segs.push(t);
    }
  }
  return segs.join("/");
}

export function sessionDir(vaultRoot: string, sessionId: string): string {
  return joinVaultPath(normalizeVaultRoot(vaultRoot), "sessions", safeIdForPath(sessionId));
}

export function runDir(vaultRoot: string, sessionId: string, runId: string): string {
  return joinVaultPath(sessionDir(vaultRoot, sessionId), "runs", safeIdForPath(runId));
}

export function runEventsPath(vaultRoot: string, sessionId: string, runId: string): string {
  return joinVaultPath(runDir(vaultRoot, sessionId, runId), "events.jsonl");
}

export function runMetaPath(vaultRoot: string, sessionId: string, runId: string): string {
  return joinVaultPath(runDir(vaultRoot, sessionId, runId), "run.json");
}

export function deliverablesDir(vaultRoot: string, sessionId: string): string {
  return joinVaultPath(sessionDir(vaultRoot, sessionId), "deliverables");
}

export function deliverablesJsonPath(vaultRoot: string, sessionId: string): string {
  return joinVaultPath(deliverablesDir(vaultRoot, sessionId), "deliverables.json");
}

export function deliverablesBriefPath(vaultRoot: string, sessionId: string): string {
  return joinVaultPath(deliverablesDir(vaultRoot, sessionId), "brief.md");
}

export function deliverablesDeliveryPath(vaultRoot: string, sessionId: string): string {
  return joinVaultPath(deliverablesDir(vaultRoot, sessionId), "delivery.md");
}


