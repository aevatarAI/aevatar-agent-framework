import { ItemView, Notice, WorkspaceLeaf } from "obsidian";

import { SraApiClient } from "../api/SraApiClient";
import type { SraSendInputRequest } from "../api/types";
import { SraSseClient, type SseStatus } from "../sse/SraSseClient";
import { VaultStore } from "../vault/VaultStore";
import { renderBriefMarkdown, renderDeliveryMarkdown } from "../vault/render";

export const VIEW_TYPE_SRA = "aevatar-sra-view";

type PluginLike = {
  app: any;
  settings: {
    baseUrl: string;
    vaultRoot: string;
    requestTimeoutMs: number;
  };
};

export class SraView extends ItemView {
  private readonly plugin: PluginLike;

  private readonly api: SraApiClient;
  private readonly sse: SraSseClient<any>;
  private readonly vault: VaultStore;

  private sessionId = "";
  private runId = "";

  private status: SseStatus = "Disconnected";
  private lastError = "";

  private elStatus!: HTMLElement;
  private elSession!: HTMLInputElement;
  private elRun!: HTMLElement;
  private elMode!: HTMLSelectElement;
  private elMessage!: HTMLTextAreaElement;
  private elError!: HTMLElement;
  private elAttachments!: HTMLElement;

  private attachments: any[] = [];

  constructor(leaf: WorkspaceLeaf, plugin: PluginLike) {
    super(leaf);
    this.plugin = plugin;

    this.api = new SraApiClient({ baseUrl: plugin.settings.baseUrl, timeoutMs: plugin.settings.requestTimeoutMs });
    this.sse = new SraSseClient({ baseUrl: plugin.settings.baseUrl });
    this.vault = new VaultStore(plugin.app, plugin.settings.vaultRoot);
  }

  getViewType(): string {
    return VIEW_TYPE_SRA;
  }

  getDisplayText(): string {
    return "SRA";
  }

  async onOpen(): Promise<void> {
    this.render();
    await this.vault.ensureRoot();
  }

  async onClose(): Promise<void> {
    this.sse.disconnect();
  }

  // ------------------------------------------------------------
  //  Public actions (used by commands)
  // ------------------------------------------------------------

  async createNewSession(): Promise<void> {
    this.refreshRuntime();
    try {
      const res = await this.api.createSession();
      this.connectToSession(res.sessionId);
      new Notice(`SRA session created: ${res.sessionId}`);
    } catch (e: any) {
      this.setError(e?.message ?? "create session failed");
      new Notice(`Create session failed: ${this.lastError}`);
    }
  }

  connectToSession(sessionId: string): void {
    const sid = String(sessionId ?? "").trim();
    if (!sid) {
      this.setError("sessionId is required");
      return;
    }

    this.sessionId = sid;
    this.runId = "";
    this.elSession.value = sid;
    this.elRun.setText("run: (none)");
    this.setError("");

    this.refreshRuntime();
    this.sse.connect(sid, {
      onStatus: (status, detail) => {
        this.status = status;
        const tail = detail ? ` — ${detail}` : "";
        this.elStatus.setText(`SSE: ${status}${tail}`);
      },
      onEvent: (evt) => {
        // Minimal UX: update runId when present, and persist raw events to vault.
        const rid = extractRunId(evt);
        if (rid && rid !== this.runId) {
          this.runId = rid;
          this.elRun.setText(`run: ${rid}`);
        }

        void this.vault.appendRunEvent(this.sessionId, this.runId || "stream", evt);
      },
    });
  }

  async sendCurrentMessage(): Promise<void> {
    const msg = (this.elMessage.value ?? "").trim();
    if (!msg) return;
    if (!this.sessionId) {
      new Notice("No active session. Create or connect first.");
      return;
    }

    this.refreshRuntime();
    try {
      const mode = this.elMode.value;
      let attachmentPaths: string[] | undefined = undefined;

      if (this.attachments.length > 0) {
        const files = await this.readAttachmentsAsync();
        const up = await this.api.uploadAttachments(this.sessionId, files);
        attachmentPaths = up.attachmentPaths;
      }

      const input: SraSendInputRequest = { message: msg, mode, attachmentPaths };
      const res = await this.api.sendInput(this.sessionId, input);
      this.runId = res.runId;
      this.elRun.setText(`run: ${res.runId}`);
      this.elMessage.value = "";
      this.attachments = [];
      this.renderAttachments();

      await this.vault.writeRunMeta(this.sessionId, res.runId, {
        sessionId: this.sessionId,
        runId: res.runId,
        createdAt: new Date().toISOString(),
        mode,
      });
    } catch (e: any) {
      this.setError(e?.message ?? "send failed");
      new Notice(`Send failed: ${this.lastError}`);
    }
  }

  // ------------------------------------------------------------
  //  Rendering
  // ------------------------------------------------------------

  private render(): void {
    const { containerEl } = this;
    containerEl.empty();
    containerEl.addClass("aevatar-sra-view");

    containerEl.createEl("h2", { text: "Aevatar SRA" });

    this.elStatus = containerEl.createEl("div", { text: "SSE: Disconnected" });
    this.elRun = containerEl.createEl("div", { text: "run: (none)" });

    const row = containerEl.createEl("div");
    row.style.display = "flex";
    row.style.gap = "8px";
    row.style.alignItems = "center";

    this.elSession = row.createEl("input");
    this.elSession.type = "text";
    this.elSession.placeholder = "sessionId";
    this.elSession.style.flex = "1";

    const btnConnect = row.createEl("button", { text: "Connect" });
    btnConnect.onclick = () => this.connectToSession(this.elSession.value);

    const btnNew = row.createEl("button", { text: "New Session" });
    btnNew.onclick = () => void this.createNewSession();

    const controls = containerEl.createEl("div");
    controls.style.display = "flex";
    controls.style.gap = "8px";
    controls.style.marginTop = "8px";

    this.elMode = controls.createEl("select");
    for (const m of ["chat", "vibe", "vibe_loop"]) {
      const opt = this.elMode.createEl("option");
      opt.value = m;
      opt.text = m;
    }

    const btnSend = controls.createEl("button", { text: "Send" });
    btnSend.onclick = () => void this.sendCurrentMessage();

    const btnPull = controls.createEl("button", { text: "Pull Deliverables" });
    btnPull.onclick = () => void this.pullDeliverablesAsync();

    const attachRow = containerEl.createEl("div");
    attachRow.style.display = "flex";
    attachRow.style.gap = "8px";
    attachRow.style.marginTop = "8px";
    attachRow.style.alignItems = "center";

    const btnAttach = attachRow.createEl("button", { text: "Attach current note" });
    btnAttach.onclick = () => void this.attachCurrentNoteAsync();

    const btnClear = attachRow.createEl("button", { text: "Clear attachments" });
    btnClear.onclick = () => {
      this.attachments = [];
      this.renderAttachments();
    };

    this.elAttachments = attachRow.createEl("div", { text: "" });
    this.elAttachments.style.opacity = "0.8";

    this.elMessage = containerEl.createEl("textarea");
    this.elMessage.placeholder = "Ask a research question…";
    this.elMessage.rows = 4;
    this.elMessage.style.width = "100%";
    this.elMessage.style.marginTop = "8px";

    this.elError = containerEl.createEl("div", { text: "" });
    this.elError.style.marginTop = "8px";
    this.elError.style.color = "var(--text-error)";

    this.renderAttachments();
  }

  private refreshRuntime(): void {
    // Keep runtime aligned with latest settings (simple MVP approach).
    this.api.setBaseUrl(this.plugin.settings.baseUrl);
    this.api.setTimeoutMs(this.plugin.settings.requestTimeoutMs);
    this.sse.setBaseUrl(this.plugin.settings.baseUrl);
    this.vault.setVaultRoot(this.plugin.settings.vaultRoot);
  }

  private setError(msg: string): void {
    this.lastError = (msg ?? "").trim();
    this.elError.setText(this.lastError);
  }

  private renderAttachments(): void {
    const n = this.attachments.length;
    this.elAttachments.setText(n === 0 ? "Attachments: (none)" : `Attachments: ${n}`);
  }

  private async attachCurrentNoteAsync(): Promise<void> {
    const f = this.app.workspace.getActiveFile();
    if (!f) {
      new Notice("No active file.");
      return;
    }

    if (!this.attachments.some((x) => x?.path === f.path)) {
      this.attachments.push(f);
      this.renderAttachments();
      new Notice(`Attached: ${f.name}`);
    }
  }

  private async readAttachmentsAsync(): Promise<Array<{ filename: string; contentType: string; data: Uint8Array }>> {
    const out: Array<{ filename: string; contentType: string; data: Uint8Array }> = [];
    for (const f of this.attachments) {
      const name = String(f?.name ?? "file").trim() || "file";
      const ext = name.toLowerCase().split(".").pop() ?? "";
      const contentType = guessContentType(ext);
      const ab = await this.app.vault.readBinary(f);
      out.push({ filename: name, contentType, data: new Uint8Array(ab) });
    }
    return out;
  }

  private async pullDeliverablesAsync(): Promise<void> {
    if (!this.sessionId) {
      new Notice("No active session.");
      return;
    }

    this.refreshRuntime();
    try {
      const snap = await this.api.getDeliverables(this.sessionId);
      await this.vault.writeDeliverablesJson(this.sessionId, snap);

      const briefMd = renderBriefMarkdown(snap?.brief);
      const deliveryMd = renderDeliveryMarkdown(snap?.delivery);
      await this.vault.writeDeliverablesMarkdown(this.sessionId, briefMd, deliveryMd);

      new Notice("Deliverables written to Vault/SRA.");
    } catch (e: any) {
      this.setError(e?.message ?? "pull deliverables failed");
      new Notice(`Pull deliverables failed: ${this.lastError}`);
    }
  }
}

function extractRunId(evt: any): string {
  const rid = (evt as any)?.runId;
  if (typeof rid === "string" && rid.trim()) return rid.trim();
  const raw = (evt as any)?.value?.runId;
  if (typeof raw === "string" && raw.trim()) return raw.trim();
  return "";
}

function guessContentType(ext: string): string {
  const e = (ext ?? "").trim().toLowerCase();
  if (e === "md") return "text/markdown";
  if (e === "txt") return "text/plain";
  if (e === "json") return "application/json";
  if (e === "pdf") return "application/pdf";
  if (e === "png") return "image/png";
  if (e === "jpg" || e === "jpeg") return "image/jpeg";
  if (e === "gif") return "image/gif";
  if (e === "webp") return "image/webp";
  return "application/octet-stream";
}


