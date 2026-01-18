import {
  createCliRenderer,
  BoxRenderable,
  TextRenderable,
  TextareaRenderable,
} from "@opentui/core";

// ------------------------------------------------------------
//  Layout
//  说明：
//  - 只负责 UI 结构与组件创建
// ------------------------------------------------------------
export type LayoutMeta = {
  sessionId: string;
  workflow: string;
  profile: string;
  backendUrl: string;
};

export type Layout = {
  renderer: Awaited<ReturnType<typeof createCliRenderer>>;
  messages: TextRenderable;
  statusText: TextRenderable;
  input: TextareaRenderable;
};

export async function createLayout(meta: LayoutMeta): Promise<Layout> {
  const renderer = await createCliRenderer({
    useAlternateScreen: true,
    useConsole: true,
    exitOnCtrlC: true,
    // kitty keyboard improves text reporting if supported
    useKittyKeyboard: { reportText: true, disambiguate: true },
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

  const heroBox = new BoxRenderable(renderer, {
    id: "hero",
    border: true,
    title: "Aevatar",
    width: "100%",
    padding: 1,
    flexDirection: "column",
    gap: 1,
  });

  const heroTitle = new TextRenderable(renderer, {
    id: "hero-title",
    content: "Aevatar Platform",
  });

  const heroMeta = new TextRenderable(renderer, {
    id: "hero-meta",
    content: `session=${meta.sessionId}  workflow=${meta.workflow}  profile=${meta.profile}\nbackend=${meta.backendUrl || "(missing)"}`,
  });

  const heroHint = new TextRenderable(renderer, {
    id: "hero-hint",
    content: "ctrl+t variants   tab agents   ctrl+p commands",
  });

  const messageBox = new BoxRenderable(renderer, {
    id: "message-box",
    border: true,
    title: "Chat",
    flexGrow: 1,
    width: "100%",
    padding: 1,
    flexDirection: "column",
  });

  const messages = new TextRenderable(renderer, {
    id: "messages",
    content: "",
    flexGrow: 1,
    width: "100%",
    wrapMode: "word",
  });

  const statusText = new TextRenderable(renderer, {
    id: "status",
    content: "",
  });

  const inputBox = new BoxRenderable(renderer, {
    id: "input-box",
    border: true,
    title: "Input",
    width: "100%",
    padding: 1,
    flexDirection: "column",
    gap: 1,
  });

  const input = new TextareaRenderable(renderer, {
    placeholder: "Ask anything... \"Fix a TODO in the codebase\"",
    width: "100%",
    height: 1,
    keyBindings: [
      { name: "return", action: "submit" },
      { name: "enter", action: "submit" },
    ],
  });

  const inputMeta = new TextRenderable(renderer, {
    id: "input-meta",
    content: `profile=${meta.profile}  workflow=${meta.workflow}`,
  });

  const hint = new TextRenderable(renderer, {
    id: "hint",
    content: "Enter发送 | Ctrl+C退出",
  });

  messageBox.add(messages);
  inputBox.add(input);
  inputBox.add(inputMeta);

  heroBox.add(heroTitle);
  heroBox.add(heroMeta);
  heroBox.add(heroHint);

  root.add(heroBox);
  root.add(messageBox);
  root.add(statusText);
  root.add(inputBox);
  root.add(hint);

  renderer.root.add(root);
  input.focus();

  return { renderer, messages, statusText, input };
}

