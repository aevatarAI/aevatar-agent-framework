// Minimal OpenTUI frontend bootstrap.
//
// NOTE:
// - This is intentionally tiny: the .NET side owns business logic today.
// - This frontend is responsible for interactive terminal UX only.
//
// OpenTUI repo: https://github.com/anomalyco/opentui
import {
  createCliRenderer,
  BoxRenderable,
  TextRenderable,
  InputRenderable,
  InputRenderableEvents,
} from "@opentui/core";

const sessionId = process.env.AEVATAR_SESSION_ID ?? "unknown";
const workflow = process.env.AEVATAR_WORKFLOW ?? "standard";
const profile = process.env.AEVATAR_PROFILE ?? "coding";
const backendUrl = process.env.AEVATAR_TUI_BACKEND_URL ?? "";

type Msg = { role: "you" | "assistant"; text: string };
const msgs: Msg[] = [];
let status = "";

const renderer = await createCliRenderer({
  useAlternateScreen: true,
  useConsole: true,
  exitOnCtrlC: true,
});

renderer.setTerminalTitle("Aevatar Platform");

const root = new BoxRenderable(renderer, {
  id: "root",
  flexDirection: "column",
  width: "100%",
  height: "100%",
  padding: 1,
  gap: 1,
});

const header = new TextRenderable(renderer, {
  id: "header",
  content: `Aevatar Platform (OpenTUI)`,
});

const meta = new TextRenderable(renderer, {
  id: "meta",
  content: `session=${sessionId} workflow=${workflow} profile=${profile}\nbackend=${backendUrl || "(missing)"}`,
});

const messages = new TextRenderable(renderer, {
  id: "messages",
  content: "",
  flexGrow: 1,
  width: "100%",
});

const statusText = new TextRenderable(renderer, {
  id: "status",
  content: "",
});

const input = new InputRenderable(renderer, {
  id: "input",
  placeholder: "Type message and press Enter...",
  width: "100%",
  height: 1,
});

const hint = new TextRenderable(renderer, {
  id: "hint",
  content: "Enter发送 | Ctrl+C退出",
});

root.add(header);
root.add(meta);
root.add(messages);
root.add(statusText);
root.add(input);
root.add(hint);
renderer.root.add(root);

function renderMessages() {
  const lines = msgs.slice(-50).map((m) => `${m.role}: ${m.text}`);
  messages.content = lines.join("\n");
}

function setStatus(text: string) {
  status = text;
  statusText.content = status ? `status: ${status}` : "";
}

async function sendMessage(text: string) {
  if (!backendUrl) {
    setStatus("missing backend url");
    return;
  }

  setStatus("sending...");
  try {
    const res = await fetch(`${backendUrl}/api/chat`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ text }),
    });
    const json = (await res.json()) as any;
    if (!json?.ok) {
      setStatus(`error: ${json?.response ?? "unknown"}`);
      return;
    }
    msgs.push({ role: "assistant", text: String(json.response ?? "") });
    setStatus("");
    renderMessages();
  } catch (e: any) {
    setStatus(`error: ${e?.message ?? String(e)}`);
  }
}

input.on(InputRenderableEvents.ENTER, () => {
  const text = (input.value ?? "").trim();
  if (!text) return;
  msgs.push({ role: "you", text });
  input.value = "";
  renderMessages();
  void sendMessage(text);
});

renderMessages();
input.focus();
renderer.start();

