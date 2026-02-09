// ============================================================
//  app-nav.js
//
//  中文 + ASCII:
//  - 顶部导航与 Sessions 内部 Tabs
// ============================================================

function initTabs() {
  const buttons = document.querySelectorAll('.tab-button');
  buttons.forEach((btn) => {
    btn.addEventListener('click', () => {
      buttons.forEach((b) => b.classList.remove('active'));
      btn.classList.add('active');
      const tab = btn.dataset.tab;
      document.querySelectorAll('.tab-panel').forEach((panel) => {
        panel.classList.toggle('active', panel.id === `tab-${tab}`);
      });
    });
  });
}

function setView(view) {
  state.currentView = view;
  el.navButtons.forEach((btn) => {
    btn.classList.toggle('active', btn.dataset.view === view);
  });
  el.views.forEach((section) => {
    section.classList.toggle('active', section.dataset.view === view);
  });

  if (view === 'role') {
    refreshRoleWorkspace();
    connectRoleChatSse(roleState.activeRole);
    refreshRoleToolsPanel();
  } else if (roleState.chatSse) {
    roleState.chatSse.close();
    roleState.chatSse = null;
  }

  if (view === 'hierarchy') {
    refreshHierarchyWorkspace();
    connectHierarchySse();
  } else if (roleState.hierarchySse) {
    roleState.hierarchySse.close();
    roleState.hierarchySse = null;
  }
}

function initTopNav() {
  el.navButtons.forEach((btn) => {
    btn.addEventListener('click', () => {
      const view = btn.dataset.view;
      setView(view);
    });
  });
  setView(state.currentView);
}
