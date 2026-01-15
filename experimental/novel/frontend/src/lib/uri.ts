// ============================================================
//  URI helpers
// ============================================================

export function fileUriToPath(uri: string): string | null {
  if (!uri) return null;
  if (!uri.startsWith("file://")) return null;

  // Best-effort:
  // - Sidecar emits `file://{full_path}` on macOS/Linux.
  // - Windows may use `file:///C:/...` (we keep it conservative).
  try {
    const u = new URL(uri);
    const decoded = decodeURIComponent(u.pathname);
    if (decoded) return decoded;
  } catch {
    // Fallback: strip prefix.
  }

  return decodeURIComponent(uri.slice("file://".length));
}


