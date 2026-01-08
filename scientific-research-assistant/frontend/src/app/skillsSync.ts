type SkillsSyncDeps = {
  busy: boolean;
  setBusy: (v: boolean) => void;
  setLastError: (v: string) => void;
  setNote: (v: string) => void;
  setStatus: (v: any) => void;
  setLogs: (v: any[]) => void;
};

// ------------------------------------------------------------
//  Skills sync (best-effort)
//
//  Goal:
//  - Keep the controller hook small and readable.
//  - Preserve current UX behavior: polling status while /api/skills/sync runs.
// ------------------------------------------------------------
export async function runSkillsSync(deps: SkillsSyncDeps): Promise<void> {
  if (deps.busy) return;
  deps.setBusy(true);

  let alive = true;
  let timer: number | null = null;

  try {
    deps.setLastError("");
    deps.setNote("");
    deps.setStatus(null);
    deps.setLogs([]);

    // Poll live status while sync is running (best-effort).
    const pollOnce = async () => {
      try {
        const r = await fetch("/api/skills/sync/status");
        if (!r.ok) return;
        const st = await r.json().catch(() => null);
        if (!alive || !st) return;
        deps.setStatus(st);
        const logs = Array.isArray(st?.logs) ? st.logs : [];
        deps.setLogs(logs.slice(-12));

        const cur = st?.current;
        if (st?.running && cur?.repoUrl) {
          deps.setNote(`Syncing: ${cur.packName ?? ""} (${cur.repoUrl})`);
        }
      } catch {
        // best-effort
      }
    };

    await pollOnce();
    timer = window.setInterval(pollOnce, 800);

    const res = await fetch("/api/skills/sync", { method: "POST" });
    const json = await res.json().catch(() => null);

    // Stop polling and fetch final snapshot once.
    if (timer != null) window.clearInterval(timer);
    await pollOnce();
    alive = false;

    if (!res.ok) {
      const body = json ? JSON.stringify(json) : "";
      throw new Error(`HTTP ${res.status}${body ? `: ${body}` : ""}`);
    }

    const packs: any[] = Array.isArray(json?.packs) ? json.packs : [];
    const okCount = packs.filter((p) => Boolean(p?.ok)).length;
    const total = packs.length;

    if (json?.ok === true) {
      deps.setNote(`Skills updated: ${okCount}/${total} pack(s) ok.`);
    } else {
      deps.setNote(`Skills update partial: ${okCount}/${total} pack(s) ok.`);
      deps.setLastError(`Skills update partial failure: ${String(json?.error ?? "unknown")}`);
    }
  } catch (e: any) {
    deps.setLastError(`Skills sync failed: ${e?.message ?? String(e)}`);
  } finally {
    alive = false;
    if (timer != null) window.clearInterval(timer);
    deps.setBusy(false);
  }
}


