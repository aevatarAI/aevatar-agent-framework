// ============================================================
//  Deliverables renderer (JSON -> Markdown)
//
//  Goal:
//  - Provide human-friendly markdown files in Vault/SRA/
//  - Keep rendering best-effort and bounded (avoid giant notes)
// ============================================================

export function renderBriefMarkdown(brief: any): string {
  const b = brief ?? {};

  const lines: string[] = [];
  lines.push("# Research Brief");
  lines.push("");

  pushField(lines, "UpdatedAt", b.updatedAt);
  pushField(lines, "Version", b.version);
  pushField(lines, "RewrittenQuestion", b.rewrittenQuestion);
  pushField(lines, "Scope", b.scope);
  pushField(lines, "SuccessCriteria", b.successCriteria);

  pushList(lines, "Assumptions", b.assumptions);
  pushList(lines, "Risks", b.risks);
  pushList(lines, "Uncertainties", b.uncertainties);

  const terms = Array.isArray(b.terms) ? b.terms : [];
  if (terms.length > 0) {
    lines.push("");
    lines.push("## Terms");
    for (const t of terms.slice(0, 50)) {
      const term = String(t?.term ?? "").trim();
      const meaning = String(t?.meaning ?? "").trim();
      if (!term && !meaning) continue;
      lines.push(`- **${escapeMd(term || "term")}**: ${escapeMd(meaning || "")}`);
    }
  }

  const milestones = Array.isArray(b.milestones) ? b.milestones : [];
  if (milestones.length > 0) {
    lines.push("");
    lines.push("## Milestones");
    for (const m of milestones.slice(0, 50)) {
      const idx = m?.roundIndex;
      const out = String(m?.expectedOutput ?? "").trim();
      lines.push(`- round ${idx ?? ""}: ${escapeMd(out)}`);
    }
  }

  lines.push("");
  return lines.join("\n");
}

export function renderDeliveryMarkdown(delivery: any): string {
  const lines: string[] = [];
  lines.push("# Delivery Snapshot");
  lines.push("");

  const json = safePrettyJson(delivery);
  lines.push("```json");
  lines.push(trimForNote(json, 20_000));
  lines.push("```");
  lines.push("");

  return lines.join("\n");
}

function pushField(lines: string[], name: string, value: any) {
  const v = String(value ?? "").trim();
  if (!v) return;
  lines.push(`- **${name}**: ${escapeMd(v)}`);
}

function pushList(lines: string[], title: string, value: any) {
  const arr = Array.isArray(value) ? value : [];
  if (arr.length === 0) return;
  lines.push("");
  lines.push(`## ${title}`);
  for (const x of arr.slice(0, 100)) {
    const s = String(x ?? "").trim();
    if (!s) continue;
    lines.push(`- ${escapeMd(s)}`);
  }
}

function safePrettyJson(obj: any): string {
  try {
    return JSON.stringify(obj ?? null, null, 2) ?? "null";
  } catch {
    return "null";
  }
}

function trimForNote(text: string, maxChars: number): string {
  const s = String(text ?? "");
  if (s.length <= maxChars) return s;
  return s.slice(0, maxChars) + "\n... (truncated)\n";
}

function escapeMd(s: string): string {
  // Minimal escaping for list/heading stability.
  return (s ?? "").replace(/\r/g, "").replace(/\n/g, " ").trim();
}


