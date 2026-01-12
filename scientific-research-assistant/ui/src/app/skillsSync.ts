import type { SraTransport } from "../transport/SraTransport";

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
//  中文说明：
//  - 通过 transport 访问 /api/skills/*，避免宿主差异（Web/Obsidian）
//  - 维持现有 UX：sync 期间轮询 status 并展示 logs
// ------------------------------------------------------------
export async function runSkillsSync(args: { transport: SraTransport; deps: SkillsSyncDeps }): Promise<void> {
  const { transport, deps } = args;
  if (deps.busy) return;
  deps.setBusy(true);

  let alive = true;
  let timer: ReturnType<typeof setInterval> | null = null;

  const getJson = transport.getJson;
  const postJson = transport.postJson;
  if (!getJson || !postJson) {
    deps.setLastError("Skills sync unavailable: transport lacks getJson/postJson.");
    deps.setBusy(false);
    return;
  }

  try {
    deps.setLastError("");
    deps.setNote("");
    deps.setStatus(null);
    deps.setLogs([]);

    // Poll live status while sync is running (best-effort).
    const pollOnce = async () => {
      try {
        const st = await getJson("/api/skills/sync/status");
        if (!alive || !st) return;
        deps.setStatus(st);
        const logs = Array.isArray((st as any)?.logs) ? (st as any).logs : [];
        deps.setLogs(logs.slice(-12));

        const cur = (st as any)?.current;
        if ((st as any)?.running && cur?.repoUrl) {
          deps.setNote(`Syncing: ${cur.packName ?? ""} (${cur.repoUrl})`);
        }
      } catch {
        // best-effort
      }
    };

    await pollOnce();
    timer = setInterval(pollOnce, 800);

    const json = await postJson("/api/skills/sync", {});

    // Stop polling and fetch final snapshot once.
    if (timer != null) clearInterval(timer);
    await pollOnce();
    alive = false;

    const packs: any[] = Array.isArray((json as any)?.packs) ? (json as any).packs : [];
    const okCount = packs.filter((p) => Boolean(p?.ok)).length;
    const total = packs.length;

    if ((json as any)?.ok === true) {
      deps.setNote(`Skills updated: ${okCount}/${total} pack(s) ok.`);
    } else {
      deps.setNote(`Skills update partial: ${okCount}/${total} pack(s) ok.`);
      deps.setLastError(`Skills update partial failure: ${String((json as any)?.error ?? "unknown")}`);
    }
  } catch (e: any) {
    deps.setLastError(`Skills sync failed: ${e?.message ?? String(e)}`);
  } finally {
    alive = false;
    if (timer != null) clearInterval(timer);
    deps.setBusy(false);
  }
}


