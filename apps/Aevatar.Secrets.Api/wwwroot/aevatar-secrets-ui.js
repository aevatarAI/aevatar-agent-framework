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

  async function refreshProviders() {
    try {
      const [pRes, iRes] = await Promise.all([
        fetch("/api/llm/providers"),
        fetch("/api/llm/instances"),
      ]);
      if (!pRes.ok) throw new Error("HTTP " + pRes.status);
      if (!iRes.ok) throw new Error("HTTP " + iRes.status);

      const pJson = await pRes.json().catch(() => null);
      const iJson = await iRes.json().catch(() => null);

      state.providers = Array.isArray(pJson && pJson.providers) ? pJson.providers : [];
      state.instances = Array.isArray(iJson && iJson.instances) ? iJson.instances : [];
    } catch (e) {
      console.error(e);
      state.providers = [];
      state.instances = [];
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

  function renderList() {
    const q = safeText(state.search).trim().toLowerCase();
    const providers = q ? state.providers.filter((p) => matches(p, q)) : state.providers.slice();
    const instances = q ? state.instances.filter((it) => matchesInstance(it, q)) : state.instances.slice();

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

    $("secConfigured").classList.toggle("hidden", instances.length === 0);

    renderInstanceSection("listConfigured", instances);
    renderProviderSection("listPopular", popular);
    renderProviderSection("listOther", other);
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

    // Fetch models needs a configured key source (either current instance or reuseFrom).
    const canFetchModels = isConfiguredInstance || !isEmpty(reuseFrom);
    $("modelsBtn").disabled = !canFetchModels;
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

    el.textContent = `Model (${src}): --`;
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
      placeholder.textContent = "--";
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
    $("modelSelect").innerHTML = "<option value=\"\">--</option>";
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
      const copyApiKeyFrom = !state.isNewKeyDraft && isEmpty(apiKey) && !isEmpty(reuseFrom) ? reuseFrom : "";
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
      const res = await fetch("/api/llm/test/" + encodeURIComponent(providerName));
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
      $("modelsBtn").disabled = false;
      updateSubmitEnabled();
    }
  }

  async function fetchModels() {
    const providerName = safeText($("providerNameInput").value).trim();
    if (isEmpty(providerName)) return;

    const reuseFrom = safeText($("reuseKeySelect") ? $("reuseKeySelect").value : "").trim();
    const sourceName = !isEmpty(reuseFrom) ? reuseFrom : providerName;

    $("modelsBtn").disabled = true;
    setConnectMsg("");

    try {
      const res = await fetch("/api/llm/models/" + encodeURIComponent(sourceName) + "?limit=200");
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
      const res = await fetch("/api/llm/api-key/" + encodeURIComponent(providerName), { method: "DELETE" });
      const text = await res.text();
      if (!res.ok) throw new Error("HTTP " + res.status + (text ? (": " + text) : ""));

      setConnectMsg("Disconnected.", "ok");
      await refreshProviders();
      await loadProviderDetails(providerName);
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
    $("backBtn").onclick = () => setView("list");
    $("advBackBtn").onclick = () => setView("list");

    $("searchInput").addEventListener("input", debounce(() => {
      state.search = safeText($("searchInput").value);
      renderList();
    }, 50));

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
          placeholder.textContent = "--";
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


