import { Notice, Plugin } from "obsidian";
import { DEFAULT_SRA_SETTINGS, SraSettingsTab, type SraPluginSettings } from "./settings";
import { SraView, VIEW_TYPE_SRA } from "./ui/SraView";
import { WorkbenchView, VIEW_TYPE_SRA_WORKBENCH } from "./ui/WorkbenchView";

// ============================================================
//  Aevatar SRA (Obsidian Desktop Plugin)
//
//  MVP goal:
//  - Provide a thin UI shell to connect to SRA backend (HTTP + SSE)
//  - Persist artifacts to Vault/SRA/
//
//  NOTE:
//  - Desktop-first (Electron). Future remote is supported by configurable baseUrl.
//  - Never use port :5000 in defaults/examples (repo policy).
// ============================================================

export default class AevatarSraPlugin extends Plugin {
  settings: SraPluginSettings = { ...DEFAULT_SRA_SETTINGS };

  async onload(): Promise<void> {
    await this.loadSettings();

    this.addSettingTab(new SraSettingsTab(this.app, this));

    this.registerView(VIEW_TYPE_SRA, (leaf) => new SraView(leaf, this as any));
    this.registerView(VIEW_TYPE_SRA_WORKBENCH, (leaf) => new WorkbenchView(leaf, this as any));

    this.addCommand({
      id: "aevatar-sra-open-panel",
      name: "SRA: Open Panel",
      callback: () => void this.activateView(),
    });

    this.addCommand({
      id: "aevatar-sra-open-workbench",
      name: "SRA: Open Workbench",
      callback: () => void this.activateWorkbenchView(),
    });

    // Quick access: open Workbench from ribbon (common UX expectation in Obsidian).
    // 中文说明：
    // - 之前“看不到 UI”的主要原因是用户不知道需要从命令面板打开
    // - Ribbon 入口能显著降低学习成本（不破坏命令面板的 power-user 流程）
    this.addRibbonIcon("test-tube", "Open SRA Workbench", () => {
      void this.activateWorkbenchView();
    });

    this.addCommand({
      id: "aevatar-sra-new-session",
      name: "SRA: New Session",
      callback: async () => {
        const view = await this.activateView();
        await view.createNewSession();
      },
    });

    this.addCommand({
      id: "aevatar-sra-connect-session",
      name: "SRA: Connect to Session…",
      callback: async () => {
        const view = await this.activateView();
        const sid = await promptForTextAsync("Connect to sessionId", "sessionId");
        if (!sid) return;
        view.connectToSession(sid);
      },
    });

    this.addCommand({
      id: "aevatar-sra-send-message",
      name: "SRA: Send Message",
      callback: async () => {
        const view = await this.activateView();
        await view.sendCurrentMessage();
      },
    });

    this.addCommand({
      id: "aevatar-sra-hello",
      name: "SRA: Hello",
      callback: () => {
        new Notice("Aevatar SRA plugin loaded (skeleton).");
      },
    });
  }

  async loadSettings(): Promise<void> {
    const data = (await this.loadData().catch(() => null)) as Partial<SraPluginSettings> | null;
    this.settings = { ...DEFAULT_SRA_SETTINGS, ...(data ?? {}) };
  }

  async saveSettings(): Promise<void> {
    await this.saveData(this.settings);
  }

  private async activateWorkbenchView(): Promise<WorkbenchView> {
    const existing = this.app.workspace.getLeavesOfType(VIEW_TYPE_SRA_WORKBENCH);
    const leaf = existing[0] ?? this.app.workspace.getRightLeaf(false);
    if (!leaf) throw new Error("No workspace leaf available");

    await leaf.setViewState({ type: VIEW_TYPE_SRA_WORKBENCH, active: true });
    this.app.workspace.revealLeaf(leaf);
    return leaf.view as WorkbenchView;
  }

  private async activateView(): Promise<SraView> {
    const existing = this.app.workspace.getLeavesOfType(VIEW_TYPE_SRA);
    const leaf = existing[0] ?? this.app.workspace.getRightLeaf(false);
    if (!leaf) throw new Error("No workspace leaf available");

    await leaf.setViewState({ type: VIEW_TYPE_SRA, active: true });
    this.app.workspace.revealLeaf(leaf);
    return leaf.view as SraView;
  }
}

async function promptForTextAsync(title: string, placeholder: string): Promise<string> {
  // MVP: use browser prompt (works in desktop Electron).
  // If Obsidian introduces a better API, swap this implementation.
  const v = window.prompt(title, placeholder) ?? "";
  return v.trim();
}


