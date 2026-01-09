import { App, Notice, PluginSettingTab, Setting, requestUrl } from "obsidian";

// ============================================================
//  Settings (MVP)
//
//  Security stance:
//  - Do NOT store API keys in plugin settings.
//  - Keys should be configured via existing Aevatar/SRA secrets workflows.
// ============================================================

export interface SraPluginSettings {
  baseUrl: string; // e.g. http://localhost:5678
  vaultRoot: string; // default: SRA
  secretsUiUrl: string; // default: http://localhost:6677
  requestTimeoutMs: number; // default: 15000
}

export const DEFAULT_SRA_SETTINGS: SraPluginSettings = {
  baseUrl: "http://localhost:5678",
  vaultRoot: "SRA",
  secretsUiUrl: "http://localhost:6677",
  requestTimeoutMs: 15000,
};

export class SraSettingsTab extends PluginSettingTab {
  private readonly plugin: { settings: SraPluginSettings; saveSettings: () => Promise<void> };

  constructor(app: App, plugin: { settings: SraPluginSettings; saveSettings: () => Promise<void> }) {
    super(app, plugin as any);
    this.plugin = plugin;
  }

  display(): void {
    const { containerEl } = this;
    containerEl.empty();

    containerEl.createEl("h2", { text: "Aevatar SRA" });

    new Setting(containerEl)
      .setName("Backend baseUrl")
      .setDesc("Default: http://localhost:5678 (repo policy: never use :5000 in examples)")
      .addText((t) => {
        t.setPlaceholder(DEFAULT_SRA_SETTINGS.baseUrl)
          .setValue(this.plugin.settings.baseUrl)
          .onChange(async (value) => {
            this.plugin.settings.baseUrl = (value ?? "").trim();
            await this.plugin.saveSettings();
          });
      });

    new Setting(containerEl)
      .setName("Vault root folder")
      .setDesc("All plugin artifacts will be written under this folder in the current vault. Default: SRA")
      .addText((t) => {
        t.setPlaceholder(DEFAULT_SRA_SETTINGS.vaultRoot)
          .setValue(this.plugin.settings.vaultRoot)
          .onChange(async (value) => {
            this.plugin.settings.vaultRoot = (value ?? "").trim() || DEFAULT_SRA_SETTINGS.vaultRoot;
            await this.plugin.saveSettings();
          });
      });

    new Setting(containerEl)
      .setName("Secrets UI URL (optional)")
      .setDesc("Best-effort helper link. Keys should be configured outside the plugin.")
      .addText((t) => {
        t.setPlaceholder(DEFAULT_SRA_SETTINGS.secretsUiUrl)
          .setValue(this.plugin.settings.secretsUiUrl)
          .onChange(async (value) => {
            this.plugin.settings.secretsUiUrl = (value ?? "").trim();
            await this.plugin.saveSettings();
          });
      });

    new Setting(containerEl)
      .setName("Open Secrets UI")
      .setDesc("Opens the configured Secrets UI URL in a browser (best-effort).")
      .addButton((b) => {
        b.setButtonText("Open").onClick(() => {
          const url = (this.plugin.settings.secretsUiUrl ?? "").trim();
          if (!url) {
            new Notice("Secrets UI URL is empty.");
            return;
          }
          try {
            window.open(url, "_blank");
          } catch {
            new Notice("Failed to open Secrets UI URL.");
          }
        });
      });

    new Setting(containerEl)
      .setName("Request timeout (ms)")
      .setDesc("Default: 15000")
      .addText((t) => {
        t.setPlaceholder(String(DEFAULT_SRA_SETTINGS.requestTimeoutMs))
          .setValue(String(this.plugin.settings.requestTimeoutMs ?? DEFAULT_SRA_SETTINGS.requestTimeoutMs))
          .onChange(async (value) => {
            const n = Number.parseInt((value ?? "").trim(), 10);
            this.plugin.settings.requestTimeoutMs = Number.isFinite(n) && n > 0 ? n : DEFAULT_SRA_SETTINGS.requestTimeoutMs;
            await this.plugin.saveSettings();
          });
      });

    new Setting(containerEl)
      .setName("Test Connection")
      .setDesc("Calls /health (then /api/info) to verify the backend is reachable.")
      .addButton((b) => {
        b.setButtonText("Test Connection").onClick(async () => {
          const baseUrl = normalizeBaseUrl(this.plugin.settings.baseUrl);
          const timeoutMs = clampTimeout(this.plugin.settings.requestTimeoutMs);

          if (!baseUrl) {
            new Notice("Invalid baseUrl.");
            return;
          }

          const res = await testConnectionAsync(baseUrl, timeoutMs);
          if (res.ok) {
            new Notice(`SRA backend OK (${res.endpoint}, HTTP ${res.status})`);
          } else {
            new Notice(`SRA backend NOT reachable (${res.endpoint}): ${res.error}`);
          }
        });
      });
  }
}

function normalizeBaseUrl(input: string): string {
  const s = (input ?? "").trim();
  if (!s) return "";
  return s.replace(/\/+$/, "");
}

function clampTimeout(ms: number): number {
  const n = Number.isFinite(ms) ? ms : DEFAULT_SRA_SETTINGS.requestTimeoutMs;
  return Math.max(1000, Math.min(120_000, n));
}

async function testConnectionAsync(
  baseUrl: string,
  timeoutMs: number,
): Promise<{ ok: boolean; endpoint: string; status: number; error: string }> {
  const healthUrl = `${baseUrl}/health`;
  try {
    const r = await requestUrl({
      url: healthUrl,
      method: "GET",
      timeout: timeoutMs as any,
      throw: false,
    } as any);

    if (r.status >= 200 && r.status < 300) {
      return { ok: true, endpoint: "/health", status: r.status, error: "" };
    }
  } catch (e: any) {
    // fallthrough to /api/info
  }

  const infoUrl = `${baseUrl}/api/info`;
  try {
    const r = await requestUrl({
      url: infoUrl,
      method: "GET",
      timeout: timeoutMs as any,
      throw: false,
    } as any);

    if (r.status >= 200 && r.status < 300) {
      return { ok: true, endpoint: "/api/info", status: r.status, error: "" };
    }

    return { ok: false, endpoint: "/api/info", status: r.status, error: `HTTP ${r.status}` };
  } catch (e: any) {
    const msg = (e?.message ?? String(e ?? "unknown error")).trim();
    return { ok: false, endpoint: "/api/info", status: 0, error: msg || "request failed" };
  }
}


