// ============================================================
//  app-workflow.js
//
//  中文 + ASCII:
//  - Workflow YAML → Cognitive Mesh Graph
// ============================================================

async function loadWorkflowYaml() {
  const yaml = el.workflowYaml.value.trim();
  if (!yaml) return;
  const name = el.workflowName.value || 'workspace_mesh';
  const data = await fetchJson('/api/workflow/yaml', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ yaml, name }),
  });
  if (!data.ok) {
    el.workflowStatus.textContent = `Error: ${(data.errors || []).map((e) => e.message).join('; ')}`;
    return;
  }
  el.workflowStatus.textContent = `Loaded workflow ${data.workflowId}`;
  renderGraph(data.graph, el.workflowGraph, state.graph);
}
