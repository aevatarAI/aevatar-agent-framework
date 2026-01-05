// ============================================================
//  C-UoT Creative Reasoning Console - Frontend
// ============================================================

let sampleProblems = [];
let currentRunId = null;
let eventSource = null;

// Icons for categories
const categoryIcons = {
    'Engineering': '🏗️',
    'Business': '💼',
    'Product': '📱',
    'Workplace': '🏢',
    'Environment': '🌱'
};

// ============================================================
//  Theme Management
// ============================================================

function initTheme() {
    const saved = localStorage.getItem('uot-theme');
    const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
    const theme = saved || (prefersDark ? 'dark' : 'light');
    setTheme(theme);
}

function setTheme(theme) {
    document.documentElement.setAttribute('data-theme', theme);
    localStorage.setItem('uot-theme', theme);
    updateThemeUI(theme);
}

function toggleTheme() {
    const current = document.documentElement.getAttribute('data-theme') || 'dark';
    const next = current === 'dark' ? 'light' : 'dark';
    setTheme(next);
}

function updateThemeUI(theme) {
    const icon = document.getElementById('theme-icon');
    const label = document.getElementById('theme-label');
    if (icon && label) {
        if (theme === 'light') {
            icon.textContent = '☀️';
            label.textContent = 'LIGHT';
        } else {
            icon.textContent = '🌙';
            label.textContent = 'DARK';
        }
    }
}

// ============================================================
//  Initialization
// ============================================================

async function init() {
    initTheme();
    await loadSampleProblems();
}

async function loadSampleProblems() {
    try {
        const response = await fetch('/api/problems');
        sampleProblems = await response.json();
        renderSampleProblems();
    } catch (err) {
        console.error('Failed to load sample problems:', err);
    }
}

function renderSampleProblems() {
    const container = document.getElementById('sample-list');
    container.innerHTML = sampleProblems.map(p => `
        <div class="sample-item" onclick="selectSample('${p.id}')">
            <span class="sample-icon">${categoryIcons[p.category] || '📝'}</span>
            <span class="sample-title">${p.title}</span>
            <span class="sample-category">${p.category}</span>
        </div>
    `).join('');
}

function selectSample(id) {
    const problem = sampleProblems.find(p => p.id === id);
    if (!problem) return;

    // Update selection UI
    document.querySelectorAll('.sample-item').forEach(el => el.classList.remove('selected'));
    event.currentTarget.classList.add('selected');

    // Fill form
    document.getElementById('problem-input').value = problem.problem;
    document.getElementById('domain-hint').value = problem.domainHint;
}

// ============================================================
//  Solve
// ============================================================

async function startSolve() {
    const problem = document.getElementById('problem-input').value.trim();
    if (!problem) {
        alert('Please enter a problem description');
        return;
    }

    const request = {
        problem,
        domainHint: document.getElementById('domain-hint').value || null,
        maxAnalogies: parseInt(document.getElementById('opt-analogies').value) || 5,
        maxCandidates: parseInt(document.getElementById('opt-candidates').value) || 10,
        feasibilityThreshold: parseFloat(document.getElementById('opt-feasibility').value) || 0.6,
        utilityWeight: parseFloat(document.getElementById('opt-utility').value) || 0.5,
        noveltyWeight: parseFloat(document.getElementById('opt-novelty').value) || 0.5
    };

    // Disable button
    const btn = document.getElementById('btn-solve');
    btn.disabled = true;
    btn.innerHTML = '<span class="btn-icon">⏳</span> Processing...';

    // Show progress section
    document.getElementById('empty-state').style.display = 'none';
    document.getElementById('progress-section').style.display = 'block';
    document.getElementById('results-section').style.display = 'none';

    // Reset stats
    resetProgress();

    try {
        const response = await fetch('/api/solve', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(request)
        });

        const result = await response.json();
        currentRunId = result.runId;

        // Start SSE
        startEventStream(currentRunId);

    } catch (err) {
        console.error('Failed to start solve:', err);
        alert('Failed to start creative reasoning: ' + err.message);
        resetButton();
    }
}

function resetProgress() {
    document.getElementById('progress-bar').style.width = '0%';
    document.getElementById('current-phase').textContent = 'Starting';
    document.getElementById('stat-analogies').textContent = '0';
    document.getElementById('stat-thoughts').textContent = '0';
    document.getElementById('stat-candidates').textContent = '0';
    document.getElementById('stat-passed').textContent = '0';
    document.getElementById('timeline').innerHTML = '';
    document.getElementById('status-badge').textContent = 'Running';
    document.getElementById('status-badge').className = 'status-badge';
}

function resetButton() {
    const btn = document.getElementById('btn-solve');
    btn.disabled = false;
    btn.innerHTML = '<span class="btn-icon">⚡</span> Start Creative Reasoning';
}

// ============================================================
//  SSE Event Stream
// ============================================================

function startEventStream(runId) {
    if (eventSource) {
        eventSource.close();
    }

    eventSource = new EventSource(`/api/runs/${runId}/events`);

    eventSource.onmessage = function(event) {
        const data = JSON.parse(event.data);
        handleEvent(data);
    };

    eventSource.onerror = function(err) {
        console.error('SSE error:', err);
        eventSource.close();
        loadResult(runId);
    };
}

function handleEvent(evt) {
    console.log('Event:', evt);

    // Update progress
    if (evt.progress != null) {
        document.getElementById('progress-bar').style.width = (evt.progress * 100) + '%';
    }

    if (evt.phase) {
        document.getElementById('current-phase').textContent = evt.phase;
    }

    // Update stats
    if (evt.analogiesFound != null) {
        document.getElementById('stat-analogies').textContent = evt.analogiesFound;
    }
    if (evt.thoughtsExtracted != null) {
        document.getElementById('stat-thoughts').textContent = evt.thoughtsExtracted;
    }
    if (evt.candidatesGenerated != null) {
        document.getElementById('stat-candidates').textContent = evt.candidatesGenerated;
    }
    if (evt.candidatesPassed != null) {
        document.getElementById('stat-passed').textContent = evt.candidatesPassed;
    }

    // Add to timeline
    if (evt.message) {
        addTimelineEntry(evt);
    }

    // Handle completion
    if (evt.type === 'result') {
        document.getElementById('status-badge').textContent = 'Completed';
        document.getElementById('status-badge').classList.add('completed');
        loadResult(evt.runId);
    } else if (evt.type === 'error') {
        document.getElementById('status-badge').textContent = 'Failed';
        document.getElementById('status-badge').classList.add('failed');
        addTimelineEntry({ phase: 'Error', message: evt.message, timestamp: new Date().toISOString() });
        resetButton();
    }
}

function addTimelineEntry(evt) {
    const timeline = document.getElementById('timeline');
    const time = new Date(evt.timestamp).toLocaleTimeString();
    
    const li = document.createElement('li');
    li.className = 'timeline-item';
    li.innerHTML = `
        <span class="timeline-time">${time}</span>
        <span class="timeline-phase">${evt.phase || '-'}</span>
        <span class="timeline-message">${evt.message}</span>
    `;
    
    timeline.insertBefore(li, timeline.firstChild);
}

// ============================================================
//  Load Result
// ============================================================

async function loadResult(runId) {
    try {
        const response = await fetch(`/api/runs/${runId}/result`);
        if (!response.ok) {
            console.error('Result not ready');
            return;
        }

        const result = await response.json();
        renderResult(result);
        resetButton();

    } catch (err) {
        console.error('Failed to load result:', err);
    }
}

function renderResult(result) {
    // Show results section
    document.getElementById('results-section').style.display = 'block';

    // Meta info
    document.getElementById('result-count').textContent = `${result.allCandidates.length} solutions`;
    document.getElementById('result-llm-calls').textContent = `${result.trace.totalLLMCalls} LLM calls`;

    // Best solution
    if (result.bestSolution) {
        const best = result.bestSolution;
        document.getElementById('best-feasibility').textContent = `F: ${(best.score?.feasibility || 0).toFixed(2)}`;
        document.getElementById('best-utility').textContent = `U: ${(best.score?.utility || 0).toFixed(2)}`;
        document.getElementById('best-novelty').textContent = `N: ${(best.score?.novelty || 0).toFixed(2)}`;
        document.getElementById('best-composite').textContent = `Score: ${(best.score?.composite || 0).toFixed(2)}`;
        document.getElementById('best-content').innerHTML = marked.parse(best.content || '');
    }

    // All candidates
    const candidatesContainer = document.getElementById('candidates-list');
    candidatesContainer.innerHTML = result.allCandidates.map((c, i) => `
        <div class="candidate-item" onclick="toggleCandidate(this)">
            <div class="candidate-header">
                <span class="candidate-rank">#${i + 1}</span>
                <div class="score-pills">
                    <span class="score-pill feasibility">F: ${(c.score?.feasibility || 0).toFixed(2)}</span>
                    <span class="score-pill utility">U: ${(c.score?.utility || 0).toFixed(2)}</span>
                    <span class="score-pill novelty">N: ${(c.score?.novelty || 0).toFixed(2)}</span>
                    <span class="score-pill composite">${(c.score?.composite || 0).toFixed(2)}</span>
                </div>
            </div>
            <div class="candidate-content">${marked.parse(c.content || '')}</div>
        </div>
    `).join('');

    // Unified ExecutionTrace (Protobuf JSON)
    document.getElementById('trace-info').innerHTML = `<p>Loading unified trace...</p>`;
    loadExecutionTrace(result.runId);
}

function toggleCandidate(el) {
    el.classList.toggle('expanded');
}

function formatDuration(duration) {
    // duration is a .NET TimeSpan string like "00:01:23.456"
    if (!duration) return '-';
    const parts = duration.split(':');
    const hours = parseInt(parts[0]);
    const minutes = parseInt(parts[1]);
    const seconds = parseFloat(parts[2]);
    
    if (hours > 0) return `${hours}h ${minutes}m`;
    if (minutes > 0) return `${minutes}m ${Math.round(seconds)}s`;
    return `${seconds.toFixed(1)}s`;
}

// ============================================================
//  Unified ExecutionTrace Viewer
// ============================================================

async function loadExecutionTrace(runId) {
    const container = document.getElementById('trace-info');
    if (!container) return;

    try {
        const res = await fetch(`/api/runs/${runId}/trace`);
        if (!res.ok) {
            container.innerHTML = `<p>No unified trace available.</p>`;
            return;
        }

        const trace = await res.json();
        container.innerHTML = renderExecutionTrace(trace);
    } catch (err) {
        console.error('Failed to load execution trace:', err);
        container.innerHTML = `<p>Failed to load trace: ${escapeHtml(err.message)}</p>`;
    }
}

function renderExecutionTrace(trace) {
    if (!trace || !trace.root) {
        return `<p>Trace is empty.</p>`;
    }

    const status = prettyEnum(trace.status);
    const kind = prettyEnum(trace.kind);
    const durationMs = toNumber(trace.cost?.durationMs);
    const llmCalls = toNumber(trace.cost?.totalLlmCalls);
    const totalTokens = toNumber(trace.cost?.totalTokens);

    const summary = `
        <div style="display:grid; gap:6px; margin-bottom: 12px;">
            <div><strong>Name:</strong> ${escapeHtml(trace.name || 'Execution')}</div>
            <div><strong>ID:</strong> <code>${escapeHtml(trace.executionId || '')}</code></div>
            <div><strong>Kind:</strong> ${escapeHtml(kind)}</div>
            <div><strong>Status:</strong> ${escapeHtml(status)}</div>
            <div><strong>Cost:</strong> ${escapeHtml(formatDurationMs(durationMs))}, ${escapeHtml(formatInt(llmCalls))} calls, ${escapeHtml(formatInt(totalTokens))} tokens</div>
            ${trace.error ? `<div style="color: var(--accent-danger);"><strong>Error:</strong> ${escapeHtml(trace.error)}</div>` : ''}
        </div>
    `;

    const tree = renderTraceNode(trace.root, 0);

    const raw = `
        <details style="margin-top: 12px;">
            <summary>RAW_TRACE_JSON</summary>
            <pre style="white-space: pre-wrap; word-break: break-word; margin-top: 8px;">${escapeHtml(JSON.stringify(trace, null, 2))}</pre>
        </details>
    `;

    return `${summary}${tree}${raw}`;
}

function renderTraceNode(node, depth) {
    const name = node.name || node.nodeId || 'node';
    const type = node.type || 'node';
    const status = prettyEnum(node.status);
    const durationMs = toNumber(node.cost?.durationMs);
    const llmCalls = toNumber(node.cost?.totalLlmCalls);
    const totalTokens = toNumber(node.cost?.totalTokens);

    const children = (node.children || []).map(c => renderTraceNode(c, depth + 1)).join('');

    const metrics = renderMap(node.metrics, formatContextValue);
    const labels = renderMap(node.labels, v => String(v ?? ''));

    const decisions = (node.decisions || []).length ? `
        <details style="margin-top: 8px;">
            <summary>DECISIONS (${node.decisions.length})</summary>
            ${(node.decisions || []).map(renderDecision).join('')}
        </details>
    ` : '';

    const alerts = (node.alerts || []).length ? `
        <details style="margin-top: 8px;">
            <summary>ALERTS (${node.alerts.length})</summary>
            ${(node.alerts || []).map(a => `<div style="margin-top:6px;"><code>${escapeHtml(a.type || 'alert')}</code> ${escapeHtml(a.message || '')}</div>`).join('')}
        </details>
    ` : '';

    const output = node.output ? `
        <details style="margin-top: 8px;">
            <summary>OUTPUT</summary>
            <pre style="white-space: pre-wrap; word-break: break-word; margin-top: 8px;">${escapeHtml(node.output)}</pre>
        </details>
    ` : '';

    const error = node.error ? `<div style="margin-top: 6px; color: var(--accent-danger);"><strong>Error:</strong> ${escapeHtml(node.error)}</div>` : '';

    return `
        <details ${depth === 0 ? 'open' : ''} style="border: 1px solid var(--border); border-radius: 10px; padding: 8px 10px; margin: 8px 0;">
            <summary>
                <strong>${escapeHtml(name)}</strong>
                <span style="color: var(--text-muted);"> · ${escapeHtml(type)} · ${escapeHtml(status)} · ${escapeHtml(formatDurationMs(durationMs))} · ${escapeHtml(formatInt(llmCalls))} calls · ${escapeHtml(formatInt(totalTokens))} tok</span>
            </summary>
            ${error}
            ${output}
            ${decisions}
            ${alerts}
            ${metrics}
            ${labels}
            ${children ? `<div style="margin-top: 10px; padding-left: 10px; border-left: 2px dashed var(--border);">${children}</div>` : ''}
        </details>
    `;
}

function renderDecision(d) {
    const winner = d.winnerCandidateId || '';
    const rows = (d.candidates || []).map(c => {
        const isWinner = winner && c.candidateId === winner;
        const preview = previewText(c.content || '', 160);
        return `
            <tr style="${isWinner ? 'outline: 1px solid rgba(124,255,107,0.35);' : ''}">
                <td><code>${escapeHtml(c.candidateId || '')}</code></td>
                <td>${escapeHtml(String(c.votes ?? 0))}</td>
                <td>${escapeHtml(String(c.score ?? 0))}</td>
                <td><code>${escapeHtml(preview)}</code></td>
            </tr>
        `;
    }).join('');

    return `
        <details style="margin-top: 8px;">
            <summary>${escapeHtml(d.type || 'decision')} · rounds=${escapeHtml(String(d.rounds ?? 0))} · winner=<code>${escapeHtml(winner)}</code></summary>
            <table style="width:100%; border-collapse: collapse; margin-top:8px;">
                <thead>
                    <tr style="text-align:left; border-bottom: 1px solid var(--border);">
                        <th>Candidate</th><th>Votes</th><th>Score</th><th>Preview</th>
                    </tr>
                </thead>
                <tbody>${rows}</tbody>
            </table>
        </details>
    `;
}

function renderMap(mapObj, valueFormatter) {
    if (!mapObj) return '';
    const entries = Object.entries(mapObj);
    if (entries.length === 0) return '';

    const rows = entries.map(([k, v]) => `
        <tr>
            <td style="padding:4px 8px; border-bottom: 1px solid var(--border);"><code>${escapeHtml(k)}</code></td>
            <td style="padding:4px 8px; border-bottom: 1px solid var(--border);"><code>${escapeHtml(valueFormatter(v))}</code></td>
        </tr>
    `).join('');

    return `
        <details style="margin-top: 8px;">
            <summary>MAP (${entries.length})</summary>
            <table style="width:100%; border-collapse: collapse; margin-top:8px;">
                <tbody>${rows}</tbody>
            </table>
        </details>
    `;
}

function formatContextValue(v) {
    if (v == null) return '';
    if (typeof v !== 'object') return String(v);
    if (v.stringValue != null) return String(v.stringValue);
    if (v.boolValue != null) return String(v.boolValue);
    if (v.intValue != null) return String(v.intValue);
    if (v.doubleValue != null) return String(v.doubleValue);
    if (v.datetimeIso != null) return String(v.datetimeIso);
    if (v.guidString != null) return String(v.guidString);
    return JSON.stringify(v);
}

function prettyEnum(value) {
    if (!value) return '-';
    return String(value)
        .replace('EXECUTION_TRACE_STATUS_', '')
        .replace('EXECUTION_TRACE_KIND_', '');
}

function toNumber(x) {
    if (x == null) return 0;
    if (typeof x === 'number') return x;
    const n = Number(x);
    return Number.isFinite(n) ? n : 0;
}

function formatDurationMs(ms) {
    const n = toNumber(ms);
    if (!n) return '0ms';
    if (n < 1000) return `${Math.round(n)}ms`;
    const s = n / 1000;
    if (s < 60) return `${s.toFixed(1)}s`;
    const m = Math.floor(s / 60);
    const rs = Math.round(s % 60);
    return `${m}m ${rs}s`;
}

function formatInt(x) {
    const n = toNumber(x);
    return n ? n.toLocaleString() : '0';
}

function previewText(s, maxLen) {
    if (!s) return '';
    const str = String(s).replace(/\s+/g, ' ').trim();
    return str.length <= maxLen ? str : str.slice(0, maxLen) + '...';
}

function escapeHtml(str) {
    if (!str) return '';
    return String(str).replace(/[&<>"']/g, c => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    }[c]));
}

// ============================================================
//  Init on load
// ============================================================

document.addEventListener('DOMContentLoaded', init);

