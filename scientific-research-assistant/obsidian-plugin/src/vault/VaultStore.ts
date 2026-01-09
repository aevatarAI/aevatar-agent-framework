import type { App } from "obsidian";

import {
  deliverablesDir,
  deliverablesBriefPath,
  deliverablesDeliveryPath,
  deliverablesJsonPath,
  normalizeVaultRoot,
  runDir,
  runEventsPath,
  runMetaPath,
  sessionDir,
} from "./paths";

// ============================================================
//  VaultStore (Vault/SRA/*)
//
//  Notes:
//  - Best-effort persistence: never break runs if vault writes fail.
//  - Desktop-first: uses Obsidian Vault/DataAdapter APIs.
// ============================================================

export interface RunMeta {
  runId: string;
  sessionId: string;
  createdAt?: string;
  mode?: string;
  requestId?: string;
  lastStatus?: string;
}

export class VaultStore {
  private readonly app: App;
  private vaultRoot: string;

  private flushTimer: any = null;
  private pendingAppends = new Map<string, string[]>();

  constructor(app: App, vaultRoot: string) {
    this.app = app;
    this.vaultRoot = normalizeVaultRoot(vaultRoot);
  }

  setVaultRoot(vaultRoot: string) {
    this.vaultRoot = normalizeVaultRoot(vaultRoot);
  }

  async ensureRoot(): Promise<void> {
    await this.ensureFolder(this.vaultRoot);
    await this.ensureFolder(`${this.vaultRoot}/sessions`);
  }

  async appendRunEvent(sessionId: string, runId: string, evt: any): Promise<void> {
    try {
      await this.ensureRoot();
      await this.ensureFolder(sessionDir(this.vaultRoot, sessionId));
      await this.ensureFolder(runDir(this.vaultRoot, sessionId, runId));

      const path = runEventsPath(this.vaultRoot, sessionId, runId);
      const line = JSON.stringify(evt) + "\n";
      this.enqueueAppend(path, line);
    } catch {
      // best-effort only
    }
  }

  async writeRunMeta(sessionId: string, runId: string, meta: RunMeta): Promise<void> {
    try {
      await this.ensureRoot();
      await this.ensureFolder(sessionDir(this.vaultRoot, sessionId));
      await this.ensureFolder(runDir(this.vaultRoot, sessionId, runId));

      const path = runMetaPath(this.vaultRoot, sessionId, runId);
      await this.safeWriteText(path, JSON.stringify(meta, null, 2) + "\n");
    } catch {
      // best-effort only
    }
  }

  async writeDeliverablesJson(sessionId: string, snapshot: any): Promise<void> {
    try {
      await this.ensureRoot();
      await this.ensureFolder(sessionDir(this.vaultRoot, sessionId));
      await this.ensureFolder(deliverablesDir(this.vaultRoot, sessionId));

      const path = deliverablesJsonPath(this.vaultRoot, sessionId);
      await this.safeWriteText(path, JSON.stringify(snapshot, null, 2) + "\n");
    } catch {
      // best-effort only
    }
  }

  async writeDeliverablesMarkdown(sessionId: string, briefMd: string, deliveryMd: string): Promise<void> {
    try {
      await this.ensureRoot();
      await this.ensureFolder(sessionDir(this.vaultRoot, sessionId));
      await this.ensureFolder(deliverablesDir(this.vaultRoot, sessionId));

      await this.safeWriteText(deliverablesBriefPath(this.vaultRoot, sessionId), briefMd);
      await this.safeWriteText(deliverablesDeliveryPath(this.vaultRoot, sessionId), deliveryMd);
    } catch {
      // best-effort
    }
  }

  // ============================================================
  //  Append buffering (avoid too many small writes)
  // ============================================================

  private enqueueAppend(path: string, text: string) {
    const list = this.pendingAppends.get(path) ?? [];
    list.push(text);
    this.pendingAppends.set(path, list);

    if (!this.flushTimer) {
      this.flushTimer = setTimeout(() => {
        this.flushTimer = null;
        void this.flushAppendsAsync();
      }, 200);
    }
  }

  private async flushAppendsAsync(): Promise<void> {
    const entries = Array.from(this.pendingAppends.entries());
    this.pendingAppends.clear();

    for (const [path, chunks] of entries) {
      const content = chunks.join("");
      try {
        await this.safeAppendText(path, content);
      } catch {
        // best-effort
      }
    }
  }

  // ============================================================
  //  Adapter helpers
  // ============================================================

  private async ensureFolder(path: string): Promise<void> {
    const adapter: any = (this.app as any)?.vault?.adapter;
    if (!adapter) return;

    const exists = await adapter.exists(path).catch(() => false);
    if (exists) return;

    await adapter.mkdir(path).catch(() => undefined);
  }

  private async safeWriteText(path: string, content: string): Promise<void> {
    const adapter: any = (this.app as any)?.vault?.adapter;
    if (!adapter) return;
    await adapter.write(path, content).catch(() => undefined);
  }

  private async safeAppendText(path: string, content: string): Promise<void> {
    const adapter: any = (this.app as any)?.vault?.adapter;
    if (!adapter) return;

    // DataAdapter.append exists on desktop; fallback to read+write when absent.
    if (typeof adapter.append === "function") {
      await adapter.append(path, content).catch(() => undefined);
      return;
    }

    const prev = await adapter.read(path).catch(() => "");
    await adapter.write(path, String(prev ?? "") + content).catch(() => undefined);
  }
}


