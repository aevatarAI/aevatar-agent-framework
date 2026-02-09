// ============================================================
//  app-init.js
//
//  中文 + ASCII:
//  - 事件绑定 + 初始数据加载
// ============================================================

el.newSession.addEventListener('click', () => createSession());
if (el.workflowRefresh) {
  el.workflowRefresh.addEventListener('click', () => fetchWorkflows());
}

el.composer.addEventListener('submit', (e) => {
  e.preventDefault();
  const message = el.input.value.trim();
  if (!message) return;
  el.input.value = '';
  sendMessage(message);
});

el.input.addEventListener('keydown', (e) => {
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault();
    const message = el.input.value.trim();
    if (!message) return;
    el.input.value = '';
    sendMessage(message);
  }
});

el.pingButton.addEventListener('click', async () => {
  if (!state.currentSessionId) return;
  const content = el.pingInput.value || 'ping';
  el.pingInput.value = '';
  await fetchJson('/api/agent/ping', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ sessionId: state.currentSessionId, content }),
  });
  const line = document.createElement('div');
  line.className = 'event-item';
  line.textContent = `PING: ${content}`;
  el.pingLog.prepend(line);
});

if (el.settingsSave) {
  el.settingsSave.addEventListener('click', () => applySettings());
}

if (el.agentYamlSave) {
  el.agentYamlSave.addEventListener('click', () => saveAgentYaml());
}
if (el.agentYamlLoad) {
  el.agentYamlLoad.addEventListener('click', () => loadAgentYaml(el.agentYamlRole.value));
}

if (el.workflowRun) {
  el.workflowRun.addEventListener('click', () => loadWorkflowYaml());
}

if (el.roleRefresh) {
  el.roleRefresh.addEventListener('click', () => refreshRoleWorkspace());
}
if (el.roleSelect) {
  el.roleSelect.addEventListener('change', () => {
    const role = el.roleSelect.value;
    if (role) setActiveRole(role);
  });
}
if (el.roleYamlSave) {
  el.roleYamlSave.addEventListener('click', () => saveRoleYaml());
}
if (el.roleInstantiate) {
  el.roleInstantiate.addEventListener('click', () => instantiateRole());
}
if (el.roleLink) {
  el.roleLink.addEventListener('click', () => linkRoles());
}
if (el.roleUnlink) {
  el.roleUnlink.addEventListener('click', () => unlinkRoles());
}

if (el.roleComposer) {
  el.roleComposer.addEventListener('submit', (e) => {
    e.preventDefault();
    const message = el.roleInput.value.trim();
    if (!message) return;
    el.roleInput.value = '';
    sendRoleMessage(message);
  });
}

if (el.roleInput) {
  el.roleInput.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      const message = el.roleInput.value.trim();
      if (!message) return;
      el.roleInput.value = '';
      sendRoleMessage(message);
    }
  });
}

if (el.roleChatToggle) {
  el.roleChatToggle.addEventListener('click', () => toggleRoleChatDrawer());
}

if (el.roleSkillAdd) {
  el.roleSkillAdd.addEventListener('click', () => {
    const value = el.roleSkillInput?.value?.trim() || '';
    if (!value) return;
    el.roleSkillInput.value = '';
    addRoleSkill(value);
  });
}

if (el.roleSkillInput) {
  el.roleSkillInput.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') {
      e.preventDefault();
      const value = el.roleSkillInput.value.trim();
      if (!value) return;
      el.roleSkillInput.value = '';
      addRoleSkill(value);
    }
  });
}

if (el.roleModuleAdd) {
  el.roleModuleAdd.addEventListener('click', () => {
    const value = el.roleModuleInput?.value?.trim() || '';
    if (!value) return;
    el.roleModuleInput.value = '';
    addRoleModule(value);
  });
}

if (el.roleModuleInput) {
  el.roleModuleInput.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') {
      e.preventDefault();
      const value = el.roleModuleInput.value.trim();
      if (!value) return;
      el.roleModuleInput.value = '';
      addRoleModule(value);
    }
  });
}

el.agentYaml.addEventListener('input', debounce(() => {
  syncSelectedToolsFromYaml();
  syncProviderFromYaml();
  syncRoleSkillsFromYaml();
  syncRoleModulesFromYaml();
}, 300));

if (el.providerSelect) {
  el.providerSelect.addEventListener('change', () => {
    if (el.providerSelect.value) {
      el.agentYaml.value = updateYamlScalar(el.agentYaml.value, 'provider', el.providerSelect.value);
    }
    updateProviderStatus();
  });
}

if (el.providerSave) {
  el.providerSave.addEventListener('click', () => saveProviderKey());
}
if (el.providerClear) {
  el.providerClear.addEventListener('click', () => clearProviderKey());
}
if (el.providerDefault) {
  el.providerDefault.addEventListener('click', () => setDefaultProvider());
}

if (el.toolSearch) {
  el.toolSearch.addEventListener('input', (e) => {
    state.toolSearch = e.target.value || '';
    renderToolCatalog();
  });
}

if (el.toolFilters) {
  el.toolFilters.addEventListener('click', (e) => {
    const btn = e.target.closest('button[data-filter]');
    if (!btn) return;
    state.toolFilter = btn.dataset.filter || 'all';
    el.toolFilters.querySelectorAll('button').forEach((b) => b.classList.remove('is-active'));
    btn.classList.add('is-active');
    renderToolCatalog();
  });
}

if (el.toolRefresh) {
  el.toolRefresh.addEventListener('click', () => loadToolCatalog());
}

enableDropZone(el.selectedTools);
enableDropZone(el.agentYaml);

initTabs();
initTopNav();

if (el.messages) {
  el.messages.addEventListener('scroll', () => {
    state.autoScroll = isNearBottom(el.messages);
  });
}

if (el.streamChunkEvery) {
  el.streamChunkEvery.addEventListener('change', () => {
    getStreamChunkEveryN();
  });
}

if (el.roleMessages) {
  el.roleMessages.addEventListener('scroll', () => {
    roleState.autoScroll = isNearBottom(el.roleMessages);
  });
}

if (el.roleStreamChunkEvery) {
  el.roleStreamChunkEvery.addEventListener('change', () => {
    getRoleStreamChunkEveryN();
  });
}

(async () => {
  setStatus('loading…');
  updateInputState(); // Initial state - input disabled until SSE connected
  await fetchWorkflows();
  await fetchSessions();
  el.workflowYaml.value = sampleWorkflowYaml;
  if (state.sessions.length > 0) {
    selectSession(state.sessions[0].sessionId);
  } else {
    setStatus('ready');
    updateInputState(); // Keep input disabled when no session
  }
})();
