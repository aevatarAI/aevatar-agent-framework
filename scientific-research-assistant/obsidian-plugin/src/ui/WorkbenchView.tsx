import { ItemView, WorkspaceLeaf } from "obsidian";
import React from "react";
import { createRoot, type Root } from "react-dom/client";

import { SraWorkbenchApp } from "../../../ui/src";
import { createObsidianTransport } from "../transport/ObsidianTransport";

export const VIEW_TYPE_SRA_WORKBENCH = "aevatar-sra-workbench-view";

type PluginLike = {
  app: any;
  settings: {
    baseUrl: string;
    requestTimeoutMs: number;
  };
};

// ============================================================
//  WorkbenchView (React host)
//
//  中文说明：
//  - 这是 Obsidian 内承载 shared React UI 的容器
//  - transport 使用 requestUrl + Node SSE（避免 CORS）
//  - 当前阶段：先把 UI 跑起来；Tailwind 样式与 local-only gating 在后续任务完善
// ============================================================

export class WorkbenchView extends ItemView {
  private readonly plugin: PluginLike;
  private reactRoot: Root | null = null;
  private mountEl: HTMLElement | null = null;

  constructor(leaf: WorkspaceLeaf, plugin: PluginLike) {
    super(leaf);
    this.plugin = plugin;
  }

  getViewType(): string {
    return VIEW_TYPE_SRA_WORKBENCH;
  }

  getDisplayText(): string {
    return "SRA Workbench";
  }

  async onOpen(): Promise<void> {
    const { containerEl } = this;
    containerEl.empty();
    containerEl.addClass("aevatar-sra");
    containerEl.addClass("aevatar-sra-workbench");
    containerEl.addClass("aevatar-sra-host");

    // NOTE: We intentionally do not use window.location routing in Obsidian.
    // The shared app currently has a few `href="/?...` links for the web host.
    // Until we add a proper host router, prevent accidental navigation away.
    containerEl.addEventListener("click", this.onLinkClickCapture, true);

    this.mountEl = containerEl.createDiv({ cls: "aevatar-sra-workbench-root" });
    // Make the React app fill the view (Obsidian's layout is not 100vh-based).
    this.mountEl.addClass("aevatar-sra-fill");
    this.reactRoot = createRoot(this.mountEl);

    const transport = createObsidianTransport({
      baseUrl: this.plugin.settings.baseUrl,
      timeoutMs: this.plugin.settings.requestTimeoutMs,
    });

    this.reactRoot.render(<SraWorkbenchApp transport={transport} />);
  }

  async onClose(): Promise<void> {
    try {
      this.reactRoot?.unmount();
    } catch {
      // ignore
    }
    this.reactRoot = null;
    this.mountEl = null;

    try {
      this.containerEl.removeEventListener("click", this.onLinkClickCapture, true);
    } catch {
      // ignore
    }
  }

  // ------------------------------------------------------------
  //  Internals
  // ------------------------------------------------------------

  private onLinkClickCapture = (ev: MouseEvent) => {
    const t = ev.target as HTMLElement | null;
    const a = t?.closest?.("a") as HTMLAnchorElement | null;
    if (!a) return;

    const href = String(a.getAttribute("href") ?? "").trim();
    if (!href) return;

    // Prevent navigating Obsidian app shell away (until host routing is added).
    if (href.startsWith("/") || href.startsWith("app://") || href.startsWith("obsidian://")) {
      ev.preventDefault();
      ev.stopPropagation();
    }
  };
}


