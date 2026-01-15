// ============================================================
//  MAKER System - Clean SSE-based Real-time UI
// ============================================================

// ============================================================
//  Theme Toggle System
// ============================================================

function initTheme() {
    const savedTheme = localStorage.getItem('maker-theme') || 'dark';
    applyTheme(savedTheme);
}

function toggleTheme() {
    const current = document.documentElement.getAttribute('data-theme') || 'dark';
    const next = current === 'dark' ? 'light' : 'dark';
    applyTheme(next);
    localStorage.setItem('maker-theme', next);
}

function applyTheme(theme) {
    document.documentElement.setAttribute('data-theme', theme);
    
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

// Initialize theme before DOM content loaded to prevent flash
initTheme();

// Expose to global
window.toggleTheme = toggleTheme;

// ============================================================
//  Application State
// ============================================================

const APP_STATE = {
    projects: {},
    activeProjectId: null,
    eventSource: null,
    eventSourceProjectId: null,  // Track which project the SSE is for
    proposals: {},       // taskId -> { content, success, error, promptTokens, completionTokens }
    tasks: [],           // { taskId, phase, message, depth }
    votingState: null,   // Current voting progress
    files: [],           // { category, name, path }
    activeFile: null,    // Currently selected file path
    artifacts: [],       // Stage results for ARTIFACTS view
    configTemplates: null,  // Config templates for project creation
    redFlags: [],        // Red flag events
    tokenStats: {        // Token statistics
        llmCalls: 0,
        promptTokens: 0,
        completionTokens: 0,
        totalTokens: 0
    },
    // Chat cards - categorized LLM conversations
    chatCards: [],       // { id, category, taskId, depth, provider, systemPrompt, userPrompt, response, tokens, timestamp }
    dom: {},
    // Project-level cache for state isolation
    projectCache: {}     // projectId -> { proposals, tasks, votingState, files, artifacts, redFlags, tokenStats, snapshot }
};

// ============================================================
//  Project State Cache Management
// ============================================================

function getEmptyProjectState() {
    return {
        proposals: {},
        tasks: [],
        votingState: null,
        files: [],
        activeFile: null,
        artifacts: [],
        redFlags: [],
        tokenStats: { llmCalls: 0, promptTokens: 0, completionTokens: 0, totalTokens: 0 },
        snapshot: null,
        lastUpdated: Date.now()
    };
}

function saveCurrentProjectState() {
    const pid = APP_STATE.activeProjectId;
    if (!pid) return;
    
    APP_STATE.projectCache[pid] = {
        proposals: { ...APP_STATE.proposals },
        tasks: [...APP_STATE.tasks],
        votingState: APP_STATE.votingState ? { ...APP_STATE.votingState } : null,
        files: [...APP_STATE.files],
        activeFile: APP_STATE.activeFile,
        artifacts: [...APP_STATE.artifacts],
        redFlags: [...APP_STATE.redFlags],
        tokenStats: { ...APP_STATE.tokenStats },
        snapshot: APP_STATE.projectCache[pid]?.snapshot || null,
        lastUpdated: Date.now()
    };
}

function loadProjectState(projectId) {
    const cached = APP_STATE.projectCache[projectId];
    if (cached) {
        APP_STATE.proposals = { ...cached.proposals };
        APP_STATE.tasks = [...cached.tasks];
        APP_STATE.votingState = cached.votingState ? { ...cached.votingState } : null;
        APP_STATE.files = [...cached.files];
        APP_STATE.activeFile = cached.activeFile;
        APP_STATE.artifacts = [...cached.artifacts];
        APP_STATE.redFlags = [...cached.redFlags];
        APP_STATE.tokenStats = { ...cached.tokenStats };
        return true;
    }
    return false;
}

function clearProjectState() {
    APP_STATE.proposals = {};
    APP_STATE.tasks = [];
    APP_STATE.votingState = null;
    APP_STATE.files = [];
    APP_STATE.activeFile = null;
    APP_STATE.artifacts = [];
    APP_STATE.redFlags = [];
    APP_STATE.tokenStats = { llmCalls: 0, promptTokens: 0, completionTokens: 0, totalTokens: 0 };
}

// ============================================================
//  Dynamic Project Creation (Zero-Code Config)
// ============================================================

function showCreateProjectModal() {
    document.getElementById('modal-overlay').classList.remove('hidden');
    // Load templates if not loaded
    if (!APP_STATE.configTemplates) {
        loadConfigTemplates();
    }
    // Load sample projects if not loaded
    if (!APP_STATE.sampleConfigs) {
        loadSampleProjects();
    }
}

function hideCreateProjectModal() {
    document.getElementById('modal-overlay').classList.add('hidden');
}

function switchConfigMode(mode) {
    document.querySelectorAll('.modal-tab').forEach(t => t.classList.remove('active'));
    document.querySelectorAll('.config-mode').forEach(m => m.classList.remove('active'));
    
    document.querySelector(`.modal-tab:nth-child(${mode === 'simple' ? 1 : 2})`).classList.add('active');
    document.getElementById(`mode-${mode}`).classList.add('active');
}

async function loadConfigTemplates() {
    try {
        APP_STATE.configTemplates = await fetchJson('/api/projects/templates');
    } catch (err) {
        console.error("Failed to load templates:", err);
    }
}

// Load sample projects
async function loadSampleProjects() {
    try {
        const data = await fetchJson('/sample-configs.json');
        APP_STATE.sampleConfigs = data.samples;
        renderSampleList(data.samples);
    } catch (err) {
        console.error("Failed to load sample configs:", err);
    }
}

function renderSampleList(samples) {
    const container = document.getElementById('sample-list');
    if (!container) return;
    
    container.innerHTML = samples.map((s, idx) => `
        <div class="sample-card" onclick="loadSampleConfig(${idx})">
            <div class="sample-card-icon">${s.config.icon}</div>
            <div class="sample-card-name">${s.name}</div>
            <div class="sample-card-desc">${s.description}</div>
            <div class="sample-card-tags">
                ${s.testFocus.map(t => `<span class="sample-tag">${t}</span>`).join('')}
            </div>
        </div>
    `).join('');
}

function loadSampleConfig(idx) {
    const sample = APP_STATE.sampleConfigs[idx];
    if (!sample) return;
    
    const config = sample.config;
    
    // Populate simple mode fields
    document.getElementById('new-cfg-name').value = config.name;
    document.getElementById('new-cfg-icon').value = config.icon;
    document.getElementById('new-cfg-desc').value = config.description;
    document.getElementById('new-cfg-task').value = config.task;
    document.getElementById('new-cfg-reliability').value = config.reliability;
    document.getElementById('new-cfg-max-calls').value = config.maxTotalLlmCalls || 500;
    document.getElementById('new-cfg-max-tokens').value = config.maxTotalTokens || 2000000;
    document.getElementById('new-cfg-max-duration').value = config.maxDurationMinutes || 30;
    document.getElementById('new-cfg-mode').value = config.executionMode || 'Production';
    document.getElementById('new-cfg-granularity').value = config.granularity || 'Balanced';
    
    // Also populate JSON mode
    document.getElementById('new-cfg-json').value = JSON.stringify(config, null, 2);
    
    // Visual feedback
    document.querySelectorAll('.sample-card').forEach((card, i) => {
        card.style.borderColor = i === idx ? 'var(--accent-main)' : '';
    });
}

function loadTemplate(type) {
    if (!APP_STATE.configTemplates) {
        alert("Templates not loaded yet.");
        return;
    }
    const template = APP_STATE.configTemplates[type];
    if (template) {
        document.getElementById('new-cfg-json').value = JSON.stringify(template, null, 2);
    }
}

async function createProject() {
    const activeMode = document.querySelector('.config-mode.active').id;
    let config;
    
    if (activeMode === 'mode-simple') {
        // Build config from form fields
        config = {
            name: document.getElementById('new-cfg-name').value,
            icon: document.getElementById('new-cfg-icon').value,
            description: document.getElementById('new-cfg-desc').value,
            task: document.getElementById('new-cfg-task').value,
            reliability: document.getElementById('new-cfg-reliability').value,
            maxTotalLlmCalls: parseInt(document.getElementById('new-cfg-max-calls').value, 10),
            maxTotalTokens: parseInt(document.getElementById('new-cfg-max-tokens').value, 10),
            maxDurationMinutes: parseInt(document.getElementById('new-cfg-max-duration').value, 10),
            executionMode: document.getElementById('new-cfg-mode').value,
            granularity: document.getElementById('new-cfg-granularity').value
        };
    } else {
        // Parse JSON from textarea
        try {
            config = JSON.parse(document.getElementById('new-cfg-json').value);
        } catch (err) {
            alert("Invalid JSON: " + err.message);
            return;
        }
    }
    
    try {
        const result = await fetch('/api/projects/create', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(config)
        }).then(r => r.json());
        
        if (result.success) {
            hideCreateProjectModal();
            await loadProjectList();
            selectProject(result.projectId);
        } else {
            alert("Failed to create project: " + (result.error || "Unknown error"));
        }
    } catch (err) {
        alert("Error creating project: " + err.message);
    }
}

// --- Initialization ---

document.addEventListener("DOMContentLoaded", () => {
    initDomCache();
    initEventListeners();
    loadProjectList();
});

function initDomCache() {
    const q = (sel) => document.querySelector(sel);
    APP_STATE.dom = {
        nav: q('#project-nav'),
        dashboard: q('#dashboard'),
        emptyState: q('#empty-state'),
        title: q('#project-name'),
        id: q('#project-id'),
        startBtn: q('#btn-start'),
        valStatus: q('#val-status'),
        valPhase: q('#val-phase'),
        valDepth: q('#val-depth'),
        valObjective: q('#val-objective'),
        pendingTasks: q('#pending-tasks'),
        tableSteps: q('#table-steps'),
        tableVotes: q('#table-votes'),
        logList: q('#log-stream'),
        workerGrid: q('#worker-grid'),
        finalResult: q('#final-result'),
        fileList: q('#file-list'),
        filePreview: q('#file-preview'),
        tabs: document.querySelectorAll('.tab-btn'),
        views: document.querySelectorAll('.term-view'),
        // Config panel elements
        configDetails: q('#config-details'),
        configReliability: q('#config-reliability'),
        configTask: q('#config-task'),
        cfgReliability: q('#cfg-reliability'),
        cfgK: q('#cfg-k'),
        cfgN: q('#cfg-n'),
        cfgTimeout: q('#cfg-timeout'),
        cfgMaxCalls: q('#cfg-max-calls'),
        cfgMaxTokens: q('#cfg-max-tokens'),
        cfgMaxDuration: q('#cfg-max-duration'),
        cfgDecomposer: q('#cfg-decomposer'),
        cfgSolver: q('#cfg-solver'),
        cfgComposer: q('#cfg-composer'),
        cfgContext: q('#cfg-context'),
        configContextSection: q('#config-context-section'),
        // Execution mode
        cfgMode: q('#cfg-mode'),
        cfgGranularity: q('#cfg-granularity'),
        cfgHardDepth: q('#cfg-hard-depth'),
        // Advanced MAKER params
        cfgClustering: q('#cfg-clustering'),
        cfgSimilarity: q('#cfg-similarity'),
        cfgBaseTemp: q('#cfg-base-temp'),
        cfgTempVariance: q('#cfg-temp-variance'),
        cfgMultiProvider: q('#cfg-multi-provider'),
        cfgRedFlag: q('#cfg-red-flag'),
        // Token stats elements
        valLlmCalls: q('#val-llm-calls'),
        valPromptTokens: q('#val-prompt-tokens'),
        valCompletionTokens: q('#val-completion-tokens'),
        valTotalTokens: q('#val-total-tokens'),
        valRedFlags: q('#val-red-flags'),
        // Compact status chips
        chipStatus: q('#chip-status'),
        chipPhase: q('#chip-phase'),
        chipDepth: q('#chip-depth'),
        chipTokens: q('#chip-tokens'),
        chipCalls: q('#chip-calls'),
        previewObjective: q('#preview-objective'),
        // Red flag panel
        redFlagPanel: q('#red-flag-panel'),
        redFlagList: q('#red-flag-list'),
        // Voting mode badge
        votingMode: q('#voting-mode')
    };
}

function initEventListeners() {
    APP_STATE.dom.tabs.forEach(btn => {
        btn.addEventListener('click', (e) => {
            APP_STATE.dom.tabs.forEach(b => b.classList.remove('active'));
            e.target.classList.add('active');
            const targetId = e.target.dataset.target;
            APP_STATE.dom.views.forEach(v => {
                v.classList.toggle('active', v.id === targetId);
            });
        });
    });
    APP_STATE.dom.startBtn.addEventListener('click', handleStartRun);
}

// --- Core Logic ---

async function loadProjectList() {
    try {
        const projects = await fetchJson('/api/projects');
        renderNav(projects);
        if (projects.length > 0) {
            selectProject(projects[0].id);
        }
    } catch (err) {
        console.error("Failed to load projects:", err);
    }
}

function selectProject(id) {
    // Skip if same project
    if (APP_STATE.activeProjectId === id) return;
    
    // Save current project state before switching
    saveCurrentProjectState();
    
    // Close SSE if switching projects
    if (APP_STATE.eventSource && APP_STATE.eventSourceProjectId !== id) {
        APP_STATE.eventSource.close();
        APP_STATE.eventSource = null;
        APP_STATE.eventSourceProjectId = null;
    }
    
    APP_STATE.activeProjectId = id;
    
    // Try to load cached state, otherwise clear
    if (!loadProjectState(id)) {
        clearProjectState();
    }
    
    // Update nav
    document.querySelectorAll('.nav-item').forEach(el => {
        el.classList.toggle('active', el.dataset.id === id);
    });
    
    // Show dashboard
    APP_STATE.dom.emptyState.classList.add('hidden');
    APP_STATE.dom.dashboard.classList.remove('hidden');
    
    // Update title
    const project = APP_STATE.projects[id];
    if (project) {
        APP_STATE.dom.title.textContent = project.name;
        APP_STATE.dom.id.textContent = `ID: ${id}`;
    }
    
    // Render cached data immediately (if available)
    renderTokenStats();
    renderRedFlags();
    renderTasks();
    renderWorkers();
    renderFiles();
    
    // Check if we have cached snapshot
    const cached = APP_STATE.projectCache[id];
    if (cached?.snapshot) {
        // Render cached snapshot immediately for instant feedback
        renderStatus({ status: cached.snapshot.status || 'idle' }, cached.snapshot);
        renderConfig(cached.snapshot.config);
        if (cached.snapshot.result?.content) {
            renderResultContent(cached.snapshot.result.content);
        }
    }
    
    // Fetch fresh data in background
    refreshProject();
}

async function refreshProject() {
    const id = APP_STATE.activeProjectId;
    if (!id) return;

    try {
        const [status, snapshot, timeline, files] = await Promise.all([
            fetchJson(`/api/projects/${id}/status`),
            fetchJson(`/api/projects/${id}/snapshot`),
            fetchJson(`/api/projects/${id}/timeline`),
            fetchJson(`/api/projects/${id}/files`)
        ]);

        // Verify we're still on the same project (user might have switched during fetch)
        if (APP_STATE.activeProjectId !== id) {
            console.log(`[Refresh] Discarding stale data for ${id}, active is ${APP_STATE.activeProjectId}`);
            return;
        }

        APP_STATE.files = files || [];
        
        // Cache snapshot for this project
        if (!APP_STATE.projectCache[id]) {
            APP_STATE.projectCache[id] = getEmptyProjectState();
        }
        APP_STATE.projectCache[id].snapshot = snapshot;
        APP_STATE.projectCache[id].lastUpdated = Date.now();
        
        renderStatus(status, snapshot);
        renderLogs(timeline);
        renderResult(snapshot);
        renderTasks();
        renderVoting();
        renderWorkers();
        renderFiles();

    } catch (err) {
        console.warn("Refresh failed:", err);
    }
}

async function handleStartRun() {
    const id = APP_STATE.activeProjectId;
    if (!id) return;
    
    APP_STATE.dom.startBtn.disabled = true;
    APP_STATE.dom.startBtn.textContent = 'INITIATING...';
    APP_STATE.proposals = {};
    APP_STATE.tasks = [];
    APP_STATE.votingState = null;
    APP_STATE.files = [];
    APP_STATE.artifacts = [];
    APP_STATE.redFlags = [];
    APP_STATE.tokenStats = { llmCalls: 0, promptTokens: 0, completionTokens: 0, totalTokens: 0 };
    APP_STATE.chatCards = [];  // Clear chat cards for new run
    
    // Clear displays
    renderTasks();
    renderVoting();
    renderWorkers();
    renderFiles();
    renderTokenStats();
    renderRedFlags();
    
    try {
        const res = await fetch(`/api/projects/${id}/run`, { method: 'POST' });
        const data = await res.json();
        
        if (data.success) {
            // Update status immediately
            APP_STATE.dom.valStatus.textContent = 'ACTIVE';
            APP_STATE.dom.valStatus.style.color = 'var(--accent-main)';
            updateStatusChip('status', 'ACTIVE', 'active');
            APP_STATE.dom.startBtn.textContent = 'RUNNING...';
            startEventStream(id);
            // Refresh to get config after task starts
            setTimeout(() => refreshProject(), 300);
        } else {
            APP_STATE.dom.startBtn.disabled = false;
            APP_STATE.dom.startBtn.textContent = '▶ INITIATE';
            alert(data.error || 'Failed to start');
        }
    } catch (err) {
        APP_STATE.dom.startBtn.disabled = false;
        APP_STATE.dom.startBtn.textContent = '▶ INITIATE';
        console.error("Start failed:", err);
    }
}

// --- SSE Real-time Streaming ---

function startEventStream(projectId) {
    // Close existing connection if for different project
    if (APP_STATE.eventSource) {
        APP_STATE.eventSource.close();
        APP_STATE.eventSource = null;
    }
    
    APP_STATE.eventSourceProjectId = projectId;
    APP_STATE.eventSource = new EventSource(`/api/projects/${projectId}/events`);
    
    APP_STATE.eventSource.onmessage = (e) => {
        try {
            const event = JSON.parse(e.data);
            
            // CRITICAL: Only process events for the currently active project
            // This prevents data corruption when user switches projects
            if (APP_STATE.activeProjectId !== projectId) {
                console.log(`[SSE] Ignoring event for inactive project ${projectId}, active is ${APP_STATE.activeProjectId}`);
                return;
            }
            
            handleSSEEvent(event, projectId);
        } catch (err) {
            console.warn("Failed to parse SSE event:", err);
        }
    };
    
    APP_STATE.eventSource.onerror = () => {
        console.log("SSE connection closed for project:", projectId);
        if (APP_STATE.eventSource) {
            APP_STATE.eventSource.close();
            APP_STATE.eventSource = null;
        }
        APP_STATE.eventSourceProjectId = null;
        
        // Only refresh if this is still the active project
        if (APP_STATE.activeProjectId === projectId) {
            refreshProject();
            APP_STATE.dom.startBtn.disabled = false;
            APP_STATE.dom.startBtn.textContent = '▶ INITIATE';
        }
    };
}

function handleSSEEvent(event, projectId) {
    // Double-check project isolation (belt and suspenders)
    if (projectId && APP_STATE.activeProjectId !== projectId) {
        return;
    }
    
    switch (event.type) {
        case 'progress':
            // Update status to ACTIVE when receiving progress
            APP_STATE.dom.valStatus.textContent = 'ACTIVE';
            APP_STATE.dom.valStatus.style.color = 'var(--accent-main)';
            updateStatusChip('status', 'ACTIVE', 'active');
            
            // Update HUD (always visible)
            APP_STATE.dom.valPhase.textContent = event.phase || '-';
            APP_STATE.dom.valDepth.textContent = event.depth ?? 0;
            APP_STATE.dom.valObjective.textContent = event.message || '-';
            
            // Update compact chips
            updateStatusChip('phase', event.phase || '-');
            updateStatusChip('depth', `D:${event.depth ?? 0}`);
            if (APP_STATE.dom.previewObjective) {
                APP_STATE.dom.previewObjective.textContent = truncate(event.message || '-', 60);
            }
            
            // Update task progress (STRATEGIC_PLANNING)
            updateTaskProgress(event);
            
            // Handle RedFlag phase
            if (event.phase === 'RedFlag') {
                addRedFlag(event.taskId, event.message);
                addLogEntry('RED_FLAG', event.message);
            }
            // Only add to log for major phase changes
            else if (['Starting', 'Decomposing', 'Solving', 'Composing', 'Completed', 'Failed', 'Assessing'].includes(event.phase)) {
                addLogEntry(event.phase, event.message);
            }
            break;
            
        case 'voting':
            // Update voting state (CONSENSUS_PROTOCOL)
            APP_STATE.votingState = {
                taskId: event.taskId,
                type: event.votingType,
                round: event.round,
                totalVotes: event.totalVotes,
                votesNeeded: event.votesNeeded,
                leaderVotes: event.leaderVotes,
                runnerUpVotes: event.runnerUpVotes,
                clusterCount: event.clusterCount,
                usedSemanticClustering: event.usedSemanticClustering,
                earlyTermination: event.earlyTermination
            };
            renderVoting();
            
            // Log early termination events
            if (event.earlyTermination) {
                addLogEntry('CONSENSUS', `Early termination! Saved ${event.savedProposals || '?'} LLM calls`);
            }
            break;
            
        case 'proposal':
            // Update proposals (SYSTEM_NODES) - final complete proposal
            const existingProposal = APP_STATE.proposals[event.taskId] || {};
            APP_STATE.proposals[event.taskId] = {
                content: event.content || '',
                success: event.success,
                error: event.message,
                promptTokens: event.promptTokens || 0,
                completionTokens: event.completionTokens || 0,
                providerName: event.providerName || null,
                streaming: false,
                // Use prompts from event if available, otherwise preserve from streaming
                systemPrompt: event.systemPrompt || existingProposal.systemPrompt,
                userPrompt: event.userPrompt || existingProposal.userPrompt
            };
            
            // Update token stats
            if (event.promptTokens || event.completionTokens) {
                APP_STATE.tokenStats.llmCalls++;
                APP_STATE.tokenStats.promptTokens += event.promptTokens || 0;
                APP_STATE.tokenStats.completionTokens += event.completionTokens || 0;
                APP_STATE.tokenStats.totalTokens = APP_STATE.tokenStats.promptTokens + APP_STATE.tokenStats.completionTokens;
                renderTokenStats();
            }
            
            // Create chat card for completed proposal
            createChatCard(event.taskId, APP_STATE.proposals[event.taskId]);
            
            renderWorkers();
            break;
            
        case 'streaming':
            // Real-time streaming token update (SYSTEM_NODES live output)
            const nodeId = event.taskId;  // Format: "taskId:proposalId"
            const workerId = event.workerId || 'unknown';
            const isCoordinator = workerId === 'COORDINATOR';
            
            // Initialize or update streaming state
            if (!APP_STATE.proposals[nodeId]) {
                APP_STATE.proposals[nodeId] = {
                    content: '',
                    success: true,
                    streaming: true,
                    providerName: event.providerName || null,
                    workerId: workerId,
                    isCoordinator: isCoordinator,
                    // Chat context for display
                    systemPrompt: null,
                    userPrompt: null
                };
            }
            
            // Update content with accumulated stream
            // OPTIMIZATION: If accumulatedContent is empty, just append token (reduces bandwidth)
            if (event.accumulatedContent) {
                APP_STATE.proposals[nodeId].content = event.accumulatedContent;
            } else if (event.token) {
                APP_STATE.proposals[nodeId].content = (APP_STATE.proposals[nodeId].content || '') + event.token;
            }
            APP_STATE.proposals[nodeId].streaming = !event.isLastToken;
            APP_STATE.proposals[nodeId].providerName = event.providerName;
            APP_STATE.proposals[nodeId].workerId = workerId;
            APP_STATE.proposals[nodeId].isCoordinator = isCoordinator;
            
            // Capture prompts on first token (for chat display)
            if (event.isFirstToken) {
                APP_STATE.proposals[nodeId].systemPrompt = event.systemPrompt || null;
                APP_STATE.proposals[nodeId].userPrompt = event.userPrompt || null;
            }
            
            // Mark completion
            if (event.isLastToken) {
                APP_STATE.proposals[nodeId].streaming = false;
                // Force full render on completion
                renderWorkers();
            } else {
                // Render immediately for real-time updates (throttled to 50ms max)
                throttledRenderWorkers();
            }
            break;
            
        case 'file':
            // Update files (DATA_CORE) - deduplicate by path
            const filePath = `${event.phase}/${event.taskId}`;
            const existingFileIndex = APP_STATE.files.findIndex(f => f.path === filePath);
            if (existingFileIndex === -1) {
                // New file - add it
                APP_STATE.files.push({
                    category: event.phase,
                    name: event.taskId,
                    path: filePath
                });
                renderFiles();
            }
            // If file already exists, skip (no duplicate)
            // Only log artifact files, not proposals/votes
            if (event.phase === 'artifacts') {
                addLogEntry('ARTIFACT', event.taskId);
            }
            break;
            
        case 'result':
            // Final result (ARTIFACTS)
            const resultStatus = event.success ? 'COMPLETED' : 'FAILED';
            APP_STATE.dom.valStatus.textContent = resultStatus;
            APP_STATE.dom.valStatus.style.color = event.success ? 'var(--accent-main)' : 'var(--accent-warn)';
            updateStatusChip('status', resultStatus, event.success ? 'active' : 'error');
            
            // Update final token stats from result
            if (event.totalTokens || event.promptTokens || event.completionTokens) {
                APP_STATE.tokenStats.totalTokens = event.totalTokens || APP_STATE.tokenStats.totalTokens;
                APP_STATE.tokenStats.promptTokens = event.promptTokens || APP_STATE.tokenStats.promptTokens;
                APP_STATE.tokenStats.completionTokens = event.completionTokens || APP_STATE.tokenStats.completionTokens;
                APP_STATE.tokenStats.llmCalls = event.totalLlmCalls || APP_STATE.tokenStats.llmCalls;
                renderTokenStats();
            }
            
            if (event.content) {
                renderResultContent(event.content);
            }
            addLogEntry(event.success ? 'COMPLETED' : 'FAILED', `Execution finished (${APP_STATE.tokenStats.totalTokens.toLocaleString()} tokens)`);
            
            // Save final state to cache after completion
            saveCurrentProjectState();
            
            setTimeout(() => refreshProject(), 500);
            break;
            
        case 'redFlag':
            // Direct red flag event
            addRedFlag(event.taskId, event.reason);
            break;
            
        case 'error':
            addLogEntry('ERROR', event.message);
            break;
    }
}

function updateTaskProgress(event) {
    let task = APP_STATE.tasks.find(t => t.taskId === event.taskId);
    if (!task) {
        task = { taskId: event.taskId, phases: [] };
        APP_STATE.tasks.push(task);
    }
    
    if (!task.phases.includes(event.phase)) {
        task.phases.push(event.phase);
    }
    task.currentPhase = event.phase;
    task.message = event.message;
    task.depth = event.depth;
    
    renderTasks();
}

// --- Rendering ---

function renderNav(projects) {
    APP_STATE.dom.nav.innerHTML = projects.map(p => {
        APP_STATE.projects[p.id] = p;
        return `
            <div class="nav-item" data-id="${p.id}" onclick="selectProject('${p.id}')">
                <span class="nav-icon">${p.icon}</span>
                <span>${p.name}</span>
            </div>
        `;
    }).join('');
}

function renderStatus(status, snapshot) {
    const isRunning = status.status === 'running';
    const statusText = isRunning ? 'ACTIVE' : (status.status || 'STANDBY').toUpperCase();
    APP_STATE.dom.valStatus.textContent = statusText;
    APP_STATE.dom.valStatus.style.color = isRunning ? 'var(--accent-main)' : 'var(--text-dim)';
    updateStatusChip('status', statusText, isRunning ? 'active' : '');
    
    if (isRunning) {
        APP_STATE.dom.startBtn.disabled = true;
        APP_STATE.dom.startBtn.textContent = 'RUNNING...';
        if (!APP_STATE.eventSource) {
            startEventStream(APP_STATE.activeProjectId);
        }
    } else {
        APP_STATE.dom.startBtn.disabled = false;
        APP_STATE.dom.startBtn.textContent = '▶ INITIATE';
    }
    
    // Render config if available
    if (snapshot?.config) {
        renderConfig(snapshot.config);
    }
}

function renderConfig(config) {
    if (!config) return;
    
    // Update config badge
    APP_STATE.dom.configReliability.textContent = config.reliability;
    
    // Update task description
    APP_STATE.dom.configTask.textContent = config.task || '-';
    
    // Update config grid values
    APP_STATE.dom.cfgReliability.textContent = config.reliability;
    APP_STATE.dom.cfgK.textContent = config.consensusK;
    APP_STATE.dom.cfgN.textContent = config.samplesPerRound;
    APP_STATE.dom.cfgTimeout.textContent = `${config.stepTimeoutSeconds}s`;
    
    // Budget-based limits
    APP_STATE.dom.cfgMaxCalls.textContent = config.maxTotalLlmCalls || '-';
    APP_STATE.dom.cfgMaxTokens.textContent = config.maxTotalTokens ? `${(config.maxTotalTokens / 1000).toFixed(0)}K` : '-';
    APP_STATE.dom.cfgMaxDuration.textContent = config.maxDurationMinutes ? `${config.maxDurationMinutes}min` : '-';
    
    // Execution mode
    const modeText = config.executionMode || 'Production';
    APP_STATE.dom.cfgMode.textContent = modeText;
    APP_STATE.dom.cfgMode.style.color = modeText === 'Academic' ? 'var(--accent-warn)' : 'var(--accent-main)';
    APP_STATE.dom.cfgGranularity.textContent = config.granularity || 'Balanced';
    APP_STATE.dom.cfgHardDepth.textContent = config.hardDepthCap || '50';
    
    // Update strategy types
    APP_STATE.dom.cfgDecomposer.textContent = config.decomposerType || 'Default';
    APP_STATE.dom.cfgSolver.textContent = config.solverType || 'Default';
    APP_STATE.dom.cfgComposer.textContent = config.composerType || 'Default';
    
    // Update advanced MAKER parameters
    APP_STATE.dom.cfgClustering.textContent = config.clusteringMethod || 'exact';
    APP_STATE.dom.cfgSimilarity.textContent = config.semanticSimilarityThreshold?.toFixed(2) || '0.85';
    APP_STATE.dom.cfgBaseTemp.textContent = config.baseTemperature?.toFixed(2) || '0.30';
    APP_STATE.dom.cfgTempVariance.textContent = `±${config.temperatureVariance?.toFixed(2) || '0.10'}`;
    APP_STATE.dom.cfgMultiProvider.textContent = config.useMultipleProviders ? 'YES' : 'NO';
    APP_STATE.dom.cfgRedFlag.textContent = config.redFlagThreshold || '3';
    
    // Update context if available
    if (config.context && Object.keys(config.context).length > 0) {
        APP_STATE.dom.configContextSection.style.display = 'block';
        APP_STATE.dom.cfgContext.innerHTML = Object.entries(config.context)
            .map(([k, v]) => `<span class="context-tag"><span class="key">${escapeHtml(k)}:</span> <span class="val">${escapeHtml(v)}</span></span>`)
            .join('');
    } else {
        APP_STATE.dom.configContextSection.style.display = 'none';
    }
}

function renderTasks() {
    const tasks = APP_STATE.tasks;
    
    const pending = tasks.filter(t => t.currentPhase !== 'Completed' && t.currentPhase !== 'Failed');
    APP_STATE.dom.pendingTasks.innerHTML = pending.length === 0 
        ? '<span style="color:var(--text-dim)">NO_PENDING</span>'
        : pending.map(t => `<span class="tag phase-${t.currentPhase?.toLowerCase()}">[D${t.depth}] ${t.taskId.substring(0,8)}...</span>`).join('');
    
    APP_STATE.dom.tableSteps.innerHTML = tasks.length === 0
        ? '<tr><td colspan="2" style="color:var(--text-dim)">AWAITING_DIRECTIVES</td></tr>'
        : tasks.map(t => {
            const phases = t.phases.map(p => {
                const isCurrent = p === t.currentPhase;
                return `<span class="phase-tag ${isCurrent ? 'current' : ''}">${p}</span>`;
            }).join(' → ');
            return `
                <tr>
                    <td><code>[D${t.depth}] ${t.taskId.substring(0,8)}</code></td>
                    <td>${phases}<br><small style="color:var(--text-dim)">${escapeHtml(t.message || '')}</small></td>
                </tr>
            `;
        }).join('');
}

function renderVoting() {
    const v = APP_STATE.votingState;
    
    // Update voting mode badge
    if (APP_STATE.dom.votingMode) {
        APP_STATE.dom.votingMode.textContent = v?.usedSemanticClustering ? 'SEMANTIC_CLUSTERING' : 'STREAMING_RACE';
    }
    
    if (!v) {
        APP_STATE.dom.tableVotes.innerHTML = '<tr><td colspan="3" style="color:var(--text-dim)">NO_ACTIVE_VOTE</td></tr>';
        return;
    }
    
    const gap = v.leaderVotes - v.runnerUpVotes;
    const progress = v.votesNeeded > 0 ? Math.round((gap / v.votesNeeded) * 100) : 0;
    const earlyBadge = v.earlyTermination ? '<span class="early-termination">EARLY_TERMINATION</span>' : '';
    
    APP_STATE.dom.tableVotes.innerHTML = `
        <tr>
            <td><code>${v.taskId.substring(0,8)}</code></td>
            <td>
                <div class="vote-bar">
                    <div class="vote-fill" style="width:${Math.min(100, Math.max(0, progress))}%"></div>
                </div>
                <small>Leader: ${v.leaderVotes} | RunnerUp: ${v.runnerUpVotes} | Gap: ${gap}/${v.votesNeeded}</small>
                ${v.clusterCount ? `<br><small>Clusters: ${v.clusterCount}</small>` : ''}
            </td>
            <td>
                <span class="vote-type">${v.type}</span>
                <span class="vote-round">R${v.round}</span>
                ${earlyBadge}
            </td>
        </tr>
    `;
}

function renderTokenStats() {
    const stats = APP_STATE.tokenStats;
    
    if (APP_STATE.dom.valLlmCalls) {
        APP_STATE.dom.valLlmCalls.textContent = stats.llmCalls.toLocaleString();
    }
    // Update compact chips
    updateStatusChip('calls', `${stats.llmCalls} calls`);
    updateStatusChip('tokens', formatTokens(stats.totalTokens));
    
    if (APP_STATE.dom.valPromptTokens) {
        APP_STATE.dom.valPromptTokens.textContent = stats.promptTokens.toLocaleString();
    }
    if (APP_STATE.dom.valCompletionTokens) {
        APP_STATE.dom.valCompletionTokens.textContent = stats.completionTokens.toLocaleString();
    }
    if (APP_STATE.dom.valTotalTokens) {
        APP_STATE.dom.valTotalTokens.textContent = stats.totalTokens.toLocaleString();
    }
    if (APP_STATE.dom.valRedFlags) {
        APP_STATE.dom.valRedFlags.textContent = APP_STATE.redFlags.length;
    }
}

function addRedFlag(taskId, reason) {
    APP_STATE.redFlags.push({
        taskId: taskId,
        reason: reason,
        timestamp: new Date()
    });
    renderRedFlags();
}

function renderRedFlags() {
    const flags = APP_STATE.redFlags;
    
    // Update counter
    if (APP_STATE.dom.valRedFlags) {
        APP_STATE.dom.valRedFlags.textContent = flags.length;
    }
    
    // Show/hide panel
    if (APP_STATE.dom.redFlagPanel) {
        APP_STATE.dom.redFlagPanel.style.display = flags.length > 0 ? 'flex' : 'none';
    }
    
    if (APP_STATE.dom.redFlagList && flags.length > 0) {
        APP_STATE.dom.redFlagList.innerHTML = flags.slice(-10).map(rf => {
            const time = rf.timestamp.toLocaleTimeString([], {hour12:false});
            return `<li><span class="rf-time">${time}</span><span class="rf-task">[${rf.taskId?.substring(0,8) || 'UNKNOWN'}]</span>${escapeHtml(rf.reason)}</li>`;
        }).join('');
    }
}

function renderLogs(timeline) {
    if (!timeline || timeline.length === 0) {
        APP_STATE.dom.logList.innerHTML = '<div style="color:var(--text-dim)">NO_LOGS</div>';
        return;
    }
    
    APP_STATE.dom.logList.innerHTML = timeline.slice(-50).map(e => {
        const time = new Date(e.timestamp).toLocaleTimeString([], {hour12:false});
        return `<div class="log-entry"><span class="log-time">${time}</span> <span class="log-phase">[${e.phase}]</span> ${escapeHtml(e.message)}</div>`;
    }).join('');
    
    APP_STATE.dom.logList.scrollTop = APP_STATE.dom.logList.scrollHeight;
}

function addLogEntry(phase, message) {
    const time = new Date().toLocaleTimeString([], {hour12:false});
    const entry = document.createElement('div');
    entry.className = 'log-entry';
    entry.innerHTML = `<span class="log-time">${time}</span> <span class="log-phase">[${phase}]</span> ${escapeHtml(message)}`;
    APP_STATE.dom.logList.appendChild(entry);
    APP_STATE.dom.logList.scrollTop = APP_STATE.dom.logList.scrollHeight;
}

function renderWorkers() {
    const workerGrid = APP_STATE.dom.workerGrid;
    if (!workerGrid) return;
    
    // Find streaming items
    const proposals = APP_STATE.proposals;
    const streamingIds = Object.keys(proposals).filter(id => proposals[id].streaming === true);
    
    // Build streaming section HTML
    let streamingHtml = '';
    if (streamingIds.length > 0) {
        const nodesHtml = streamingIds.map(id => {
            const p = proposals[id];
            const content = p.content || 'Generating...';
            const contentPreview = content.length > 200 ? content.substring(0, 200) + '...' : content;
            
            return `
                <div class="streaming-node">
                    <div class="streaming-node-header">
                        <span class="streaming-indicator">⏳</span>
                        <span class="streaming-label">STREAMING</span>
                        <span class="streaming-provider">🤖 ${p.providerName || 'LLM'}</span>
                    </div>
                    <div class="streaming-content">
                        ${escapeHtml(contentPreview)}<span class="streaming-cursor">▌</span>
                    </div>
                </div>
            `;
        }).join('');
        
        streamingHtml = `
            <div class="streaming-section">
                <div class="streaming-section-header">🔄 LIVE STREAMING (${streamingIds.length})</div>
                ${nodesHtml}
            </div>
        `;
    }
    
    // Build chat cards HTML
    let cardsHtml = '';
    if (APP_STATE.chatCards.length > 0) {
        // Group by category
        const byCategory = {};
        APP_STATE.chatCards.forEach(card => {
            const cat = card.category.name;
            if (!byCategory[cat]) byCategory[cat] = [];
            byCategory[cat].push(card);
        });
        
        cardsHtml = Object.entries(byCategory).map(([catName, cards]) => {
            const cat = cards[0].category;
            return `
                <div class="chat-category" data-category="${catName}">
                    <div class="chat-category-header" style="border-left-color: ${cat.color}">
                        <span class="chat-category-icon">${cat.icon}</span>
                        <span class="chat-category-name">${catName}</span>
                        <span class="chat-category-count">${cards.length}</span>
                    </div>
                    <div class="chat-cards-list">
                        ${cards.map(renderChatCard).join('')}
                    </div>
                </div>
            `;
        }).join('');
    }
    
    // Combine: streaming at top, cards below
    if (!streamingHtml && !cardsHtml) {
        workerGrid.innerHTML = '<div style="color:var(--text-dim); text-align:center; padding: 40px">AWAITING_LLM_RESPONSES</div>';
    } else {
        workerGrid.innerHTML = streamingHtml + cardsHtml;
    }
    
    // Auto-scroll to latest activity
    if (streamingIds.length > 0) {
        const lastStreaming = workerGrid.querySelector('.streaming-node:last-child');
        if (lastStreaming) {
            lastStreaming.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        }
    }
}

/**
 * Helper to create element from HTML string.
 */
function createElementFromHTML(htmlString) {
    const div = document.createElement('div');
    div.innerHTML = htmlString.trim();
    return div.firstChild;
}

// Extract operation type from proposal id (e.g., "task:COORD_ASSESS_xxx" -> "ASSESS")
function extractOperationType(id) {
    const match = id.match(/COORD_([A-Z]+)_/);
    return match ? match[1] : 'TASK';
}

function renderFiles() {
    const files = APP_STATE.files;
    
    if (!files || files.length === 0) {
        APP_STATE.dom.fileList.innerHTML = '<div style="padding:10px; color:var(--text-dim)">NO_FILES_YET</div>';
        APP_STATE.dom.filePreview.innerHTML = '<div class="preview-placeholder">[ SELECT_FILE ]</div>';
        return;
    }
    
    // Group by category
    const grouped = {};
    files.forEach(f => {
        if (!grouped[f.category]) grouped[f.category] = [];
        grouped[f.category].push(f);
    });
    
    const categoryIcons = {
        'proposals': '📝',
        'votes': '🗳️',
        'consensus': '📊',
        'artifacts': '📦'
    };
    
    let html = '';
    for (const [category, categoryFiles] of Object.entries(grouped).sort()) {
        html += `<div class="file-category">${categoryIcons[category] || '📄'} ${category.toUpperCase()}</div>`;
        categoryFiles.forEach(f => {
            const isActive = APP_STATE.activeFile === f.path;
            html += `
                <div class="file-item ${isActive ? 'active' : ''}" 
                     data-path="${f.path}"
                     onclick="loadFile('${f.category}', '${f.name}')">
                    <span class="file-icon">📄</span>
                    <span>${f.name}</span>
                </div>
            `;
        });
    }
    
    APP_STATE.dom.fileList.innerHTML = html;
}

async function loadFile(category, name) {
    const id = APP_STATE.activeProjectId;
    if (!id) return;
    
    APP_STATE.activeFile = `${category}/${name}`;
    renderFiles();
    
    APP_STATE.dom.filePreview.innerHTML = '<div class="preview-placeholder">LOADING...</div>';
    
    try {
        const res = await fetch(`/api/projects/${id}/files/${category}/${name}`);
        if (!res.ok) throw new Error('Failed to load file');
        
        const content = await res.text();

        // ============================================================
        //  Unified ExecutionTrace viewer (trace.json)
        //
        //  WHY:
        //  - `ExecutionTrace` is a Protobuf contract exported as JSON.
        //  - Rendering JSON as markdown is unreadable; provide a structured view.
        // ============================================================
        if (name.toLowerCase().endsWith('.json')) {
            const rendered = tryRenderExecutionTrace(content);
            if (rendered) {
                APP_STATE.dom.filePreview.innerHTML = rendered;
                return;
            }

            // Fallback: show raw JSON.
            APP_STATE.dom.filePreview.innerHTML =
                `<pre class="file-content json-body">${escapeHtml(content)}</pre>`;
            return;
        }
        
        // Use marked.js for proper markdown rendering (including tables)
        const html = typeof marked !== 'undefined' 
            ? marked.parse(content) 
            : escapeHtml(content);
        
        APP_STATE.dom.filePreview.innerHTML = `<div class="file-content markdown-body">${html}</div>`;
        
    } catch (err) {
        APP_STATE.dom.filePreview.innerHTML = `<div class="preview-placeholder">ERROR: ${err.message}</div>`;
    }
}

function tryRenderExecutionTrace(rawJson) {
    if (!rawJson || typeof rawJson !== 'string')
        return null;

    const trimmed = rawJson.trim();
    if (!trimmed.startsWith('{'))
        return null;

    try {
        const trace = JSON.parse(trimmed);
        if (!looksLikeExecutionTrace(trace))
            return null;

        return renderExecutionTraceHtml(trace, trimmed);
    } catch {
        return null;
    }
}

function looksLikeExecutionTrace(obj) {
    return !!(obj &&
        typeof obj === 'object' &&
        obj.executionId &&
        obj.root &&
        typeof obj.root === 'object');
}

function renderExecutionTraceHtml(trace, rawJson) {
    const status = prettyEnum(trace.status);
    const kind = prettyEnum(trace.kind);
    const name = trace.name || 'Execution';
    const durationMs = toNumber(trace.cost?.durationMs);
    const llmCalls = toNumber(trace.cost?.totalLlmCalls);
    const totalTokens = toNumber(trace.cost?.totalTokens);

    const summary = `
        <div class="trace-summary">
            <div><strong>Execution:</strong> ${escapeHtml(name)}</div>
            <div><strong>ID:</strong> <code>${escapeHtml(trace.executionId)}</code></div>
            <div><strong>Kind:</strong> ${escapeHtml(kind)}</div>
            <div><strong>Status:</strong> <span class="trace-status trace-${escapeHtml(status.toLowerCase())}">${escapeHtml(status)}</span></div>
            <div><strong>Cost:</strong> ${escapeHtml(formatDurationMs(durationMs))}, ${escapeHtml(formatInt(llmCalls))} calls, ${escapeHtml(formatInt(totalTokens))} tokens</div>
            ${trace.error ? `<div class="trace-error"><strong>Error:</strong> ${escapeHtml(trace.error)}</div>` : ''}
        </div>
    `;

    const tree = `<div class="trace-tree">${renderExecutionTraceNode(trace.root, 0)}</div>`;

    const raw = `
        <details class="trace-raw">
            <summary>RAW_TRACE_JSON</summary>
            <pre class="json-body">${escapeHtml(rawJson)}</pre>
        </details>
    `;

    return `<div class="trace-view">${summary}${tree}${raw}</div>`;
}

function renderExecutionTraceNode(node, depth) {
    if (!node) return '';

    const name = node.name || node.nodeId || 'node';
    const type = node.type || 'node';
    const status = prettyEnum(node.status);
    const durationMs = toNumber(node.cost?.durationMs);
    const llmCalls = toNumber(node.cost?.totalLlmCalls);
    const totalTokens = toNumber(node.cost?.totalTokens);

    const header = `
        <summary>
            <span class="trace-node-title">${escapeHtml(name)}</span>
            <span class="trace-node-meta">[${escapeHtml(type)} · ${escapeHtml(status)}]</span>
            <span class="trace-node-cost">${escapeHtml(formatDurationMs(durationMs))} · ${escapeHtml(formatInt(llmCalls))} calls · ${escapeHtml(formatInt(totalTokens))} tok</span>
        </summary>
    `;

    const metricsHtml = renderContextValueMap(node.metrics, 'METRICS');
    const labelsHtml = renderStringMap(node.labels, 'LABELS');
    const decisionsHtml = renderDecisions(node.decisions || []);
    const alertsHtml = renderAlerts(node.alerts || []);

    const outputHtml = node.output
        ? `<details class="trace-output"><summary>OUTPUT</summary><pre class="json-body">${escapeHtml(node.output)}</pre></details>`
        : '';
    const errorHtml = node.error
        ? `<div class="trace-error"><strong>Error:</strong> ${escapeHtml(node.error)}</div>`
        : '';

    const children = (node.children || []).map(c => renderExecutionTraceNode(c, depth + 1)).join('');
    const childrenHtml = children ? `<div class="trace-children">${children}</div>` : '';

    return `
        <details class="trace-node depth-${depth}" ${depth === 0 ? 'open' : ''}>
            ${header}
            <div class="trace-node-body">
                ${errorHtml}
                ${outputHtml}
                ${decisionsHtml}
                ${alertsHtml}
                ${metricsHtml}
                ${labelsHtml}
                ${childrenHtml}
            </div>
        </details>
    `;
}

function renderDecisions(decisions) {
    if (!decisions || decisions.length === 0) return '';

    const html = decisions.map(d => {
        const type = d.type || 'decision';
        const winner = d.winnerCandidateId || '';
        const rows = (d.candidates || []).map(c => {
            const isWinner = winner && c.candidateId === winner;
            const preview = previewText(c.content || '', 160);
            return `
                <tr class="${isWinner ? 'winner' : ''}">
                    <td><code>${escapeHtml(c.candidateId || '')}</code></td>
                    <td>${escapeHtml(formatInt(toNumber(c.votes)))}</td>
                    <td>${escapeHtml((c.score ?? 0).toFixed ? c.score.toFixed(3) : String(c.score ?? 0))}</td>
                    <td><code>${escapeHtml(preview)}</code></td>
                </tr>
            `;
        }).join('');

        return `
            <details class="trace-decision">
                <summary>DECISION · ${escapeHtml(type)} · rounds=${escapeHtml(String(d.rounds ?? 0))} · winner=<code>${escapeHtml(winner)}</code></summary>
                <table class="geek-table">
                    <thead>
                        <tr><th>CANDIDATE</th><th>VOTES</th><th>SCORE</th><th>PREVIEW</th></tr>
                    </thead>
                    <tbody>${rows}</tbody>
                </table>
            </details>
        `;
    }).join('');

    return `<div class="trace-decisions">${html}</div>`;
}

function renderAlerts(alerts) {
    if (!alerts || alerts.length === 0) return '';
    const items = alerts.map(a => `
        <div class="trace-alert">
            <span class="badge">${escapeHtml(a.type || 'alert')}</span>
            <span>${escapeHtml(a.message || '')}</span>
            ${a.recovered ? '<span class="badge ok">recovered</span>' : ''}
        </div>
    `).join('');
    return `<div class="trace-alerts"><div class="trace-section-title">ALERTS</div>${items}</div>`;
}

function renderStringMap(mapObj, title) {
    if (!mapObj) return '';
    const entries = Object.entries(mapObj);
    if (entries.length === 0) return '';

    const rows = entries.map(([k, v]) => `
        <tr><td><code>${escapeHtml(k)}</code></td><td><code>${escapeHtml(String(v ?? ''))}</code></td></tr>
    `).join('');

    return `
        <details class="trace-map">
            <summary>${escapeHtml(title)}</summary>
            <table class="geek-table">
                <thead><tr><th>KEY</th><th>VALUE</th></tr></thead>
                <tbody>${rows}</tbody>
            </table>
        </details>
    `;
}

function renderContextValueMap(mapObj, title) {
    if (!mapObj) return '';
    const entries = Object.entries(mapObj);
    if (entries.length === 0) return '';

    const rows = entries.map(([k, v]) => `
        <tr><td><code>${escapeHtml(k)}</code></td><td><code>${escapeHtml(formatContextValue(v))}</code></td></tr>
    `).join('');

    return `
        <details class="trace-map">
            <summary>${escapeHtml(title)}</summary>
            <table class="geek-table">
                <thead><tr><th>KEY</th><th>VALUE</th></tr></thead>
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

function renderResult(snapshot) {
    const content = snapshot.finalResult || snapshot.result;
    if (!content) {
        APP_STATE.dom.finalResult.innerHTML = '<div style="color:var(--text-dim); text-align:center; margin-top:20px">AWAITING_ARTIFACTS</div>';
        return;
    }
    renderResultContent(content);
}

function renderResultContent(content) {
    // Use marked.js for proper markdown rendering
    const html = typeof marked !== 'undefined' 
        ? marked.parse(content) 
        : escapeHtml(content);
    
    APP_STATE.dom.finalResult.innerHTML = `<div class="result-content markdown-body">${html}</div>`;
}

// --- Utilities ---

async function fetchJson(url) {
    const res = await fetch(url);
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    return res.json();
}

function escapeHtml(str) {
    if (!str) return '';
    return str.replace(/[&<>"']/g, c => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    }[c]));
}

// ============================================================
//  Performance: Throttled Rendering
// ============================================================

let _lastRenderTime = 0;
let _pendingRender = null;

/**
 * Throttled version of renderWorkers to avoid excessive DOM updates.
 * Max 20 renders per second during streaming for responsive UI.
 */
function throttledRenderWorkers() {
    const now = Date.now();
    const timeSinceLastRender = now - _lastRenderTime;
    
    // If we rendered recently, schedule a deferred render (50ms = 20 FPS)
    if (timeSinceLastRender < 50) {
        if (!_pendingRender) {
            _pendingRender = setTimeout(() => {
                _pendingRender = null;
                _lastRenderTime = Date.now();
                renderWorkers();
            }, 50 - timeSinceLastRender);
        }
        return;
    }
    
    // Otherwise render immediately
    _lastRenderTime = now;
    renderWorkers();
}

// ============================================================
//  Chat Cards - Categorized LLM Conversations
// ============================================================

/**
 * Determine chat card category from task ID and proposal ID.
 */
function getChatCategory(taskId) {
    // Check for LATE (discarded) proposals first - format: "taskId:LATE:D1"
    if (taskId.includes(':LATE:')) {
        return { name: 'DISCARDED', icon: '⚠️', color: '#ff6666' };  // Red for late/discarded
    }
    
    // Parse task ID structure: "root:S1:S2" or "taskId:proposalId"
    const parts = taskId.split(':');
    const proposalId = parts[parts.length - 1];
    
    // Determine category from proposal ID prefix
    if (proposalId.startsWith('D')) {
        return { name: 'DECOMPOSE', icon: '🔀', color: 'var(--accent-info)' };
    } else if (proposalId.startsWith('S')) {
        return { name: 'SOLVE', icon: '🧠', color: 'var(--accent-main)' };
    } else if (proposalId.startsWith('COORD')) {
        return { name: 'COMPOSE', icon: '📝', color: 'var(--accent-warn)' };
    }
    return { name: 'LLM', icon: '🤖', color: 'var(--text-dim)' };
}

/**
 * Extract task step info from task ID.
 */
function getTaskStep(taskId) {
    // Format: "root:S1:S2:proposalId" -> "Step 1.2"
    const parts = taskId.split(':');
    const steps = parts.filter(p => p.startsWith('S') && p.length <= 3);
    if (steps.length === 0) return 'Root';
    return `Step ${steps.map(s => s.substring(1)).join('.')}`;
}

/**
 * Create a chat card when LLM response completes.
 */
function createChatCard(taskId, proposal) {
    // Allow discarded (late) proposals to be shown even if success=false
    const isDiscarded = taskId.includes(':LATE:');
    if (!isDiscarded && (!proposal.success || !proposal.content)) return;
    if (!proposal.content && !proposal.error) return;
    
    const category = getChatCategory(taskId);
    const step = getTaskStep(taskId);
    
    const card = {
        id: `card-${Date.now()}-${Math.random().toString(36).substr(2, 5)}`,
        taskId: taskId,
        category: category,
        step: step,
        provider: proposal.providerName || 'unknown',
        systemPrompt: proposal.systemPrompt || null,
        userPrompt: proposal.userPrompt || null,
        response: proposal.content,
        tokens: {
            prompt: proposal.promptTokens || 0,
            completion: proposal.completionTokens || 0
        },
        timestamp: new Date()
    };
    
    APP_STATE.chatCards.push(card);
    renderChatCards();
}

/**
 * Render all chat cards in SYSTEM_NODES view.
 * Now just delegates to renderWorkers which handles both streaming and cards.
 */
function renderChatCards() {
    renderWorkers();
}

/**
 * Render a single chat card.
 */
function renderChatCard(card) {
    const userPreview = card.userPrompt 
        ? (card.userPrompt.length > 150 ? card.userPrompt.substring(0, 150) + '...' : card.userPrompt)
        : null;
    const responsePreview = card.response.length > 200 
        ? card.response.substring(0, 200) + '...' 
        : card.response;
    const timeStr = card.timestamp.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
    
    return `
        <div class="chat-card" onclick="toggleCardExpand('${card.id}')">
            <div class="chat-card-header">
                <span class="chat-card-step">${card.step}</span>
                <span class="chat-card-provider">🤖 ${card.provider}</span>
                <span class="chat-card-tokens">${card.tokens.prompt}+${card.tokens.completion}</span>
                <span class="chat-card-time">${timeStr}</span>
            </div>
            ${userPreview ? `
                <div class="chat-card-user">
                    <span class="chat-card-role">👤 USER:</span>
                    <span class="chat-card-text">${escapeHtml(userPreview)}</span>
                </div>
            ` : ''}
            <div class="chat-card-response">
                <span class="chat-card-role">🤖 ASSISTANT:</span>
                <span class="chat-card-text">${escapeHtml(responsePreview)}</span>
            </div>
            <div id="${card.id}-full" class="chat-card-full" style="display:none">
                ${card.systemPrompt ? `
                    <div class="chat-card-section">
                        <div class="chat-card-section-title">⚙️ SYSTEM PROMPT</div>
                        <div class="chat-card-section-content">${escapeHtml(card.systemPrompt)}</div>
                    </div>
                ` : ''}
                ${card.userPrompt ? `
                    <div class="chat-card-section">
                        <div class="chat-card-section-title">👤 USER PROMPT</div>
                        <div class="chat-card-section-content">${escapeHtml(card.userPrompt)}</div>
                    </div>
                ` : ''}
                <div class="chat-card-section">
                    <div class="chat-card-section-title">🤖 FULL RESPONSE</div>
                    <div class="chat-card-section-content">${escapeHtml(card.response)}</div>
                </div>
            </div>
        </div>
    `;
}

/**
 * Toggle card expansion.
 */
function toggleCardExpand(cardId) {
    const full = document.getElementById(`${cardId}-full`);
    if (full) {
        full.style.display = full.style.display === 'none' ? 'block' : 'none';
    }
}

// ============================================================
// Status Chip Helpers
// ============================================================

/**
 * Update a compact status chip.
 */
function updateStatusChip(type, value, state = '') {
    const chipId = `chip-${type}`;
    const chip = APP_STATE.dom[`chip${type.charAt(0).toUpperCase() + type.slice(1)}`] || document.getElementById(chipId);
    if (chip) {
        chip.textContent = value;
        // Update visual state
        chip.classList.remove('active', 'warn', 'error');
        if (state) chip.classList.add(state);
    }
}

/**
 * Format token count for compact display.
 */
function formatTokens(count) {
    if (count >= 1_000_000) return `${(count / 1_000_000).toFixed(1)}M tok`;
    if (count >= 1_000) return `${(count / 1_000).toFixed(1)}K tok`;
    return `${count} tok`;
}

// Expose to global for onclick handlers
window.selectProject = selectProject;
window.loadFile = loadFile;
window.toggleCardExpand = toggleCardExpand;
