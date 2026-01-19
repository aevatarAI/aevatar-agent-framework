// ------------------------------------------------------------
//  Workflows API
//  说明：
//  - 获取 / 切换 workflow
// ------------------------------------------------------------
export type WorkflowsSnapshot = {
  current: string;
  workflows: string[];
};

export type WorkflowsApi = {
  list: () => Promise<WorkflowsSnapshot | null>;
  select: (workflow: string) => Promise<WorkflowsSnapshot | null>;
};

export function createWorkflowsApi(backendUrl: string): WorkflowsApi {
  async function list() {
    if (!backendUrl) return null;
    try {
      const res = await fetch(`${backendUrl}/api/workflows`);
      if (!res.ok) return null;
      return (await res.json()) as WorkflowsSnapshot;
    } catch {
      return null;
    }
  }

  async function select(workflow: string) {
    if (!backendUrl) return null;
    try {
      const res = await fetch(`${backendUrl}/api/workflows/select`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ workflow }),
      });
      if (!res.ok) return null;
      return (await res.json()) as WorkflowsSnapshot;
    } catch {
      return null;
    }
  }

  return { list, select };
}
