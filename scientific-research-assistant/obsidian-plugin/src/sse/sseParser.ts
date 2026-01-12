// ============================================================
//  SSE parser (Node-friendly)
//
//  Parses Server-Sent Events streams into `data:` payload blocks.
//  - Supports `\n\n` and `\r\n\r\n` delimiters
//  - Supports multi-line `data:` (joined with '\n')
// ============================================================

export class SseParser {
  private buffer = "";

  push(chunk: string): string[] {
    if (!chunk) return [];

    this.buffer += chunk;
    // Normalize CRLF to LF for easier parsing.
    this.buffer = this.buffer.replace(/\r\n/g, "\n");

    const out: string[] = [];
    while (true) {
      const idx = this.buffer.indexOf("\n\n");
      if (idx < 0) break;

      const frame = this.buffer.slice(0, idx);
      this.buffer = this.buffer.slice(idx + 2);

      const data = extractData(frame);
      if (data.length > 0) out.push(data);
    }

    return out;
  }

  flush(): string[] {
    // If the stream ends without delimiter, best-effort parse remainder.
    if (!this.buffer) return [];
    const rest = this.buffer;
    this.buffer = "";
    const data = extractData(rest.replace(/\r\n/g, "\n"));
    return data.length > 0 ? [data] : [];
  }
}

function extractData(frame: string): string {
  const lines = (frame ?? "").split("\n");
  const parts: string[] = [];
  for (const line of lines) {
    if (!line) continue;
    if (line.startsWith("data:")) {
      // Keep leading spaces in payload (SSE spec allows them), but trim one optional space.
      const raw = line.slice("data:".length);
      parts.push(raw.startsWith(" ") ? raw.slice(1) : raw);
    }
  }
  return parts.join("\n").trim();
}


