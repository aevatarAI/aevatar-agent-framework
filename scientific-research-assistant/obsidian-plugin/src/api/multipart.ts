// ============================================================
//  Minimal multipart/form-data builder (browser/Node)
//
//  Goal:
//  - Upload vault files to SRA /uploads endpoint (multipart/form-data)
//  - Avoid heavy dependencies; keep it deterministic and bounded.
// ============================================================

export interface MultipartFilePart {
  fieldName: string; // e.g., "file"
  filename: string;
  contentType: string;
  data: Uint8Array;
}

export interface MultipartBody {
  contentType: string;
  body: ArrayBuffer;
}

export function buildMultipartBody(files: MultipartFilePart[], boundary?: string): MultipartBody {
  const list = Array.isArray(files) ? files : [];
  if (list.length === 0) throw new Error("no files");

  const b = boundary && boundary.trim() ? boundary.trim() : randomBoundary();
  const enc = new TextEncoder();

  const chunks: Uint8Array[] = [];

  for (const f of list) {
    const fieldName = (f?.fieldName ?? "file").trim() || "file";
    const filename = sanitizeFilename((f?.filename ?? "file").trim() || "file");
    const contentType = (f?.contentType ?? "application/octet-stream").trim() || "application/octet-stream";
    const data = f?.data instanceof Uint8Array ? f.data : new Uint8Array();

    chunks.push(enc.encode(`--${b}\r\n`));
    chunks.push(
      enc.encode(
        `Content-Disposition: form-data; name="${escapeQuotes(fieldName)}"; filename="${escapeQuotes(filename)}"\r\n`,
      ),
    );
    chunks.push(enc.encode(`Content-Type: ${contentType}\r\n\r\n`));
    chunks.push(data);
    chunks.push(enc.encode("\r\n"));
  }

  chunks.push(enc.encode(`--${b}--\r\n`));

  const merged = concatBytes(chunks);
  return {
    contentType: `multipart/form-data; boundary=${b}`,
    body: toArrayBuffer(merged),
  };
}

function randomBoundary(): string {
  // Small and safe: avoid huge hashes.
  const rnd = Math.floor(Math.random() * 1_000_000_000).toString(16);
  return `----aevatar-sra-${Date.now().toString(16)}-${rnd}`;
}

function concatBytes(chunks: Uint8Array[]): Uint8Array {
  let total = 0;
  for (const c of chunks) total += c.byteLength;

  const out = new Uint8Array(total);
  let offset = 0;
  for (const c of chunks) {
    out.set(c, offset);
    offset += c.byteLength;
  }
  return out;
}

function toArrayBuffer(u8: Uint8Array): ArrayBuffer {
  // Slice to avoid exposing a larger underlying buffer.
  return u8.buffer.slice(u8.byteOffset, u8.byteOffset + u8.byteLength);
}

function escapeQuotes(s: string): string {
  return (s ?? "").replace(/"/g, '\\"');
}

function sanitizeFilename(name: string): string {
  const s = (name ?? "").trim();
  if (!s) return "file";
  // Keep it simple; remove path separators.
  return s.replace(/[\\/]/g, "_");
}


