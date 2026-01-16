(() => {
  const $ = (id) => document.getElementById(id);
  const chat = $("chat");
  const input = $("input");
  const send = $("send");
  const sessionLabel = $("sessionLabel");
  const runLabel = $("runLabel");

  let sessionId = null;
  let activeRunId = "";
  let currentAssistantByRun = new Map(); // runId -> bubble element

  function addRow(kind, text, opts = {}) {
    const row = document.createElement("div");
    row.className = `row ${kind}`;

    const bubble = document.createElement("div");
    bubble.className = "bubble";
    if (opts.html) bubble.innerHTML = opts.html;
    else bubble.textContent = text;

    row.appendChild(bubble);
    chat.appendChild(row);
    chat.scrollTop = chat.scrollHeight;
    return bubble;
  }

  function setRunLabel(id) {
    activeRunId = id || "";
    runLabel.textContent = `activeRun: ${activeRunId || "-"}`;
  }

  async function newSession() {
    const res = await fetch("/api/sessions/new");
    const data = await res.json();
    sessionId = data.sessionId;
    sessionLabel.textContent = `session: ${sessionId}`;
    setRunLabel("");
    connectSse();
  }

  function connectSse() {
    const es = new EventSource(`/api/sessions/${sessionId}/events`);
    es.addEventListener("message", (ev) => {
      const msg = JSON.parse(ev.data);
      handleServerEvent(msg);
    });
    es.onerror = () => {
      addRow("system", "SSE disconnected. Refresh if needed.");
    };
  }

  function handleServerEvent(e) {
    switch (e.type) {
      case "system":
        addRow("system", e.message || "");
        break;
      case "hello":
        if (e.activeRunId) setRunLabel(e.activeRunId);
        break;
      case "run_started":
        setRunLabel(e.runId);
        addRow("system", `run started: ${e.runId}`);
        break;
      case "run_interrupted":
        addRow("system", `run interrupted: ${e.oldRunId} → ${e.newRunId}`);
        break;
      case "user_message":
        addRow("user", e.content);
        break;
      case "assistant_delta": {
        const runId = e.runId || "";
        let bubble = currentAssistantByRun.get(runId);
        if (!bubble) {
          bubble = addRow("assistant", "");
          currentAssistantByRun.set(runId, bubble);
        }
        bubble.textContent += e.delta || "";
        chat.scrollTop = chat.scrollHeight;
        break;
      }
      case "run_finished": {
        const ok = !!e.ok;
        const canceled = !!e.canceled;
        const tag = ok
          ? `<span class="tag ok">ok</span>`
          : canceled
            ? `<span class="tag danger">canceled</span>`
            : `<span class="tag danger">error</span>`;
        const detail = e.error ? ` ${e.error}` : "";
        addRow("system", "", { html: `${tag} run finished: ${e.runId}${detail}` });
        break;
      }
      default:
        addRow("system", `unknown event: ${JSON.stringify(e)}`);
        break;
    }
  }

  async function sendMessage() {
    const text = (input.value || "").trim();
    if (!text) return;
    input.value = "";

    await fetch(`/api/sessions/${sessionId}/input`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ message: text }),
    });
  }

  send.addEventListener("click", sendMessage);
  input.addEventListener("keydown", (e) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      sendMessage();
    }
  });

  addRow("system", "Loading session...");
  newSession().catch((err) => {
    addRow("system", `Failed to start session: ${err?.message || err}`);
  });
})();


