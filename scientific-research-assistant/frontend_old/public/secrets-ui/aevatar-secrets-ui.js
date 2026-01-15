// ============================================================
//  Aevatar.Secrets.Ui (vanilla, reusable)
//
//  Goals:
//  - Keep Program.cs clean (no embedded HTML/JS).
//  - Ship a static UI that can be hosted by ANY ASP.NET Core app.
//
//  Notes:
//  - This UI assumes APIs exist at:
//      /api/llm/*  and  /api/secrets/*
//  - All write APIs are localhost-only on the server side.
// ============================================================

(function () {
  const $ = (id) => document.getElementById(id);
  const state = {
    providers: [],
    instances: [],
    trash: [],
    defaultProvider: "",
    embeddings: { enabled: null, providerType: "", model: "", endpoint: "", configured: false, masked: "" },
    skillsMp: { configured: false, masked: "", keyPath: "SkillsMP:ApiKey", baseUrl: "" },
    selectedId: "",
    selectedProviderType: "",
    nameEdited: false,
    search: "",
    keyShown: false,
    // If user starts typing a new key, we should not overwrite the input while refreshing details.
    isNewKeyDraft: false,
    hasExistingKey: false,
    existingKeyMasked: "",
    existingKeyFull: "",
    advShown: false,
    endpointOriginal: "",
    endpointSource: "",
    modelOriginal: "",
    modelSource: "",

    // Embeddings view state (separate from LLM instance connect view)
    embKeyShown: false,
    embIsNewKeyDraft: false,
    embHasExistingKey: false,
    embExistingKeyMasked: "",
    embExistingKeyFull: "",

    // SkillsMP view state
    skillsMpKeyShown: false,
    skillsMpIsNewKeyDraft: false,
    skillsMpHasExistingKey: false,
    skillsMpExistingKeyMasked: "",
    skillsMpExistingKeyFull: "",
  };
  const categoryOrder = { configured: 0, popular: 1, other: 2 };
  const safeText = (s) => String(s || "");
  const upper1 = (s) => safeText(s).trim().slice(0, 1).toUpperCase();
  const isEmpty = (s) => !safeText(s).trim();
  const debounce = (fn, ms) => {
    let t = null;
    return (...args) => {
      if (t) window.clearTimeout(t);
      t = window.setTimeout(() => fn(...args), ms);
    };
  };

  function sanitizeModelForInstanceName(model) {
    const raw = safeText(model || "").trim();
    if (!raw) return "";
    // Keep it simple + stable: replace non [a-zA-Z0-9._-] with '-'
    const s = raw.replace(/[^a-zA-Z0-9._-]+/g, "-").replace(/-+/g, "-").replace(/^-+/, "").replace(/-+$/, "");
    return s || raw.replace(/[^a-zA-Z0-9]+/g, "-");
  }

  function setView(view) {
    $("viewList").classList.toggle("hidden", view !== "list");
    $("viewConnect").classList.toggle("hidden", view !== "connect");
    $("viewAdvanced").classList.toggle("hidden", view !== "advanced");
    $("viewEmbeddings").classList.toggle("hidden", view !== "embeddings");
    $("viewSkillsMp").classList.toggle("hidden", view !== "skillsmp");
  }

  function findProviderType(id) {
    const key = String(id || "").toLowerCase();
    return state.providers.find((p) => String(p.id || "").toLowerCase() === key) || null;
  }

  function findInstance(name) {
    const key = String(name || "").toLowerCase();
    return state.instances.find((it) => String(it.name || "").toLowerCase() === key) || null;
  }

  function matches(p, q) {
    const hay = (safeText(p.displayName) + " " + safeText(p.id) + " " + safeText(p.description)).toLowerCase();
    return hay.includes(q);
  }

  function matchesInstance(it, q) {
    const hay = (
      safeText(it.name) +
      " " +
      safeText(it.providerType) +
      " " +
      safeText(it.providerDisplayName) +
      " " +
      safeText(it.model)
    ).toLowerCase();
    return hay.includes(q);
  }

  function matchesTrash(it, q) {
    const hay = (
      safeText(it.providerName) +
      " " +
      safeText(it.providerType) +
      " " +
      safeText(it.model) +
      " " +
      safeText(it.endpoint)
    ).toLowerCase();
    return hay.includes(q);
  }

  async function refreshProviders() {
    try {
      const [pRes, iRes, tRes, dRes, eRes, sRes] = await Promise.all([
        fetch("/api/llm/providers"),
        fetch("/api/llm/instances"),
        fetch("/api/trash/api-keys"),
        fetch("/api/llm/default"),
        fetch("/api/embeddings"),
        fetch("/api/skillsmp/status"),
      ]);
      if (!pRes.ok) throw new Error("HTTP " + pRes.status);
      if (!iRes.ok) throw new Error("HTTP " + iRes.status);
      if (!tRes.ok) throw new Error("HTTP " + tRes.status);
      if (!dRes.ok) throw new Error("HTTP " + dRes.status);
      if (!eRes.ok) throw new Error("HTTP " + eRes.status);
      if (!sRes.ok) throw new Error("HTTP " + sRes.status);

      const pJson = await pRes.json().catch(() => null);
      const iJson = await iRes.json().catch(() => null);
      const tJson = await tRes.json().catch(() => null);
      const dJson = await dRes.json().catch(() => null);
      const eJson = await eRes.json().catch(() => null);
      const sJson = await sRes.json().catch(() => null);

      state.providers = Array.isArray(pJson && pJson.providers) ? pJson.providers : [];
      state.instances = Array.isArray(iJson && iJson.instances) ? iJson.instances : [];
      state.trash = Array.isArray(tJson && tJson.items) ? tJson.items : [];
      state.defaultProvider = safeText(dJson && dJson.providerName).trim();
      state.embeddings = (eJson && eJson.embeddings) ? eJson.embeddings : state.embeddings;
      state.skillsMp = (sJson && sJson.ok === true) ? sJson : state.skillsMp;
    } catch (e) {
      console.error(e);
      state.providers = [];
      state.instances = [];
      state.trash = [];
      state.defaultProvider = "";
      state.embeddings = { enabled: null, providerType: "", model: "", endpoint: "", configured: false, masked: "" };
      state.skillsMp = { configured: false, masked: "", keyPath: "SkillsMP:ApiKey", baseUrl: "" };
    }
    renderList();
  }

  function renderProviderSection(containerId, items) {
    const root = $(containerId);
    root.innerHTML = "";
    for (const p of items) {
      const row = document.createElement("div");
      row.className = "item";
      row.onclick = () => openConnect(p.id);

      const logo = document.createElement("div");
      logo.className = "logo";
      logo.textContent = upper1(p.displayName || p.id);

      const main = document.createElement("div");
      main.className = "item-main";

      const name = document.createElement("div");
      name.className = "item-name";
      name.textContent = safeText(p.displayName || p.id);

      if (p.recommended) {
        const badge = document.createElement("span");
        badge.className = "badge rec";
        badge.textContent = "Recommended";
        name.appendChild(badge);
      }
      const cnt = Number(p.configuredInstancesCount || 0) || 0;
      if (cnt > 0) {
        const badge = document.createElement("span");
        badge.className = "badge ok";
        badge.textContent = `${cnt} configured`;
        name.appendChild(badge);
      }

      const desc = document.createElement("div");
      desc.className = "item-desc";
      desc.textContent = safeText(p.description || "");

      main.appendChild(name);
      main.appendChild(desc);

      const chev = document.createElement("div");
      chev.className = "chev";
      chev.textContent = "›";

      row.appendChild(logo);
      row.appendChild(main);
      row.appendChild(chev);
      root.appendChild(row);
    }
  }

  function renderInstanceSection(containerId, items) {
    const root = $(containerId);
    root.innerHTML = "";
    for (const it of items) {
      const row = document.createElement("div");
      row.className = "item";
      row.onclick = () => openConnect(it.name);

      const logo = document.createElement("div");
      logo.className = "logo";
      logo.textContent = upper1(it.providerDisplayName || it.providerType || it.name);

      const main = document.createElement("div");
      main.className = "item-main";

      const name = document.createElement("div");
      name.className = "item-name";
      name.textContent = safeText(it.name || "");

      const badge = document.createElement("span");
      badge.className = "badge ok";
      badge.textContent = "Configured";
      name.appendChild(badge);

      if (state.defaultProvider && safeText(state.defaultProvider).trim().toLowerCase() === safeText(it.name).trim().toLowerCase()) {
        const def = document.createElement("span");
        def.className = "badge def";
        def.textContent = "Default";
        name.appendChild(def);
      }

      const desc = document.createElement("div");
      desc.className = "item-desc";
      const prov = safeText(it.providerDisplayName || it.providerType || "");
      const model = safeText(it.model || "");
      desc.textContent = prov && model ? `${prov} · ${model}` : (prov || model || "");

      main.appendChild(name);
      main.appendChild(desc);

      const chev = document.createElement("div");
      chev.className = "chev";
      chev.textContent = "›";

      row.appendChild(logo);
      row.appendChild(main);
      row.appendChild(chev);
      root.appendChild(row);
    }
  }

  function renderTrashSection(containerId, items) {
    const root = $(containerId);
    root.innerHTML = "";

    for (const it of items) {
      const name = safeText(it.providerName || "").trim();
      if (!name) continue;

      const row = document.createElement("div");
      row.className = "item";
      row.style.cursor = "default";

      const logo = document.createElement("div");
      logo.className = "logo";
      logo.textContent = "🗑";

      const main = document.createElement("div");
      main.className = "item-main";

      const title = document.createElement("div");
      title.className = "item-name";
      title.textContent = name;

      const badge = document.createElement("span");
      badge.className = "badge";
      badge.textContent = "Trashed";
      title.appendChild(badge);

      const desc = document.createElement("div");
      desc.className = "item-desc";
      const pt = safeText(it.providerType || "");
      const model = safeText(it.model || "");
      const when = typeof it.trashedAtUnixMs === "number" ? new Date(it.trashedAtUnixMs).toLocaleString() : "";
      const left = [pt, model].filter(Boolean).join(" · ");
      desc.textContent = left + (when ? ` · ${when}` : "");

      main.appendChild(title);
      main.appendChild(desc);

      const actions = document.createElement("div");
      actions.className = "trash-actions";

      const restoreBtn = document.createElement("button");
      restoreBtn.className = "mini-btn";
      restoreBtn.textContent = "Restore";
      restoreBtn.onclick = async (e) => {
        e.preventDefault();
        e.stopPropagation();
        try {
          const res = await fetch("/api/trash/api-key/" + encodeURIComponent(name) + "/restore", { method: "POST" });
          const json = await res.json().catch(() => null);
          if (!res.ok || !json || json.ok !== true) throw new Error((json && json.error) ? json.error : ("HTTP " + res.status));
          await refreshProviders();
          try {
            if (window.parent && window.parent !== window) {
              window.parent.postMessage({ type: "aevatar-secrets-ui:changed", action: "restore", providerName: name }, "*");
            }
          } catch {
            // ignore
          }
        } catch (err) {
          setConnectMsg(err && err.message ? err.message : String(err), "err");
        }
      };

      const delBtn = document.createElement("button");
      delBtn.className = "mini-btn danger";
      delBtn.textContent = "Delete";
      delBtn.onclick = async (e) => {
        e.preventDefault();
        e.stopPropagation();
        if (!window.confirm(`Delete API key from Trash permanently?\n\n${name}`)) return;
        try {
          const res = await fetch("/api/trash/api-key/" + encodeURIComponent(name), { method: "DELETE" });
          const json = await res.json().catch(() => null);
          if (!res.ok || !json || json.ok !== true) throw new Error((json && json.error) ? json.error : ("HTTP " + res.status));
          await refreshProviders();
          try {
            if (window.parent && window.parent !== window) {
              window.parent.postMessage({ type: "aevatar-secrets-ui:changed", action: "trash_delete", providerName: name }, "*");
            }
          } catch {
            // ignore
          }
        } catch (err) {
          setConnectMsg(err && err.message ? err.message : String(err), "err");
        }
      };

      actions.appendChild(restoreBtn);
      actions.appendChild(delBtn);

      row.appendChild(logo);
      row.appendChild(main);
      row.appendChild(actions);
      root.appendChild(row);
    }
  }

  function renderList() {
    const q = safeText(state.search).trim().toLowerCase();
    const providers = q ? state.providers.filter((p) => matches(p, q)) : state.providers.slice();
    const instances = q ? state.instances.filter((it) => matchesInstance(it, q)) : state.instances.slice();
    const trash = q ? state.trash.filter((it) => matchesTrash(it, q)) : state.trash.slice();

    providers.sort((a, b) => {
      const ra = categoryOrder[String(a.category || "")] ?? 9;
      const rb = categoryOrder[String(b.category || "")] ?? 9;
      if (ra !== rb) return ra - rb;
      return String(a.displayName || a.id).localeCompare(String(b.displayName || b.id), undefined, { sensitivity: "base" });
    });

    instances.sort((a, b) => {
      const pa = safeText(a.providerDisplayName || a.providerType || "");
      const pb = safeText(b.providerDisplayName || b.providerType || "");
      const c = pa.localeCompare(pb, undefined, { sensitivity: "base" });
      if (c !== 0) return c;
      return safeText(a.name).localeCompare(safeText(b.name), undefined, { sensitivity: "base" });
    });

    const popular = providers.filter((p) => p.category === "popular");
    const other = providers.filter((p) => p.category === "other");

    $("secDefault").classList.toggle("hidden", instances.length === 0);
    $("secConfigured").classList.toggle("hidden", instances.length === 0);
    $("secTrash").classList.toggle("hidden", trash.length === 0);

    renderDefaultSection(instances);
    try {
      const h = $("embeddingsHint");
      if (h) {
        const e = state.embeddings || {};
        const cfg = Boolean(e.configured);
        const masked = safeText(e.masked || "").trim();
        h.textContent = cfg ? `Configured: ${masked || "(hidden)"}` : "Not configured yet.";
      }
    } catch {
      // ignore
    }

    try {
      const h = $("skillsMpHint");
      if (h) {
        const s = state.skillsMp || {};
        const cfg = Boolean(s.configured);
        const masked = safeText(s.masked || "").trim();
        h.textContent = cfg ? `Configured: ${masked || "(hidden)"}` : "Not configured yet.";
      }
    } catch {
      // ignore
    }
    renderInstanceSection("listConfigured", instances);
    renderTrashSection("listTrash", trash);
    renderProviderSection("listPopular", popular);
    renderProviderSection("listOther", other);
  }

  function renderDefaultSection(instances) {
    const sel = $("defaultSelect");
    const hint = $("defaultHint");
    const btn = $("defaultSetBtn");
    if (!sel || !btn) return;

    // Rebuild options
    const cur = safeText(state.defaultProvider).trim();
    sel.innerHTML = "";
    const opt0 = document.createElement("option");
    opt0.value = "";
    opt0.textContent = "-- select default --";
    sel.appendChild(opt0);

    for (const it of instances || []) {
      const name = safeText(it.name).trim();
      if (!name) continue;
      const opt = document.createElement("option");
      opt.value = name;
      opt.textContent = name;
      sel.appendChild(opt);
    }

    // Select current
    if (cur) {
      try { sel.value = cur; } catch {}
    } else {
      sel.value = "";
    }

    if (hint) {
      hint.textContent = cur ? `Current: ${cur}` : "Not set yet (will be auto-selected after you configure a provider).";
    }

    updateDefaultButtons();
  }

  function updateDefaultButtons() {
    const sel = $("defaultSelect");
    const btn = $("defaultSetBtn");
    if (!sel || !btn) return;
    const v = safeText(sel.value).trim();
    const cur = safeText(state.defaultProvider).trim();
    btn.disabled = isEmpty(v) || v.toLowerCase() === cur.toLowerCase();
  }

  async function setDefaultProvider() {
    const name = safeText($("defaultSelect").value).trim();
    if (isEmpty(name)) return;

    $("defaultSetBtn").disabled = true;
    try {
      const res = await fetch("/api/llm/default", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ providerName: name }),
      });
      const json = await res.json().catch(() => null);
      if (!res.ok || !json || json.ok !== true) throw new Error((json && json.error) ? json.error : ("HTTP " + res.status));

      state.defaultProvider = name;
      await refreshProviders();

      try {
        if (window.parent && window.parent !== window) {
          window.parent.postMessage({ type: "aevatar-secrets-ui:changed", action: "set_default", providerName: name }, "*");
        }
      } catch {
        // ignore
      }
    } catch (e) {
      // Reuse connect msg for visibility (modal UX); this UI is local-only anyway.
      setConnectMsg(e && e.message ? e.message : String(e), "err");
    } finally {
      updateDefaultButtons();
    }
  }

  function setConnectMsg(text, kind) {
    const el = $("connectMsg");
    el.textContent = safeText(text);
    el.className = "msg";
    if (kind === "ok") el.classList.add("ok");
    if (kind === "err") el.classList.add("err");
  }

  function setAdvMsg(text, kind) {
    const el = $("advMsg");
    el.textContent = safeText(text);
    el.className = "msg";
    if (kind === "ok") el.classList.add("ok");
    if (kind === "err") el.classList.add("err");
  }

  function updateSubmitEnabled() {
    const pn = safeText($("providerNameInput").value).trim();
    const key = safeText($("apiKeyInput").value).trim();
    const model = safeText($("modelSelect").value).trim();
    const reuseFrom = safeText($("reuseKeySelect") ? $("reuseKeySelect").value : "").trim();

    const isConfiguredInstance = state.instances.some(
      (x) => safeText(x.name || "").trim().toLowerCase() === pn.toLowerCase()
    );

    // Safety:
    // - Never write API key unless user is actively drafting a new key (state.isNewKeyDraft).
    // - But allow saving Endpoint/Model settings even without changing API key.
    const keyOk = state.isNewKeyDraft
      ? !isEmpty(key)
      : (isConfiguredInstance || !isEmpty(reuseFrom));

    $("submitBtn").disabled = isEmpty(pn) || isEmpty(model) || isEmpty(state.selectedProviderType) || !keyOk;

    // Test / Fetch models should be available as soon as we have a key source:
    // - configured key on this instance, OR
    // - selected reuseFrom instance, OR
    // - user is typing a new key draft (no persistence; probe endpoints are used)
    const canUseDraftKey = state.isNewKeyDraft && !isEmpty(key);
    const canUseStoredKey = state.hasExistingKey || isConfiguredInstance;
    const canUseReuseKey = !isEmpty(reuseFrom);
    const canProbe = canUseDraftKey || canUseStoredKey || canUseReuseKey;

    $("testBtn").disabled = !canProbe;
    $("modelsBtn").disabled = !canProbe;
  }

  function renderModelMeta(modelSource, model) {
    const src = safeText(modelSource || "unknown");
    const m = safeText(model || "").trim();
    const el = $("modelMeta");

    // Reset
    while (el.firstChild) el.removeChild(el.firstChild);

    if (src === "secret" && !isEmpty(m)) {
      const badge = document.createElement("span");
      badge.className = "badge ok";
      badge.textContent = "Current";
      el.appendChild(badge);

      const t = document.createElement("span");
      t.textContent = ` Model (${src}): ${m}`;
      el.appendChild(t);
      return;
    }

    el.textContent = `Model (${src}): click Fetch models to load`;
  }

  function populateReuseKeySelect(providerType, currentProviderName) {
    const sel = $("reuseKeySelect");
    const meta = $("reuseKeyMeta");
    if (!sel || !meta) return;

    const pt = safeText(providerType || "").trim();
    const curName = safeText(currentProviderName || "").trim();
    const prior = safeText(sel.value || "").trim();

    const candidates = pt
      ? state.instances.filter((x) => safeText(x.providerType || "").trim().toLowerCase() === pt.toLowerCase())
      : [];

    // Preserve current selection if still valid; otherwise default to first candidate for convenience.
    let selected = prior;
    const exists = (v) => candidates.some((x) => safeText(x.name || "").trim().toLowerCase() === safeText(v).trim().toLowerCase());
    if (isEmpty(selected) || !exists(selected)) {
      selected = candidates.length > 0 ? safeText(candidates[0].name || "").trim() : "";
    }

    // Rebuild options
    try { sel.innerHTML = ""; } catch {}
    const o0 = document.createElement("option");
    o0.value = "";
    o0.textContent = "-- (enter new key) --";
    sel.appendChild(o0);

    for (const it of candidates) {
      const name = safeText(it.name || "").trim();
      if (!name) continue;
      const opt = document.createElement("option");
      opt.value = name;
      opt.textContent = name;
      sel.appendChild(opt);
    }

    sel.value = exists(selected) ? selected : "";

    if (!isEmpty(sel.value)) {
      meta.textContent = `Will copy API key from: ${sel.value} (server-side; key is not shown)`;
    } else if (!isEmpty(curName)) {
      meta.textContent = "Enter a new API key (or pick an existing instance above).";
    } else {
      meta.textContent = "";
    }
  }

  async function loadProviderDetails(providerName) {
    const name = safeText(providerName).trim();
    if (isEmpty(name)) return;

    try {
      const res = await fetch("/api/llm/provider/" + encodeURIComponent(name));
      const json = await res.json().catch(() => null);
      const p = json && json.provider ? json.provider : null;
      if (!p) return;

      const providerType = safeText(p.providerType || "").trim();
      if (!isEmpty(providerType)) {
        state.selectedProviderType = providerType;
      }
      // Keep reuse dropdown in sync with current provider type (best-effort).
      populateReuseKeySelect(state.selectedProviderType, name);

      const ep = safeText(p.endpoint || "");
      $("endpointInput").value = ep;
      $("endpointMeta").textContent = ep
        ? `Endpoint (${safeText(p.endpointSource || "unknown")}): ${ep}`
        : `Endpoint (${safeText(p.endpointSource || "unknown")}): (empty)`;

      state.endpointOriginal = ep;
      state.endpointSource = safeText(p.endpointSource || "");

      const model = safeText(p.model || "");
      const modelSource = safeText(p.modelSource || "");

      state.modelSource = modelSource;
      state.modelOriginal = modelSource === "secret" ? model : "";

      // UX: keep empty ("--") unless the model is explicitly configured in secrets.
      const sel = $("modelSelect");
      try { sel.innerHTML = ""; } catch {}
      const placeholder = document.createElement("option");
      placeholder.value = "";
      // When no model is configured, guide the user to fetch model list first.
      placeholder.textContent = "-- click Fetch models --";
      sel.appendChild(placeholder);

      if (modelSource === "secret" && !isEmpty(model)) {
        const cur = document.createElement("option");
        cur.value = model;
        cur.textContent = model;
        sel.appendChild(cur);
        sel.value = model;
      } else {
        sel.value = "";
      }

      renderModelMeta(modelSource, model);

      const configured = Boolean(p.apiKeyConfigured);
      $("disconnectBtn").disabled = !configured;
      $("testBtn").disabled = !configured;
      $("modelsBtn").disabled = !configured;

      // Sync API key display (masked by default). Do NOT override when user is typing a new key.
      await loadApiKeyMask(name);
    } catch {
      // best-effort
    }
  }

  async function loadApiKeyMask(providerName) {
    if (state.isNewKeyDraft) return;

    const name = safeText(providerName).trim();
    if (isEmpty(name)) return;

    try {
      const res = await fetch("/api/llm/api-key/" + encodeURIComponent(name));
      const json = await res.json().catch(() => null);
      if (!json || json.ok !== true) return;

      state.hasExistingKey = Boolean(json.configured);
      state.existingKeyMasked = safeText(json.masked || "");
      state.existingKeyFull = "";
      state.keyShown = false;

      // Default: show masked for configured key; keep input ready for draft otherwise.
      if (state.hasExistingKey && state.existingKeyMasked) {
        $("apiKeyInput").type = "text";
        $("apiKeyInput").value = state.existingKeyMasked;
        $("toggleKeyBtn").textContent = "Show";
      } else {
        // No stored key: keep as password input for new entry.
        if (!state.isNewKeyDraft) {
          $("apiKeyInput").type = "password";
          $("apiKeyInput").value = "";
          $("toggleKeyBtn").textContent = state.keyShown ? "Hide" : "Show";
        }
      }

      updateSubmitEnabled();
    } catch {
      // best-effort
    }
  }

  function openConnect(id) {
    state.selectedId = id;
    state.keyShown = false;
    state.isNewKeyDraft = false;
    state.hasExistingKey = false;
    state.existingKeyMasked = "";
    state.existingKeyFull = "";

    const type = findProviderType(id);
    const inst = type ? null : findInstance(id);
    const providerName = inst ? safeText(inst.name || id) : safeText(type ? (type.id || id) : id);

    state.selectedProviderType = safeText(inst ? (inst.providerType || "") : (type ? (type.id || "") : "")).trim();
    state.nameEdited = Boolean(inst); // editing an existing instance should not auto-rename on model change.

    const titleName = safeText(type ? (type.displayName || type.id) : (inst ? (inst.providerDisplayName || inst.providerType || inst.name) : id));

    $("connectTitle").textContent = "Connect " + titleName;
    $("connectSubtitle").textContent =
      "Enter your " + titleName + " API key to connect your account and use it in Aevatar apps.";

    $("providerNameInput").value = providerName;
    $("endpointInput").value = "";
    $("endpointMeta").textContent = "";
    $("modelSelect").innerHTML = "<option value=\"\">-- click Fetch models --</option>";
    $("modelMeta").textContent = "";
    state.modelOriginal = "";
    state.modelSource = "";
    $("apiKeyInput").value = "";
    $("apiKeyInput").type = "password";
    $("toggleKeyBtn").textContent = "Show";
    $("disconnectBtn").disabled = true;
    $("testBtn").disabled = true;
    $("modelsBtn").disabled = true;

    // Reuse key dropdown (best-effort)
    populateReuseKeySelect(state.selectedProviderType, inst ? safeText(inst.name || "") : "");

    setConnectMsg("");
    updateSubmitEnabled();
    setView("connect");

    void loadProviderDetails(providerName);
  }

  function openAdvanced() {
    state.advShown = false;
    $("advKeyInput").value = "";
    $("advValueInput").value = "";
    $("advValueInput").type = "password";
    $("advToggleBtn").textContent = "Show";
    setAdvMsg("");
    setView("advanced");
  }

  // ------------------------------------------------------------
  // Embeddings (global fallback) UI
  // ------------------------------------------------------------
  function setEmbMsg(text, kind) {
    const el = $("embMsg");
    el.textContent = safeText(text);
    el.className = "msg";
    if (kind === "ok") el.classList.add("ok");
    if (kind === "err") el.classList.add("err");
  }

  function resetEmbeddingsViewState() {
    state.embKeyShown = false;
    state.embIsNewKeyDraft = false;
    state.embHasExistingKey = false;
    state.embExistingKeyMasked = "";
    state.embExistingKeyFull = "";
  }

  function updateEmbeddingsButtons() {
    const enabled = Boolean($("embEnabledInput").checked);
    const providerType = safeText($("embProviderTypeInput").value).trim();
    const endpoint = safeText($("embEndpointInput").value).trim();
    const model = safeText($("embModelInput").value).trim();
    const key = safeText($("embApiKeyInput").value).trim();

    const canUseDraftKey = state.embIsNewKeyDraft && !isEmpty(key);
    const canUseStoredKey = state.embHasExistingKey;
    const hasKeySource = canUseDraftKey || canUseStoredKey;

    const requiredOk = !enabled || (!isEmpty(endpoint) && !isEmpty(model));
    $("embSaveBtn").disabled = !requiredOk || (enabled && !hasKeySource);

    $("embDeleteBtn").disabled = !(
      state.embHasExistingKey ||
      !isEmpty(providerType) ||
      !isEmpty(endpoint) ||
      !isEmpty(model) ||
      Boolean(state.embeddings && state.embeddings.configured)
    );
  }

  async function loadEmbeddingsConfigIntoForm() {
    try {
      const res = await fetch("/api/embeddings");
      const json = await res.json().catch(() => null);
      if (!res.ok || !json || json.ok !== true) throw new Error("HTTP " + res.status);

      const e = json.embeddings || {};
      state.embeddings = e;

      const enabled = (e.enabled === null || typeof e.enabled === "undefined") ? true : Boolean(e.enabled);
      $("embEnabledInput").checked = enabled;

      const providerType = safeText(e.providerType || "").trim() || "deepseek";
      const endpoint = safeText(e.endpoint || "").trim() || "https://dashscope.aliyuncs.com/compatible-mode/v1";
      const model = safeText(e.model || "").trim() || "text-embedding-v3";

      $("embProviderTypeInput").value = providerType;
      $("embEndpointInput").value = endpoint;
      $("embModelInput").value = model;

      const configured = Boolean(e.configured);
      state.embHasExistingKey = configured;
      state.embExistingKeyMasked = safeText(e.masked || "");
      state.embExistingKeyFull = "";
      state.embKeyShown = false;

      if (configured && state.embExistingKeyMasked) {
        $("embApiKeyInput").type = "text";
        $("embApiKeyInput").value = state.embExistingKeyMasked;
        $("embToggleKeyBtn").textContent = "Show";
      } else {
        $("embApiKeyInput").type = "password";
        $("embApiKeyInput").value = "";
        $("embToggleKeyBtn").textContent = "Show";
      }
    } catch (e) {
      $("embEnabledInput").checked = true;
      if (isEmpty($("embProviderTypeInput").value)) $("embProviderTypeInput").value = "deepseek";
      if (isEmpty($("embEndpointInput").value)) $("embEndpointInput").value = "https://dashscope.aliyuncs.com/compatible-mode/v1";
      if (isEmpty($("embModelInput").value)) $("embModelInput").value = "text-embedding-v3";
    } finally {
      updateEmbeddingsButtons();
    }
  }

  function openEmbeddings() {
    resetEmbeddingsViewState();
    setEmbMsg("");
    setView("embeddings");
    void loadEmbeddingsConfigIntoForm();
  }

  async function saveEmbeddings() {
    const enabled = Boolean($("embEnabledInput").checked);
    const providerType = safeText($("embProviderTypeInput").value).trim();
    const endpoint = safeText($("embEndpointInput").value).trim();
    const model = safeText($("embModelInput").value).trim();
    const apiKey = safeText($("embApiKeyInput").value).trim();

    $("embSaveBtn").disabled = true;
    try {
      const body = {
        enabled,
        providerType,
        endpoint,
        model,
        apiKey: state.embIsNewKeyDraft ? apiKey : null,
      };

      const res = await fetch("/api/embeddings", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(body),
      });
      const json = await res.json().catch(() => null);
      if (!res.ok || !json || json.ok !== true) throw new Error((json && json.error) ? json.error : ("HTTP " + res.status));

      setEmbMsg("Saved.", "ok");
      resetEmbeddingsViewState();
      await refreshProviders();
      await loadEmbeddingsConfigIntoForm();

      try {
        if (window.parent && window.parent !== window) {
          window.parent.postMessage({ type: "aevatar-secrets-ui:changed", action: "embeddings_save" }, "*");
        }
      } catch {
        // ignore
      }
    } catch (e) {
      setEmbMsg(e && e.message ? e.message : String(e), "err");
    } finally {
      updateEmbeddingsButtons();
    }
  }

  async function deleteEmbeddings() {
    if (!window.confirm("Delete embeddings configuration (including API key)?")) return;
    $("embDeleteBtn").disabled = true;
    try {
      const res = await fetch("/api/embeddings", { method: "DELETE" });
      const json = await res.json().catch(() => null);
      if (!res.ok || !json || json.ok !== true) throw new Error((json && json.error) ? json.error : ("HTTP " + res.status));

      setEmbMsg("Deleted.", "ok");
      resetEmbeddingsViewState();
      await refreshProviders();
      await loadEmbeddingsConfigIntoForm();

      try {
        if (window.parent && window.parent !== window) {
          window.parent.postMessage({ type: "aevatar-secrets-ui:changed", action: "embeddings_delete" }, "*");
        }
      } catch {
        // ignore
      }
    } catch (e) {
      setEmbMsg(e && e.message ? e.message : String(e), "err");
    } finally {
      updateEmbeddingsButtons();
    }
  }

  // ------------------------------------------------------------
  // SkillsMP UI
  // ------------------------------------------------------------
  function setSkillsMpMsg(text, kind) {
    const el = $("skillsMpMsg");
    el.textContent = safeText(text);
    el.className = "msg";
    if (kind === "ok") el.classList.add("ok");
    if (kind === "err") el.classList.add("err");
  }

  function resetSkillsMpViewState() {
    state.skillsMpKeyShown = false;
    state.skillsMpIsNewKeyDraft = false;
    state.skillsMpHasExistingKey = false;
    state.skillsMpExistingKeyMasked = "";
    state.skillsMpExistingKeyFull = "";
  }

  function updateSkillsMpButtons() {
    const key = safeText($("skillsMpApiKeyInput").value).trim();
    const canUseDraftKey = state.skillsMpIsNewKeyDraft && !isEmpty(key);
    const canUseStoredKey = state.skillsMpHasExistingKey;
    const hasKeySource = canUseDraftKey || canUseStoredKey;
    $("skillsMpSaveBtn").disabled = !hasKeySource;
    $("skillsMpDeleteBtn").disabled = !state.skillsMpHasExistingKey && isEmpty(safeText($("skillsMpBaseUrlInput").value).trim());
  }

  async function loadSkillsMpStatusIntoForm() {
    try {
      const res = await fetch("/api/skillsmp/status");
      const json = await res.json().catch(() => null);
      if (!res.ok || !json || json.ok !== true) throw new Error("HTTP " + res.status);

      const configured = Boolean(json.configured);
      const masked = safeText(json.masked || "");
      const baseUrl = safeText(json.baseUrl || "");

      state.skillsMpHasExistingKey = configured;
      state.skillsMpExistingKeyMasked = masked;
      state.skillsMpExistingKeyFull = "";
      state.skillsMpKeyShown = false;

      $("skillsMpBaseUrlInput").value = baseUrl || "https://skillsmp.com";

      if (configured && masked) {
        $("skillsMpApiKeyInput").type = "text";
        $("skillsMpApiKeyInput").value = masked;
        $("skillsMpToggleKeyBtn").textContent = "Show";
      } else {
        $("skillsMpApiKeyInput").type = "password";
        $("skillsMpApiKeyInput").value = "";
        $("skillsMpToggleKeyBtn").textContent = "Show";
      }
    } catch (e) {
      $("skillsMpBaseUrlInput").value = "https://skillsmp.com";
    } finally {
      updateSkillsMpButtons();
    }
  }

  function openSkillsMp() {
    resetSkillsMpViewState();
    setSkillsMpMsg("");
    setView("skillsmp");
    void loadSkillsMpStatusIntoForm();
  }

  async function saveSkillsMp() {
    const apiKey = safeText($("skillsMpApiKeyInput").value).trim();
    const baseUrl = safeText($("skillsMpBaseUrlInput").value).trim();

    $("skillsMpSaveBtn").disabled = true;
    try {
      const body = {
        apiKey: state.skillsMpIsNewKeyDraft ? apiKey : null,
        baseUrl: baseUrl || null,
      };

      const res = await fetch("/api/skillsmp", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(body),
      });
      const json = await res.json().catch(() => null);
      if (!res.ok || !json || json.ok !== true) throw new Error((json && json.error) ? json.error : ("HTTP " + res.status));

      setSkillsMpMsg("Saved.", "ok");
      resetSkillsMpViewState();
      await refreshProviders();
      await loadSkillsMpStatusIntoForm();

      try {
        if (window.parent && window.parent !== window) {
          window.parent.postMessage({ type: "aevatar-secrets-ui:changed", action: "skillsmp_save" }, "*");
        }
      } catch {
        // ignore
      }
    } catch (e) {
      setSkillsMpMsg(e && e.message ? e.message : String(e), "err");
    } finally {
      updateSkillsMpButtons();
    }
  }

  async function deleteSkillsMp() {
    if (!window.confirm("Delete SkillsMP configuration (including API key)?")) return;
    $("skillsMpDeleteBtn").disabled = true;
    try {
      const res = await fetch("/api/skillsmp", { method: "DELETE" });
      const json = await res.json().catch(() => null);
      if (!res.ok || !json || json.ok !== true) throw new Error((json && json.error) ? json.error : ("HTTP " + res.status));

      setSkillsMpMsg("Deleted.", "ok");
      resetSkillsMpViewState();
      await refreshProviders();
      await loadSkillsMpStatusIntoForm();

      try {
        if (window.parent && window.parent !== window) {
          window.parent.postMessage({ type: "aevatar-secrets-ui:changed", action: "skillsmp_delete" }, "*");
        }
      } catch {
        // ignore
      }
    } catch (e) {
      setSkillsMpMsg(e && e.message ? e.message : String(e), "err");
    } finally {
      updateSkillsMpButtons();
    }
  }

  async function saveEndpointOverride(providerName) {
    const name = safeText(providerName).trim();
    if (isEmpty(name)) return;

    const endpoint = safeText($("endpointInput").value).trim();
    const key = `LLMProviders:Providers:${name}:Endpoint`;

    if (isEmpty(endpoint)) {
      await fetch("/api/secrets/remove", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ key }),
      });
      return;
    }

    await fetch("/api/secrets/set", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ key, value: endpoint }),
    });
  }

  async function saveModelOverride(providerName) {
    const name = safeText(providerName).trim();
    if (isEmpty(name)) return;

    const model = safeText($("modelSelect").value).trim();
    const key = `LLMProviders:Providers:${name}:Model`;

    if (isEmpty(model)) {
      await fetch("/api/secrets/remove", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ key }),
      });
      return;
    }

    await fetch("/api/secrets/set", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ key, value: model }),
    });
  }

  async function submitApiKey() {
    const providerName = safeText($("providerNameInput").value).trim();
    const apiKey = safeText($("apiKeyInput").value).trim();
    const model = safeText($("modelSelect").value).trim();
    const providerType = safeText(state.selectedProviderType || "").trim();
    const endpoint = safeText($("endpointInput").value).trim();
    const reuseFrom = safeText($("reuseKeySelect") ? $("reuseKeySelect").value : "").trim();
    if (isEmpty(providerName)) return;

    if (isEmpty(model)) {
      setConnectMsg("Model is required. Click Fetch models and pick one.", "err");
      updateSubmitEnabled();
      return;
    }

    if (isEmpty(providerType)) {
      setConnectMsg("Provider type is missing. Please reopen the provider from the list.", "err");
      updateSubmitEnabled();
      return;
    }

    $("submitBtn").disabled = true;
    setConnectMsg("");

    try {
      // Key source rules:
      // - If user is typing a new key draft -> send ApiKey.
      // - Else, if reuseFrom is selected -> send CopyApiKeyFrom (even if apiKey input shows a masked existing key).
      const copyApiKeyFrom = !state.isNewKeyDraft && !isEmpty(reuseFrom) ? reuseFrom : "";
      const payload = {
        providerName,
        providerType,
        model,
        endpoint,
        apiKey: state.isNewKeyDraft ? apiKey : "",
        copyApiKeyFrom,
      };

      const res = await fetch("/api/llm/instance", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(payload),
      });

      const json = await res.json().catch(() => null);
      if (!res.ok || !json || json.ok !== true) {
        const err = json && json.error ? safeText(json.error) : ("HTTP " + res.status);
        throw new Error(err);
      }

      setConnectMsg("Saved. You can now Test / Fetch models.", "ok");
      $("apiKeyInput").value = "";
      state.isNewKeyDraft = false;

      try {
        if (window.parent && window.parent !== window) {
          window.parent.postMessage({ type: "aevatar-secrets-ui:saved", providerName, providerType, model }, "*");
        }
      } catch {
        // ignore
      }

      await refreshProviders();
      await loadProviderDetails(providerName);
    } catch (e) {
      setConnectMsg(e && e.message ? e.message : String(e), "err");
    } finally {
      updateSubmitEnabled();
    }
  }

  async function testConnection() {
    const providerName = safeText($("providerNameInput").value).trim();
    if (isEmpty(providerName)) return;

    $("testBtn").disabled = true;
    setConnectMsg("");

    try {
      const reuseFrom = safeText($("reuseKeySelect") ? $("reuseKeySelect").value : "").trim();
      const apiKey = safeText($("apiKeyInput").value).trim();
      const providerType = safeText(state.selectedProviderType || "").trim();
      const endpoint = safeText($("endpointInput").value).trim();

      const useReuse = !isEmpty(reuseFrom);
      const useDraft = state.isNewKeyDraft && !isEmpty(apiKey) && !useReuse;

      let res;
      if (useDraft) {
        if (isEmpty(providerType)) throw new Error("Provider type is missing. Please reopen the provider from the list.");
        res = await fetch("/api/llm/probe/test", {
          method: "POST",
          headers: { "content-type": "application/json" },
          body: JSON.stringify({ providerType, endpoint, apiKey }),
        });
      } else {
        const sourceName = useReuse ? reuseFrom : providerName;
        res = await fetch("/api/llm/test/" + encodeURIComponent(sourceName));
      }

      const json = await res.json().catch(() => null);
      if (!json) throw new Error("bad response");

      if (json.ok === true) {
        const ms = typeof json.latencyMs === "number" ? json.latencyMs : null;
        const cnt = typeof json.modelsCount === "number" ? json.modelsCount : null;
        setConnectMsg(`OK${ms != null ? ` · ${ms}ms` : ""}${cnt != null ? ` · models=${cnt}` : ""}`, "ok");
      } else {
        setConnectMsg(`Test failed: ${safeText(json.error || "unknown error")}`, "err");
      }
    } catch (e) {
      setConnectMsg(e && e.message ? e.message : String(e), "err");
    } finally {
      updateSubmitEnabled();
    }
  }

  async function fetchModels() {
    const providerName = safeText($("providerNameInput").value).trim();
    if (isEmpty(providerName)) return;

    const reuseFrom = safeText($("reuseKeySelect") ? $("reuseKeySelect").value : "").trim();
    const apiKey = safeText($("apiKeyInput").value).trim();
    const providerType = safeText(state.selectedProviderType || "").trim();
    const endpoint = safeText($("endpointInput").value).trim();

    const useReuse = !isEmpty(reuseFrom);
    const useDraft = state.isNewKeyDraft && !isEmpty(apiKey) && !useReuse;
    const sourceName = useReuse ? reuseFrom : providerName;

    $("modelsBtn").disabled = true;
    setConnectMsg("");

    try {
      let res;
      if (useDraft) {
        if (isEmpty(providerType)) throw new Error("Provider type is missing. Please reopen the provider from the list.");
        res = await fetch("/api/llm/probe/models?limit=200", {
          method: "POST",
          headers: { "content-type": "application/json" },
          body: JSON.stringify({ providerType, endpoint, apiKey }),
        });
      } else {
        res = await fetch("/api/llm/models/" + encodeURIComponent(sourceName) + "?limit=200");
      }

      const json = await res.json().catch(() => null);
      if (!json) throw new Error("bad response");

      if (json.ok === true) {
        const arr = Array.isArray(json.models) ? json.models : [];
        setConnectMsg(`Fetched models: ${arr.length}`, "ok");

        // Populate model dropdown (user selects explicitly).
        const sel = $("modelSelect");
        const current = safeText(sel.value).trim();
        const seen = new Set();
        try { sel.innerHTML = ""; } catch {}
        const placeholder = document.createElement("option");
        placeholder.value = "";
        placeholder.textContent = "-- select model --";
        sel.appendChild(placeholder);

        if (!isEmpty(current)) {
          const cur = document.createElement("option");
          cur.value = current;
          cur.textContent = current;
          sel.appendChild(cur);
          seen.add(current.toLowerCase());
        }

        for (const m of arr) {
          const v = safeText(m).trim();
          if (!v) continue;
          const key = v.toLowerCase();
          if (seen.has(key)) continue;
          seen.add(key);
          const opt = document.createElement("option");
          opt.value = v;
          opt.textContent = v;
          sel.appendChild(opt);
        }

        sel.value = current;

        updateSubmitEnabled();
      } else {
        setConnectMsg(`Fetch models failed: ${safeText(json.error || "unknown error")}`, "err");
      }
    } catch (e) {
      setConnectMsg(e && e.message ? e.message : String(e), "err");
    } finally {
      $("modelsBtn").disabled = false;
      updateSubmitEnabled();
    }
  }

  async function disconnectApiKey() {
    const providerName = safeText($("providerNameInput").value).trim();
    if (isEmpty(providerName)) return;

    $("disconnectBtn").disabled = true;
    setConnectMsg("");

    try {
      const res = await fetch("/api/trash/api-key/" + encodeURIComponent(providerName), { method: "POST" });
      const json = await res.json().catch(() => null);
      if (!res.ok || !json || json.ok !== true) {
        const err = json && json.error ? safeText(json.error) : ("HTTP " + res.status);
        throw new Error(err);
      }

      setConnectMsg("Moved to Trash. Delete permanently from the Trash section on the home page.", "ok");
      await refreshProviders();
      await loadProviderDetails(providerName);

      try {
        if (window.parent && window.parent !== window) {
          window.parent.postMessage({ type: "aevatar-secrets-ui:changed", action: "disconnect", providerName }, "*");
        }
      } catch {
        // ignore
      }
    } catch (e) {
      setConnectMsg(e && e.message ? e.message : String(e), "err");
    } finally {
      updateSubmitEnabled();
    }
  }

  async function saveRaw() {
    const key = safeText($("advKeyInput").value).trim();
    const value = safeText($("advValueInput").value).trim();
    if (isEmpty(key) || isEmpty(value)) return;

    $("advSaveBtn").disabled = true;
    setAdvMsg("");

    try {
      const res = await fetch("/api/secrets/set", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ key, value }),
      });
      const text = await res.text();
      if (!res.ok) throw new Error("HTTP " + res.status + (text ? (": " + text) : ""));

      setAdvMsg("Saved.", "ok");
      $("advValueInput").value = "";
      await refreshProviders();
    } catch (e) {
      setAdvMsg(e && e.message ? e.message : String(e), "err");
    } finally {
      $("advSaveBtn").disabled = false;
    }
  }

  async function removeRaw() {
    const key = safeText($("advKeyInput").value).trim();
    if (isEmpty(key)) return;

    $("advRemoveBtn").disabled = true;
    setAdvMsg("");

    try {
      const res = await fetch("/api/secrets/remove", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ key }),
      });
      const text = await res.text();
      if (!res.ok) throw new Error("HTTP " + res.status + (text ? (": " + text) : ""));

      setAdvMsg("Removed.", "ok");
      await refreshProviders();
    } catch (e) {
      setAdvMsg(e && e.message ? e.message : String(e), "err");
    } finally {
      $("advRemoveBtn").disabled = false;
    }
  }

  function updateAdvancedButtons() {
    const k = safeText($("advKeyInput").value).trim();
    const v = safeText($("advValueInput").value).trim();
    $("advSaveBtn").disabled = isEmpty(k) || isEmpty(v);
    $("advRemoveBtn").disabled = isEmpty(k);
  }

  function wire() {
    $("closeBtn").onclick = () => {
      try {
        if (window.parent && window.parent !== window) {
          window.parent.postMessage({ type: "aevatar-secrets-ui:close" }, "*");
          return;
        }
      } catch {
        // ignore
      }
      // standalone fallback
      window.location.href = "/health";
    };
    $("connectCloseBtn").onclick = () => setView("list");
    $("advCloseBtn").onclick = () => setView("list");
    $("embCloseBtn").onclick = () => setView("list");
    $("skillsMpCloseBtn").onclick = () => setView("list");
    $("backBtn").onclick = () => setView("list");
    $("advBackBtn").onclick = () => setView("list");
    $("embBackBtn").onclick = () => setView("list");
    $("skillsMpBackBtn").onclick = () => setView("list");

    $("searchInput").addEventListener("input", debounce(() => {
      state.search = safeText($("searchInput").value);
      renderList();
    }, 50));

    $("defaultSelect").addEventListener("change", debounce(updateDefaultButtons, 30));
    $("defaultSetBtn").onclick = () => setDefaultProvider();

    $("embeddingsItem").onclick = () => openEmbeddings();
    $("skillsMpItem").onclick = () => openSkillsMp();

    $("advancedItem").onclick = () => openAdvanced();

    $("toggleKeyBtn").onclick = () => {
      // Existing key draft: toggle reveal via API; draft mode toggles input type.
      if (!state.hasExistingKey || state.isNewKeyDraft) {
        state.keyShown = !state.keyShown;
        $("apiKeyInput").type = state.keyShown ? "text" : "password";
        $("toggleKeyBtn").textContent = state.keyShown ? "Hide" : "Show";
        return;
      }

      // Existing key mode:
      if (!state.keyShown) {
        (async () => {
          try {
            const name = safeText($("providerNameInput").value).trim();
            const res = await fetch("/api/llm/api-key/" + encodeURIComponent(name) + "?reveal=true");
            const json = await res.json().catch(() => null);
            if (!json || json.ok !== true || !json.value) {
              setConnectMsg("Failed to reveal key (not configured).", "err");
              return;
            }
            state.keyShown = true;
            state.existingKeyFull = safeText(json.value || "");
            $("apiKeyInput").type = "text";
            $("apiKeyInput").value = state.existingKeyFull;
            $("toggleKeyBtn").textContent = "Hide";
            updateSubmitEnabled();
          } catch (e) {
            setConnectMsg(e && e.message ? e.message : String(e), "err");
          }
        })();
      } else {
        state.keyShown = false;
        state.existingKeyFull = "";
        $("apiKeyInput").type = "text";
        $("apiKeyInput").value = state.existingKeyMasked || "";
        $("toggleKeyBtn").textContent = "Show";
        updateSubmitEnabled();
      }
    };

    $("providerNameInput").addEventListener("input", debounce(() => {
      state.nameEdited = true;
      updateSubmitEnabled();
    }, 60));
    $("endpointInput").addEventListener("input", debounce(updateSubmitEnabled, 60));
    $("modelSelect").addEventListener("change", debounce(() => {
      // Auto-suggest instance name: "<provider>-<model>" (unless user already edited the name).
      const m = safeText($("modelSelect").value).trim();
      if (!state.nameEdited && !isEmpty(state.selectedProviderType) && !isEmpty(m)) {
        const suggested = safeText(state.selectedProviderType).trim() + "-" + sanitizeModelForInstanceName(m);
        if (!isEmpty(suggested)) {
          $("providerNameInput").value = suggested;
        }
      }
      updateSubmitEnabled();
    }, 60));
    $("reuseKeySelect").addEventListener("change", debounce(() => {
      const v = safeText($("reuseKeySelect").value).trim();
      $("reuseKeyMeta").textContent = v
        ? `Will copy API key from: ${v} (server-side; key is not shown)`
        : "Enter a new API key (or pick an existing instance above).";

      if (!isEmpty(v)) {
        // Reuse mode: clear draft API key input to avoid confusion.
        state.isNewKeyDraft = false;
        state.keyShown = false;
        $("apiKeyInput").type = "password";
        $("apiKeyInput").value = "";
        $("toggleKeyBtn").textContent = "Show";
      }
      updateSubmitEnabled();
    }, 60));
    $("apiKeyInput").addEventListener("focus", () => {
      // Convenience: when displaying stored key, select all so paste replaces it cleanly.
      if (!state.isNewKeyDraft && state.hasExistingKey) {
        try { $("apiKeyInput").select(); } catch {}
      }
    });
    $("apiKeyInput").addEventListener("input", debounce(() => {
      const cur = safeText($("apiKeyInput").value).trim();

      if (!state.isNewKeyDraft) {
        const equalsMasked = state.hasExistingKey && cur === safeText(state.existingKeyMasked).trim();
        const equalsFull = state.hasExistingKey && state.existingKeyFull && cur === safeText(state.existingKeyFull).trim();
        if (!equalsMasked && !equalsFull && !isEmpty(cur)) {
          // User started typing a new key: switch to draft mode (password by default).
          state.isNewKeyDraft = true;
          state.keyShown = false;
          $("apiKeyInput").type = "password";
          $("toggleKeyBtn").textContent = "Show";

          // Draft key overrides reuse mode.
          try { $("reuseKeySelect").value = ""; } catch {}
          try { $("reuseKeyMeta").textContent = "Enter a new API key (or pick an existing instance above)."; } catch {}

          // Clear fetched model list (key changed) but keep current selection if any.
          const sel = $("modelSelect");
          const current = safeText(sel.value).trim();
          try { sel.innerHTML = ""; } catch {}
          const placeholder = document.createElement("option");
          placeholder.value = "";
        placeholder.textContent = "-- click Fetch models --";
          sel.appendChild(placeholder);
          if (!isEmpty(current)) {
            const curOpt = document.createElement("option");
            curOpt.value = current;
            curOpt.textContent = current;
            sel.appendChild(curOpt);
            sel.value = current;
          } else {
            sel.value = "";
          }
        }
      }

      updateSubmitEnabled();
    }, 60));
    $("submitBtn").onclick = () => submitApiKey();
    $("testBtn").onclick = () => testConnection();
    $("modelsBtn").onclick = () => fetchModels();
    $("disconnectBtn").onclick = () => disconnectApiKey();

    $("advToggleBtn").onclick = () => {
      state.advShown = !state.advShown;
      $("advValueInput").type = state.advShown ? "text" : "password";
      $("advToggleBtn").textContent = state.advShown ? "Hide" : "Show";
    };
    $("advKeyInput").addEventListener("input", debounce(updateAdvancedButtons, 60));
    $("advValueInput").addEventListener("input", debounce(updateAdvancedButtons, 60));
    $("advSaveBtn").onclick = () => saveRaw();
    $("advRemoveBtn").onclick = () => removeRaw();

    // Embeddings view
    $("embToggleKeyBtn").onclick = () => {
      if (!state.embHasExistingKey || state.embIsNewKeyDraft) {
        state.embKeyShown = !state.embKeyShown;
        $("embApiKeyInput").type = state.embKeyShown ? "text" : "password";
        $("embToggleKeyBtn").textContent = state.embKeyShown ? "Hide" : "Show";
        return;
      }

      if (!state.embKeyShown) {
        (async () => {
          try {
            const res = await fetch("/api/embeddings/api-key?reveal=true");
            const json = await res.json().catch(() => null);
            if (!json || json.ok !== true || !json.value) {
              setEmbMsg("Failed to reveal key (not configured).", "err");
              return;
            }
            state.embKeyShown = true;
            state.embExistingKeyFull = safeText(json.value || "");
            $("embApiKeyInput").type = "text";
            $("embApiKeyInput").value = state.embExistingKeyFull;
            $("embToggleKeyBtn").textContent = "Hide";
            updateEmbeddingsButtons();
          } catch (e) {
            setEmbMsg(e && e.message ? e.message : String(e), "err");
          }
        })();
      } else {
        state.embKeyShown = false;
        state.embExistingKeyFull = "";
        $("embApiKeyInput").type = "text";
        $("embApiKeyInput").value = state.embExistingKeyMasked || "";
        $("embToggleKeyBtn").textContent = "Show";
        updateEmbeddingsButtons();
      }
    };

    $("embEnabledInput").addEventListener("change", debounce(updateEmbeddingsButtons, 30));
    $("embProviderTypeInput").addEventListener("input", debounce(updateEmbeddingsButtons, 60));
    $("embEndpointInput").addEventListener("input", debounce(updateEmbeddingsButtons, 60));
    $("embModelInput").addEventListener("input", debounce(updateEmbeddingsButtons, 60));
    $("embApiKeyInput").addEventListener("focus", () => {
      if (!state.embIsNewKeyDraft && state.embHasExistingKey) {
        try { $("embApiKeyInput").select(); } catch {}
      }
    });
    $("embApiKeyInput").addEventListener("input", debounce(() => {
      const cur = safeText($("embApiKeyInput").value).trim();
      if (!state.embIsNewKeyDraft) {
        const equalsMasked = state.embHasExistingKey && cur === safeText(state.embExistingKeyMasked).trim();
        const equalsFull = state.embHasExistingKey && state.embExistingKeyFull && cur === safeText(state.embExistingKeyFull).trim();
        if (!equalsMasked && !equalsFull && !isEmpty(cur)) {
          state.embIsNewKeyDraft = true;
          state.embKeyShown = false;
          $("embApiKeyInput").type = "password";
          $("embToggleKeyBtn").textContent = "Show";
        }
      }
      updateEmbeddingsButtons();
    }, 60));

    $("embSaveBtn").onclick = () => saveEmbeddings();
    $("embDeleteBtn").onclick = () => deleteEmbeddings();

    // SkillsMP view
    $("skillsMpToggleKeyBtn").onclick = () => {
      if (!state.skillsMpHasExistingKey || state.skillsMpIsNewKeyDraft) {
        state.skillsMpKeyShown = !state.skillsMpKeyShown;
        $("skillsMpApiKeyInput").type = state.skillsMpKeyShown ? "text" : "password";
        $("skillsMpToggleKeyBtn").textContent = state.skillsMpKeyShown ? "Hide" : "Show";
        return;
      }

      if (!state.skillsMpKeyShown) {
        (async () => {
          try {
            const res = await fetch("/api/skillsmp/api-key?reveal=true");
            const json = await res.json().catch(() => null);
            if (!json || json.ok !== true || !json.value) {
              setSkillsMpMsg("Failed to reveal key (not configured).", "err");
              return;
            }
            state.skillsMpKeyShown = true;
            state.skillsMpExistingKeyFull = safeText(json.value || "");
            $("skillsMpApiKeyInput").type = "text";
            $("skillsMpApiKeyInput").value = state.skillsMpExistingKeyFull;
            $("skillsMpToggleKeyBtn").textContent = "Hide";
            updateSkillsMpButtons();
          } catch (e) {
            setSkillsMpMsg(e && e.message ? e.message : String(e), "err");
          }
        })();
      } else {
        state.skillsMpKeyShown = false;
        state.skillsMpExistingKeyFull = "";
        $("skillsMpApiKeyInput").type = "text";
        $("skillsMpApiKeyInput").value = state.skillsMpExistingKeyMasked || "";
        $("skillsMpToggleKeyBtn").textContent = "Show";
        updateSkillsMpButtons();
      }
    };

    $("skillsMpBaseUrlInput").addEventListener("input", debounce(updateSkillsMpButtons, 60));
    $("skillsMpApiKeyInput").addEventListener("focus", () => {
      if (!state.skillsMpIsNewKeyDraft && state.skillsMpHasExistingKey) {
        try { $("skillsMpApiKeyInput").select(); } catch {}
      }
    });
    $("skillsMpApiKeyInput").addEventListener("input", debounce(() => {
      const cur = safeText($("skillsMpApiKeyInput").value).trim();
      if (!state.skillsMpIsNewKeyDraft) {
        const equalsMasked = state.skillsMpHasExistingKey && cur === safeText(state.skillsMpExistingKeyMasked).trim();
        const equalsFull = state.skillsMpHasExistingKey && state.skillsMpExistingKeyFull && cur === safeText(state.skillsMpExistingKeyFull).trim();
        if (!equalsMasked && !equalsFull && !isEmpty(cur)) {
          state.skillsMpIsNewKeyDraft = true;
          state.skillsMpKeyShown = false;
          $("skillsMpApiKeyInput").type = "password";
          $("skillsMpToggleKeyBtn").textContent = "Show";
        }
      }
      updateSkillsMpButtons();
    }, 60));

    $("skillsMpSaveBtn").onclick = () => saveSkillsMp();
    $("skillsMpDeleteBtn").onclick = () => deleteSkillsMp();
  }

  async function init() {
    wire();
    setView("list");
    await refreshProviders();
  }

  // Export a tiny "SDK-like" handle for embedding/debugging.
  // Other apps can serve these static files and call:
  //   window.AevatarSecretsUi.init()
  window.AevatarSecretsUi = { init };

  // Auto-init for the standalone page.
  void init();
})();


