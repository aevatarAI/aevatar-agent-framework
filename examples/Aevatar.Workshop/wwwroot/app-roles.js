// ============================================================
//  app-roles.js
//
//  中文 + ASCII:
//  - Role Workspace UI (Sisyphus group)
//  - YAML 编辑 + 实例化 + 层级编排 + 群组聊天
// ============================================================

function resetRoleStreams() {
  roleState.messageMap.clear();
  roleState.streamBuffer.clear();
  if (roleState.streamFlushHandle) {
    cancelAnimationFrame(roleState.streamFlushHandle);
    roleState.streamFlushHandle = null;
  }
  roleState.autoScroll = true;
  if (el.roleMessages) {
    el.roleMessages.innerHTML = '';
  }
  if (el.delegationLog) {
    el.delegationLog.innerHTML = '';
  }
}

function clearRoleSelection() {
  roleState.selectedParent = '';
  roleState.selectedChild = '';
  updateRoleSelectionUi();
}

function updateRoleSelectionUi() {
  if (el.roleParentLabel) {
    el.roleParentLabel.textContent = roleState.selectedParent
      ? `Parent: ${roleState.selectedParent}`
      : 'Parent: -';
  }
  if (el.roleChildLabel) {
    el.roleChildLabel.textContent = roleState.selectedChild
      ? `Child: ${roleState.selectedChild}`
      : 'Child: -';
  }

  const canLink = Boolean(roleState.selectedParent && roleState.selectedChild);
  if (el.roleLink) el.roleLink.disabled = !canLink;
  if (el.roleUnlink) el.roleUnlink.disabled = !canLink;

  if (el.roleGraph) {
    el.roleGraph.querySelectorAll('.graph-node').forEach((node) => {
      const id = node.dataset.nodeId || '';
      node.classList.toggle('is-parent', id === roleState.selectedParent);
      node.classList.toggle('is-child', id === roleState.selectedChild);
    });
  }
}

function bindRoleGraphSelection() {
  if (!el.roleGraph || el.roleGraph.dataset.boundSelection === 'true') return;
  el.roleGraph.dataset.boundSelection = 'true';

  el.roleGraph.addEventListener('click', (e) => {
    const node = e.target.closest('.graph-node');
    if (!node) {
      clearRoleSelection();
      return;
    }

    const nodeId = node.dataset.nodeId || '';
    if (!nodeId) return;

    if (!roleState.selectedParent) {
      roleState.selectedParent = nodeId;
      roleState.selectedChild = '';
      updateRoleSelectionUi();
      return;
    }

    if (!roleState.selectedChild) {
      if (nodeId === roleState.selectedParent) {
        clearRoleSelection();
        return;
      }
      roleState.selectedChild = nodeId;
      updateRoleSelectionUi();
      return;
    }

    roleState.selectedParent = nodeId;
    roleState.selectedChild = '';
    updateRoleSelectionUi();
  });
}

function connectRoleSse() {
  if (roleState.sse) {
    roleState.sse.close();
    roleState.sse = null;
  }
  resetRoleStreams();
  const es = new EventSource('/api/roles/agui/events');
  roleState.sse = es;

  es.onmessage = (evt) => {
    try {
      const payload = JSON.parse(evt.data);
      handleRoleAgUiEvent(payload);
    } catch (err) {
      console.warn('Bad role SSE payload', err);
    }
  };
}

function ensureRoleMessage(id, role) {
  if (roleState.messageMap.has(id)) {
    const existing = roleState.messageMap.get(id);
    if (role && existing.role !== role) {
      existing.role = role;
      existing.node.className = `message ${role}`;
    }
    return existing;
  }
  const node = document.createElement('div');
  node.className = `message ${role}`;
  node.dataset.id = id;
  node.innerHTML = '<div class="content"></div>';
  const content = node.querySelector('.content');
  const entry = { node, content, text: '', role };
  roleState.messageMap.set(id, entry);
  if (el.roleMessages) {
    el.roleMessages.appendChild(node);
  }
  return entry;
}

function appendRoleDeltaImmediate(messageId, role, delta) {
  const existing = ensureRoleMessage(messageId, role);
  existing.text += delta;
  renderMessage(existing, false);
}

function flushRoleStreamBuffer() {
  roleState.streamFlushHandle = null;
  if (roleState.streamBuffer.size === 0) return;
  const shouldScroll = roleState.autoScroll;
  const pending = Array.from(roleState.streamBuffer.entries());
  roleState.streamBuffer.clear();
  pending.forEach(([messageId, entry]) => {
    if (!entry.delta) return;
    appendRoleDeltaImmediate(messageId, entry.role, entry.delta);
  });
  if (shouldScroll) scrollMessagesToBottom(el.roleMessages);
}

function scheduleRoleStreamFlush() {
  if (roleState.streamFlushHandle) return;
  roleState.streamFlushHandle = requestAnimationFrame(flushRoleStreamBuffer);
}

function enqueueRoleDelta(messageId, role, delta) {
  if (!delta) return;
  const entry = roleState.streamBuffer.get(messageId) || { delta: '', role };
  entry.delta += delta;
  if (role) entry.role = role;
  roleState.streamBuffer.set(messageId, entry);
  scheduleRoleStreamFlush();
}

function finalizeRoleMessage(messageId) {
  const entry = roleState.streamBuffer.get(messageId);
  if (entry && entry.delta) {
    appendRoleDeltaImmediate(messageId, entry.role, entry.delta);
    roleState.streamBuffer.delete(messageId);
  }
  const existing = roleState.messageMap.get(messageId);
  if (!existing) return;
  renderMessage(existing, true);
}

function renderRoleMessagesSnapshot(messages) {
  roleState.messageMap.clear();
  roleState.streamBuffer.clear();
  if (el.roleMessages) {
    el.roleMessages.innerHTML = '';
  }
  (messages || []).forEach((msg) => {
    const item = ensureRoleMessage(msg.id, msg.role || 'assistant');
    item.text = msg.content || '';
    renderMessage(item, true);
  });
  roleState.autoScroll = true;
  scrollMessagesToBottom(el.roleMessages);
}

function handleRoleAgUiEvent(evt) {
  if (!evt || !evt.type) return;

  if (evt.type === 'MESSAGES_SNAPSHOT') {
    renderRoleMessagesSnapshot(evt.messages || []);
    return;
  }

  if (evt.type === 'TEXT_MESSAGE_START') {
    ensureRoleMessage(evt.messageId, evt.role || 'assistant');
    return;
  }

  if (evt.type === 'TEXT_MESSAGE_CONTENT') {
    const existing = roleState.messageMap.get(evt.messageId);
    enqueueRoleDelta(evt.messageId, existing?.role || 'assistant', evt.delta || '');
    return;
  }

  if (evt.type === 'TEXT_MESSAGE_END') {
    finalizeRoleMessage(evt.messageId);
    return;
  }

  if (evt.type === 'CUSTOM' && evt.name === 'WORKSHOP_DELEGATION') {
    appendDelegationLog(evt);
    return;
  }
}

function appendDelegationLog(evt) {
  if (!el.delegationLog) return;
  const value = evt.value || {};
  const fromRole = value.fromRole || '-';
  const toRole = value.toRole || '-';
  const message = (value.message || '').trim();
  const time = formatTimestamp(evt.timestamp);
  const line = document.createElement('div');
  line.className = 'event-item';
  line.textContent = message.length > 0
    ? `${time} · ${fromRole} → ${toRole}: ${message}`
    : `${time} · ${fromRole} → ${toRole}`;
  el.delegationLog.prepend(line);
}

async function ensureSisyphusInstance() {
  try {
    await fetchJson('/api/roles/instances', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ role: roleState.rootRole || 'sisyphus', linkToRoot: false, setAsRoot: true }),
    });
  } catch (err) {
    console.warn('Failed to ensure Sisyphus', err);
  }
}

async function refreshRoleWorkspace() {
  await ensureSisyphusInstance();
  const data = await fetchJson('/api/roles');
  roleState.rootRole = data.rootRole || 'sisyphus';
  roleState.roles = data.roles || [];
  roleState.instances = data.instances || [];
  renderRoleList();
  updateRoleSelects();
  await refreshRoleGraph();
  if (roleState.roles.length > 0 && el.roleSelect && !el.roleSelect.value) {
    el.roleSelect.value = roleState.roles[0];
    loadRoleYaml(roleState.roles[0]);
  }
}

async function refreshRoleGraph() {
  const data = await fetchJson('/api/roles/graph');
  renderRoleGraph(data);
}

function renderRoleGraph(snapshot) {
  if (!snapshot || !el.roleGraph) {
    if (el.roleGraph) {
      el.roleGraph.innerHTML = '<div class="panel-subtitle">No graph loaded.</div>';
    }
    return;
  }

  const nodes = snapshot.nodes || [];
  const edges = snapshot.edges || [];
  const root = snapshot.rootRole || roleState.rootRole || 'sisyphus';

  const depthMap = buildRoleDepthMap(root, nodes, edges);
  const graph = {
    nodes: nodes.map((node) => ({
      id: node.role,
      label: node.role,
      type: node.actorId || '',
      depth: depthMap.get(node.role) ?? 0,
    })),
    edges: edges.map((edge) => ({ from: edge.parentRole, to: edge.childRole })),
  };

  renderGraph(graph, el.roleGraph, roleState.graph);
  bindRoleGraphSelection();

  const nodeIds = new Set(nodes.map((node) => node.role));
  if (roleState.selectedParent && !nodeIds.has(roleState.selectedParent)) {
    roleState.selectedParent = '';
  }
  if (roleState.selectedChild && !nodeIds.has(roleState.selectedChild)) {
    roleState.selectedChild = '';
  }
  updateRoleSelectionUi();
}

function buildRoleDepthMap(rootRole, nodes, edges) {
  const depth = new Map();
  const adjacency = {};
  edges.forEach((edge) => {
    if (!adjacency[edge.parentRole]) adjacency[edge.parentRole] = [];
    adjacency[edge.parentRole].push(edge.childRole);
  });

  const queue = [];
  if (rootRole) {
    depth.set(rootRole, 0);
    queue.push(rootRole);
  }

  while (queue.length > 0) {
    const current = queue.shift();
    const nextDepth = (depth.get(current) ?? 0) + 1;
    const children = adjacency[current] || [];
    children.forEach((child) => {
      if (!depth.has(child) || depth.get(child) > nextDepth) {
        depth.set(child, nextDepth);
        queue.push(child);
      }
    });
  }

  nodes.forEach((node) => {
    if (!depth.has(node.role)) depth.set(node.role, 0);
  });

  return depth;
}

function updateRoleSelects() {
  if (!el.roleSelect) return;
  const roles = roleState.roles.length > 0 ? roleState.roles : ['sisyphus'];

  el.roleSelect.innerHTML = '';
  roles.forEach((role) => {
    const opt = document.createElement('option');
    opt.value = role;
    opt.textContent = role;
    el.roleSelect.appendChild(opt);
  });
}

function renderRoleList() {
  if (!el.roleList) return;
  el.roleList.innerHTML = '';
  const active = new Set(roleState.instances.map((i) => i.role));
  roleState.roles.forEach((role) => {
    const item = document.createElement('div');
    item.className = 'role-item';
    if (active.has(role)) item.classList.add('active');
    const rootTag = role === roleState.rootRole ? ' · root' : '';
    const status = active.has(role) ? 'active' : 'yaml';
    const label = document.createElement('div');
    label.textContent = `${role} (${status}${rootTag})`;
    label.addEventListener('click', () => {
      if (el.roleSelect) {
        el.roleSelect.value = role;
        loadRoleYaml(role);
      }
    });

    item.appendChild(label);

    if (active.has(role) && role !== roleState.rootRole) {
      const remove = document.createElement('button');
      remove.type = 'button';
      remove.className = 'btn btn-outline btn-sm';
      remove.textContent = 'Remove';
      remove.addEventListener('click', (e) => {
        e.stopPropagation();
        removeRoleInstance(role);
      });
      item.appendChild(remove);
    }

    el.roleList.appendChild(item);
  });
}

async function loadRoleYaml(role, targetEditor = el.roleYaml, targetStatus = el.roleYamlStatus) {
  if (!role || !targetEditor) return;
  const data = await fetchJson(`/api/roles/yaml?role=${encodeURIComponent(role)}`);
  targetEditor.value = data.yaml || '';
  if (targetStatus) {
    targetStatus.textContent = data.path ? `Loaded from ${data.path}` : 'Role YAML loaded.';
  }
}

async function saveRoleYaml() {
  if (!el.roleYaml) return;
  const yaml = el.roleYaml.value.trim();
  if (!yaml) return;
  const data = await fetchJson('/api/roles/yaml', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ yaml }),
  });
  if (el.roleYamlStatus) {
    el.roleYamlStatus.textContent = `Saved ${data.role}`;
  }
  await refreshRoleWorkspace();
}

async function instantiateRole() {
  if (!el.roleSelect) return;
  const role = el.roleSelect.value;
  if (!role) return;
  await fetchJson('/api/roles/instances', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      role,
      linkToRoot: el.roleLinkRoot ? el.roleLinkRoot.checked : true,
      setAsRoot: role === roleState.rootRole,
    }),
  });
  await refreshRoleWorkspace();
}

async function removeRoleInstance(role) {
  if (!role) return;
  await fetchJson('/api/roles/instances/remove', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ role }),
  });
  if (role === roleState.selectedParent || role === roleState.selectedChild) {
    clearRoleSelection();
  }
  await refreshRoleWorkspace();
}

async function linkRoles() {
  const parentRole = roleState.selectedParent;
  const childRole = roleState.selectedChild;
  if (!parentRole || !childRole) return;
  await fetchJson('/api/roles/link', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ parentRole, childRole }),
  });
  clearRoleSelection();
  await refreshRoleGraph();
}

async function unlinkRoles() {
  const parentRole = roleState.selectedParent;
  const childRole = roleState.selectedChild;
  if (!parentRole || !childRole) return;
  await fetchJson('/api/roles/unlink', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ parentRole, childRole }),
  });
  clearRoleSelection();
  await refreshRoleGraph();
}

async function sendRoleMessage(message) {
  if (!message) return;
  const streamChunkEveryN = getRoleStreamChunkEveryN();
  const requestId = `role:${Date.now()}`;
  const payload = {
    requestId,
    userId: 'workshop-ui',
    message,
    context: {
      source: 'role_workspace',
    },
    timestamp: new Date().toISOString(),
    streamChunkEveryN,
  };
  await fetchJson('/api/roles/chat', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });
}
