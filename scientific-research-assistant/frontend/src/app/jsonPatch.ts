export function applyJsonPatch(base: any, ops: any[]): any {
  // Minimal RFC6902 subset: add/replace on objects/arrays. Best-effort; fallback to base.
  let cur: any = base ?? {};

  const clone = (v: any) => (v && typeof v === "object" ? JSON.parse(JSON.stringify(v)) : v);
  cur = clone(cur);

  const getPath = (obj: any, parts: string[]) => {
    let node = obj;
    for (let i = 0; i < parts.length; i++) {
      if (node == null) return { parent: null, key: "" };
      if (i === parts.length - 1) return { parent: node, key: parts[i] };
      node = node[parts[i]];
    }
    return { parent: null, key: "" };
  };

  for (const op of ops) {
    const kind = String(op?.op ?? "");
    const path = String(op?.path ?? "");
    if (!kind || !path || !path.startsWith("/")) continue;
    const parts = path
      .split("/")
      .slice(1)
      .map((p) => p.replace(/~1/g, "/").replace(/~0/g, "~"));

    const { parent, key } = getPath(cur, parts);
    if (parent == null) continue;

    if (kind === "replace") {
      parent[key] = op?.value;
      continue;
    }

    if (kind === "add") {
      if (key === "-" && Array.isArray(parent)) {
        parent.push(op?.value);
      } else if (Array.isArray(parent)) {
        const idx = Number(key);
        if (!Number.isNaN(idx)) parent.splice(idx, 0, op?.value);
      } else {
        parent[key] = op?.value;
      }
    }
  }

  return cur;
}
