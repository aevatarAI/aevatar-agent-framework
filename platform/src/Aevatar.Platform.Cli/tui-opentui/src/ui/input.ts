import type { TextareaRenderable } from "@opentui/core";
import type { CliRenderer } from "@opentui/core";

// ------------------------------------------------------------
//  Input wiring
//  说明：
//  - 统一输入提交/粘贴/IME 兜底
// ------------------------------------------------------------
type InputHandlers = {
  onSubmit: (text: string) => void;
};

export function wireInput(
  renderer: CliRenderer,
  input: TextareaRenderable,
  handlers: InputHandlers
) {
  let lastImeText = "";
  let lastImeAt = 0;

  const insertImeText = (text: string) => {
    if (!text) return;
    const now = Date.now();
    if (text === lastImeText && now - lastImeAt < 30) return;
    lastImeText = text;
    lastImeAt = now;
    input.insertText(text);
  };

  input.onSubmit = () => {
    const text = (input.plainText ?? "").trim();
    if (!text) return;
    input.clear();
    handlers.onSubmit(text);
  };

  // Paste support (IME / clipboard)
  renderer.keyInput.on("paste", (e: any) => {
    const text = String(e?.text ?? "");
    if (!text) return;
    insertImeText(text);
  });

  // CJK fallback: if IME commits Unicode characters that input ignores,
  // intercept keypress and inject text manually (only when input is focused).
  renderer.keyInput.on("keypress", (key: any) => {
    if (renderer.currentFocusedRenderable !== input) return;
    const seq = typeof key?.sequence === "string" ? key.sequence : "";
    if (!seq) return;
    if (!/[^\x00-\x7F]/.test(seq)) return;
    insertImeText(seq);
    if (typeof key.preventDefault === "function") key.preventDefault();
    if (typeof key.stopPropagation === "function") key.stopPropagation();
  });

  // Raw input fallback: some IMEs commit text without triggering keypress/paste.
  renderer.on("key", (data: Buffer) => {
    if (renderer.currentFocusedRenderable !== input) return;
    const text = data.toString("utf8");
    if (!text) return;
    // Extract only non-ASCII characters (CJK/emoji) to avoid duplicating normal keys.
    const ime = Array.from(text).filter((ch) => ch.charCodeAt(0) > 0x7f).join("");
    if (!ime) return;
    insertImeText(ime);
  });
}

