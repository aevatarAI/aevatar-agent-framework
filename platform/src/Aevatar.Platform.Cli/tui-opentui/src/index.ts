// ------------------------------------------------------------
//  OpenTUI entry
//  说明：
//  - 仅负责组装与生命周期
// ------------------------------------------------------------
import { createChatApi } from "./api/chat";
import { createLayout } from "./ui/layout";
import { wireInput } from "./ui/input";

const sessionId = process.env.AEVATAR_SESSION_ID ?? "unknown";
const workflow = process.env.AEVATAR_WORKFLOW ?? "hermes";
const profile = process.env.AEVATAR_PROFILE ?? "coding";
const backendUrl = process.env.AEVATAR_TUI_BACKEND_URL ?? "";

type Msg = { role: "you" | "assistant"; text: string };
const msgs: Msg[] = [];
let pendingAssistantIndex: number | null = null;

const layout = await createLayout({
  sessionId,
  workflow,
  profile,
  backendUrl,
});

function renderMessages() {
  const lines = msgs.slice(-50).map((m) => `${m.role}: ${m.text}`);
  layout.messages.content = lines.join("\n");
}

function setStatus(text: string) {
  layout.statusText.content = text ? `status: ${text}` : "";
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
  },
});

wireInput(layout.renderer, layout.input, {
  onSubmit: (text) => {
    msgs.push({ role: "you", text });
    renderMessages();
    void api.send(text);
  },
});

renderMessages();
layout.renderer.start();

