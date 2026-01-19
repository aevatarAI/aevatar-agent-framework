// ------------------------------------------------------------
//  OpenTUI entry
//  说明：
//  - 仅负责组装与生命周期
// ------------------------------------------------------------
import { createChatApi } from "./api/chat";
import { createProvidersApi, type ProvidersSnapshot } from "./api/providers";
import { createWorkflowsApi, type WorkflowsSnapshot } from "./api/workflows";
import { createLayout } from "./ui/layout";
import { wireInput } from "./ui/input";

const sessionId = process.env.AEVATAR_SESSION_ID ?? "unknown";
const workflow = process.env.AEVATAR_WORKFLOW ?? "hermes";
const profile = process.env.AEVATAR_PROFILE ?? "coding";
const providerEnv = process.env.AEVATAR_PROVIDER ?? "";
const modelEnv = process.env.AEVATAR_MODEL ?? "";
const backendUrl = process.env.AEVATAR_TUI_BACKEND_URL ?? "";

type Msg = { role: "you" | "assistant"; text: string };
const msgs: Msg[] = [];
let pendingAssistantIndex: number | null = null;

const layout = await createLayout({
  sessionId,
  workflow,
  profile,
  provider: providerEnv,
  model: modelEnv,
  backendUrl,
});

function renderMessages() {
  const lines = msgs.slice(-50).map((m) => `${m.role}: ${m.text}`);
  layout.messages.content = lines.join("\n");
}

function setStatus(text: string) {
  layout.statusText.content = text ? `status: ${text}` : "";
}

const providersApi = createProvidersApi(backendUrl);
let providers: string[] = [];
let currentProvider = providerEnv;
let currentModel = modelEnv;
let workflows: string[] = [];
let currentWorkflow = workflow;

function updateProviderMeta() {
  const providerText = currentProvider || "(unknown)";
  const modelText = currentModel || "-";
  const wfText = currentWorkflow || "(unknown)";
  layout.heroMeta.content = `session=${sessionId}  workflow=${wfText}  profile=${profile}  provider=${providerText}\nmodel=${modelText}  backend=${backendUrl || "(missing)"}`;
  layout.inputMeta.content = `profile=${profile}  workflow=${wfText}  provider=${providerText}  model=${modelText}`;
}

function applyProvidersSnapshot(snapshot: ProvidersSnapshot) {
  providers = (snapshot.providers ?? []).map((p) => p.name);
  currentProvider = snapshot.defaultProvider ?? currentProvider;
  currentModel = snapshot.defaultModel ?? currentModel;
  updateProviderMeta();
}

async function refreshProviders() {
  const snapshot = await providersApi.list();
  if (snapshot) applyProvidersSnapshot(snapshot);
}

const api = createChatApi(backendUrl, {
  onStatus: setStatus,
  onAssistantStart: () => {
    pendingAssistantIndex = msgs.push({ role: "assistant", text: "" }) - 1;
    renderMessages();
  },
  onAssistantDelta: (text) => {
    if (pendingAssistantIndex === null) {
      pendingAssistantIndex = msgs.push({ role: "assistant", text: "" }) - 1;
    }
    msgs[pendingAssistantIndex].text += text;
    renderMessages();
  },
  onAssistantDone: () => {
    pendingAssistantIndex = null;
    renderMessages();
    const sessionId = process.env.AEVATAR_SESSION_ID ?? "unknown";
    const runId = String(Date.now());
    const content =
      typeof layout.messages.content === "string"
        ? layout.messages.content
        : String(layout.messages.content ?? "");
    const lines = content.split("\n").length;
    const lastMsg = msgs.length > 0 ? msgs[msgs.length - 1] : null;
    // #region agent log
    fetch("http://127.0.0.1:7242/ingest/602d30ab-17ad-45f0-a915-8a7cf2e47189", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        location: "index.ts:onAssistantDone",
        message: "ui_render_summary",
        data: {
          lines,
          lastRole: lastMsg?.role ?? "",
          lastLen: lastMsg?.text.length ?? 0,
        },
        timestamp: Date.now(),
        sessionId,
        runId,
        hypothesisId: "H4",
      }),
    }).catch(() => {});
    // #endregion
  },
});

const workflowsApi = createWorkflowsApi(backendUrl);

function applyWorkflowsSnapshot(snapshot: WorkflowsSnapshot) {
  workflows = (snapshot.workflows ?? []).slice();
  currentWorkflow = snapshot.current ?? currentWorkflow;
  updateProviderMeta();
}

async function refreshWorkflows() {
  const snapshot = await workflowsApi.list();
  if (snapshot) applyWorkflowsSnapshot(snapshot);
}

async function selectWorkflow(next: string) {
  const snapshot = await workflowsApi.select(next);
  if (!snapshot) {
    setStatus("error: workflow switch failed");
    return;
  }
  applyWorkflowsSnapshot(snapshot);
  setStatus(`workflow=${snapshot.current || next}`);
}

wireInput(layout.renderer, layout.input, {
  onSubmit: (text) => {
    const trimmed = text.trim();
    if (trimmed.startsWith("/workflow")) {
      const parts = trimmed.split(/\s+/);
      if (parts.length < 2) {
        setStatus("usage: /workflow <name>");
        return;
      }
      void selectWorkflow(parts.slice(1).join(" "));
      return;
    }
    msgs.push({ role: "you", text });
    renderMessages();
    void api.send(text);
  },
  onTab: (shift) => {
  if (shift) {
    const sessionId = process.env.AEVATAR_SESSION_ID ?? "unknown";
    // #region agent log
    fetch("http://127.0.0.1:7242/ingest/602d30ab-17ad-45f0-a915-8a7cf2e47189", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        location: "index.ts:onTab",
        message: "workflow_tab_pressed",
        data: { workflowsCount: workflows.length, currentWorkflow },
        timestamp: Date.now(),
        sessionId,
        runId: String(Date.now()),
        hypothesisId: "H4",
      }),
    }).catch(() => {});
    // #endregion
      if (workflows.length === 0) {
        setStatus("no workflows");
        return;
      }
      const currentIndex = workflows.indexOf(currentWorkflow);
      const nextIndex = currentIndex >= 0 ? (currentIndex + 1) % workflows.length : 0;
      const next = workflows[nextIndex];
      if (!next) return;
      setStatus(`switching workflow -> ${next}`);
      void selectWorkflow(next);
      return;
    }
    if (providers.length === 0) {
      setStatus("no providers");
      return;
    }
    const currentIndex = providers.indexOf(currentProvider);
    const nextIndex = currentIndex >= 0 ? (currentIndex + 1) % providers.length : 0;
    const next = providers[nextIndex];
    if (!next) return;
    setStatus(`switching provider -> ${next}`);
    void providersApi.setDefault(next).then((snapshot) => {
      if (!snapshot) {
        setStatus("error: switch failed");
        return;
      }
      applyProvidersSnapshot(snapshot);
      setStatus(`provider=${snapshot.defaultProvider || next}`);
    });
  },
});

renderMessages();
updateProviderMeta();
void refreshProviders();
void refreshWorkflows();
layout.renderer.start();

