/**
 * Demo plugin: append a fixed tag (deterministic).
 */
export function transform(text, _ctx) {
  return `${String(text)}\n[plugin_append_tag]`;
}


