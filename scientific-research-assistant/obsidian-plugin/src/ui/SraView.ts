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
    containerEl.addClass("aevatar-sra");
    containerEl.addClass("aevatar-sra-view");

    // ------------------------------------------------------------
    //  Layout: a compact, platform-like panel (no Tailwind runtime)
    // ------------------------------------------------------------

    const root = containerEl.createDiv({ cls: "aevatar-sra-panel" });

    // Header
    const header = root.createDiv({ cls: "aevatar-sra-panel__header" });
    const title = header.createDiv({ cls: "aevatar-sra-panel__title" });
    title.createEl("div", { text: "Aevatar SRA", cls: "aevatar-sra-panel__titleText" });
    title.createEl("div", { text: "Obsidian research workbench (SSE + Vault)", cls: "aevatar-sra-panel__subtitle" });

    const badges = header.createDiv({ cls: "aevatar-sra-panel__badges" });
    this.elStatus = badges.createEl("span", { text: "SSE: Disconnected", cls: "aevatar-sra-badge" });
    this.elRun = badges.createEl("span", { text: "run: (none)", cls: "aevatar-sra-badge aevatar-sra-badge--mono" });

    // Session row
    const sessionRow = root.createDiv({ cls: "aevatar-sra-panel__row" });
    const sessionBox = sessionRow.createDiv({ cls: "aevatar-sra-field" });
    sessionBox.createEl("div", { text: "Session", cls: "aevatar-sra-field__label" });
    this.elSession = sessionBox.createEl("input", { cls: "aevatar-sra-input" });
    this.elSession.type = "text";
    this.elSession.placeholder = "sessionId";

    const sessionActions = sessionRow.createDiv({ cls: "aevatar-sra-actions" });
    const btnConnect = sessionActions.createEl("button", { text: "Connect", cls: "aevatar-sra-btn" });
    btnConnect.onclick = () => this.connectToSession(this.elSession.value);
    const btnNew = sessionActions.createEl("button", { text: "New Session", cls: "aevatar-sra-btn aevatar-sra-btn--primary" });
    btnNew.onclick = () => void this.createNewSession();

    // Composer row
    const composeRow = root.createDiv({ cls: "aevatar-sra-panel__row" });
    const modeBox = composeRow.createDiv({ cls: "aevatar-sra-field" });
    modeBox.createEl("div", { text: "Mode", cls: "aevatar-sra-field__label" });
    this.elMode = modeBox.createEl("select", { cls: "aevatar-sra-select" });
    for (const m of ["chat", "vibe", "vibe_loop"]) {
      const opt = this.elMode.createEl("option");
      opt.value = m;
      opt.text = m;
    }

    const msgBox = composeRow.createDiv({ cls: "aevatar-sra-field aevatar-sra-field--grow" });
    msgBox.createEl("div", { text: "Message", cls: "aevatar-sra-field__label" });
    this.elMessage = msgBox.createEl("textarea", { cls: "aevatar-sra-textarea" });
    this.elMessage.placeholder = "Ask a research question…";
    this.elMessage.rows = 4;

    const composeActions = composeRow.createDiv({ cls: "aevatar-sra-actions aevatar-sra-actions--stack" });
    const btnSend = composeActions.createEl("button", { text: "Send", cls: "aevatar-sra-btn aevatar-sra-btn--primary" });
    btnSend.onclick = () => void this.sendCurrentMessage();
    const btnPull = composeActions.createEl("button", { text: "Sync Deliverables", cls: "aevatar-sra-btn" });
    btnPull.onclick = () => void this.pullDeliverablesAsync();

    // Attachments row
    const attachRow = root.createDiv({ cls: "aevatar-sra-panel__row" });
    const attachInfo = attachRow.createDiv({ cls: "aevatar-sra-field aevatar-sra-field--grow" });
    attachInfo.createEl("div", { text: "Attachments", cls: "aevatar-sra-field__label" });
    this.elAttachments = attachInfo.createDiv({ cls: "aevatar-sra-muted" });
    this.elAttachments.setText("Attachments: (none)");

    const attachActions = attachRow.createDiv({ cls: "aevatar-sra-actions" });
    const btnAttach = attachActions.createEl("button", { text: "Attach current note", cls: "aevatar-sra-btn" });
    btnAttach.onclick = () => void this.attachCurrentNoteAsync();
    const btnClear = attachActions.createEl("button", { text: "Clear", cls: "aevatar-sra-btn" });
    btnClear.onclick = () => {
      this.attachments = [];
      this.renderAttachments();
    };

    // Error / status note
    this.elError = root.createDiv({ cls: "aevatar-sra-error" });
    this.elError.setText("");

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


