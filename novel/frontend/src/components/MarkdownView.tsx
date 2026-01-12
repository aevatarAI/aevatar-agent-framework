import React from "react";
import clsx from "clsx";

type Props = {
  text: string;
  className?: string;
};

type Block =
  | { kind: "p"; text: string }
  | { kind: "h"; level: number; text: string }
  | { kind: "hr" }
  | { kind: "ul"; items: string[] }
  | { kind: "ol"; items: string[] }
  | { kind: "code"; lang: string; code: string };

function parseBlocks(md: string): Block[] {
  const lines = String(md ?? "").replace(/\r\n/g, "\n").split("\n");
  const out: Block[] = [];

  let i = 0;
  while (i < lines.length) {
    const line = lines[i] ?? "";

    // Fenced code block
    const fence = line.match(/^```(\w+)?\s*$/);
    if (fence) {
      const lang = fence[1] ?? "";
      i++;
      const buf: string[] = [];
      while (i < lines.length && !/^```\s*$/.test(lines[i] ?? "")) {
        buf.push(lines[i] ?? "");
        i++;
      }
      // Skip closing fence
      if (i < lines.length && /^```\s*$/.test(lines[i] ?? "")) i++;
      out.push({ kind: "code", lang, code: buf.join("\n") });
      continue;
    }

    // Horizontal rule
    if (/^\s*---+\s*$/.test(line)) {
      out.push({ kind: "hr" });
      i++;
      continue;
    }

    // Heading
    const h = line.match(/^(#{1,6})\s+(.*)$/);
    if (h) {
      out.push({ kind: "h", level: h[1].length, text: h[2] ?? "" });
      i++;
      continue;
    }

    // Unordered list
    const ul = line.match(/^\s*[-*]\s+(.*)$/);
    if (ul) {
      const items: string[] = [];
      while (i < lines.length) {
        const m = (lines[i] ?? "").match(/^\s*[-*]\s+(.*)$/);
        if (!m) break;
        items.push(m[1] ?? "");
        i++;
      }
      out.push({ kind: "ul", items });
      continue;
    }

    // Ordered list
    const ol = line.match(/^\s*\d+\.\s+(.*)$/);
    if (ol) {
      const items: string[] = [];
      while (i < lines.length) {
        const m = (lines[i] ?? "").match(/^\s*\d+\.\s+(.*)$/);
        if (!m) break;
        items.push(m[1] ?? "");
        i++;
      }
      out.push({ kind: "ol", items });
      continue;
    }

    // Blank line -> skip
    if (!line.trim()) {
      i++;
      continue;
    }

    // Paragraph (consume until blank line or special block)
    const buf: string[] = [];
    while (i < lines.length) {
      const l = lines[i] ?? "";
      if (!l.trim()) break;
      if (/^```/.test(l)) break;
      if (/^\s*---+\s*$/.test(l)) break;
      if (/^(#{1,6})\s+/.test(l)) break;
      if (/^\s*[-*]\s+/.test(l)) break;
      if (/^\s*\d+\.\s+/.test(l)) break;
      buf.push(l);
      i++;
    }
    out.push({ kind: "p", text: buf.join("\n") });
  }

  return out;
}

function parseInline(text: string): React.ReactNode[] {
  const s = String(text ?? "");
  if (!s) return [];

  // 1) Split by code spans (`)
  const codeParts = s.split("`");
  const out: React.ReactNode[] = [];

  for (let i = 0; i < codeParts.length; i++) {
    const part = codeParts[i] ?? "";
    if (i % 2 === 1) {
      out.push(
        <code key={`c_${i}`} className="MdInlineCode">
          {part}
        </code>,
      );
      continue;
    }

    // 2) Split by bold (**)
    const boldParts = part.split("**");
    for (let j = 0; j < boldParts.length; j++) {
      const seg = boldParts[j] ?? "";
      if (j % 2 === 1) {
        out.push(
          <strong key={`b_${i}_${j}`} className="MdStrong">
            {seg}
          </strong>,
        );
      } else {
        // 3) Basic links: [text](url)
        const nodes: React.ReactNode[] = [];
        let last = 0;
        const re = /\[([^\]]+)\]\(([^)]+)\)/g;
        let m: RegExpExecArray | null = null;
        while ((m = re.exec(seg)) !== null) {
          const start = m.index;
          if (start > last) nodes.push(seg.slice(last, start));
          const label = m[1] ?? "";
          const href = m[2] ?? "";
          nodes.push(
            <a key={`a_${i}_${j}_${start}`} className="MdLink" href={href} target="_blank" rel="noreferrer">
              {label}
            </a>,
          );
          last = start + m[0].length;
        }
        if (last < seg.length) nodes.push(seg.slice(last));

        out.push(<React.Fragment key={`t_${i}_${j}`}>{nodes}</React.Fragment>);
      }
    }
  }

  return out;
}

export default function MarkdownView(props: Props) {
  const blocks = parseBlocks(props.text);
  if (blocks.length === 0) return <div className={clsx("Md", props.className)} />;

  return (
    <div className={clsx("Md", props.className)}>
      {blocks.map((b, idx) => {
        if (b.kind === "hr") return <hr key={idx} className="MdHr" />;
        if (b.kind === "code") {
          return (
            <pre key={idx} className="MdCode">
              <div className="MdCodeHeader">{b.lang ? b.lang : "code"}</div>
              <code>{b.code}</code>
            </pre>
          );
        }
        if (b.kind === "ul") {
          return (
            <ul key={idx} className="MdUl">
              {b.items.map((it, i) => (
                <li key={i}>{parseInline(it)}</li>
              ))}
            </ul>
          );
        }
        if (b.kind === "ol") {
          return (
            <ol key={idx} className="MdOl">
              {b.items.map((it, i) => (
                <li key={i}>{parseInline(it)}</li>
              ))}
            </ol>
          );
        }
        if (b.kind === "h") {
          const Tag = (`h${Math.min(6, Math.max(1, b.level))}` as any) as React.ElementType;
          return (
            <Tag key={idx} className={clsx("MdH", `MdH${b.level}`)}>
              {parseInline(b.text)}
            </Tag>
          );
        }
        // paragraph
        return (
          <p key={idx} className="MdP">
            {parseInline(b.text)}
          </p>
        );
      })}
    </div>
  );
}


