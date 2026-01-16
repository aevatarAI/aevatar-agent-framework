// ═══════════════════════════════════════════════════════════════════════════════
//  WORKFLOW VISUALIZER
//  DAG Graph with Real-time Updates via SSE
// ═══════════════════════════════════════════════════════════════════════════════

// ─────────────────────────────────────────────────────────────────────────────
//  State
// ─────────────────────────────────────────────────────────────────────────────

const state = {
    projects: [],
    currentProject: null,
    currentRun: null,
    eventSource: null,
    
    // Graph state
    nodes: new Map(),      // stepId -> node data
    edges: [],             // { from, to, type }
    selectedNodeId: null,
    
    // View state
    zoom: 1,
    panX: 0,
    panY: 0,
    layout: 'dagre',       // dagre | tree | timeline
    
    // Drag state
    isDragging: false,
    dragStartX: 0,
    dragStartY: 0
};

// Node type configuration
const NODE_CONFIG = {
    llm_call: { icon: '🤖', label: 'LLM', color: '#3498db' },
    vote: { icon: '🗳️', label: 'VOTE', color: '#9b59b6' },
    conditional: { icon: '🔀', label: 'IF', color: '#e67e22' },
    fan_out: { icon: '⚡', label: 'FAN-OUT', color: '#2ecc71' },
    workflow_call: { icon: '📋', label: 'WORKFLOW', color: '#f39c12' },
    checkpoint: { icon: '💾', label: 'CHECKPOINT', color: '#1abc9c' },
    parallel: { icon: '⫽', label: 'PARALLEL', color: '#2ecc71' }
};

// ─────────────────────────────────────────────────────────────────────────────
//  Initialization
// ─────────────────────────────────────────────────────────────────────────────

document.addEventListener('DOMContentLoaded', async () => {
    initGraphCanvas();
    await loadProjects();
    
    // Check URL params for direct link
    const params = new URLSearchParams(window.location.search);
    const projectId = params.get('project');
    const runId = params.get('run');
    
    console.log('Workflow page init:', { projectId, runId, projects: state.projects });
    
    if (projectId) {
        // Set project selection
        document.getElementById('project-select').value = projectId;
        state.currentProject = state.projects.find(p => p.id === projectId);
        
        // Load runs for this project
        await loadRuns(projectId);
        
        // If we have a runId, select it
        if (runId) {
            document.getElementById('run-select').value = runId;
        }
        
        // Connect to SSE (even without specific runId, we can receive events)
        connectToRun();
    }
});

// ─────────────────────────────────────────────────────────────────────────────
//  API
// ─────────────────────────────────────────────────────────────────────────────

async function loadProjects() {
    try {
        const res = await fetch('/api/projects');
        // API 直接返回数组，不是 { projects: [...] }
        state.projects = await res.json();
        
        console.log('Loaded projects:', state.projects);
        
        const select = document.getElementById('project-select');
        select.innerHTML = '<option value="">Select project...</option>';
        
        state.projects.forEach(p => {
            const opt = document.createElement('option');
            opt.value = p.id;
            opt.textContent = `${p.name} (${p.strategy})`;
            select.appendChild(opt);
        });
        
        select.onchange = () => {
            const projectId = select.value;
            state.currentProject = state.projects.find(p => p.id === projectId);
            loadRuns(projectId);
        };
        
        return state.projects;
    } catch (err) {
        console.error('Failed to load projects:', err);
        return [];
    }
}

async function loadRuns(projectId) {
    const select = document.getElementById('run-select');
    select.innerHTML = '<option value="">Select run...</option>';
    
    if (!projectId) return;
    
    try {
        const res = await fetch(`/api/projects/${projectId}/runs`);
        // API 直接返回数组，不是 { runs: [...] }
        const runs = await res.json();
        
        console.log('Loaded runs:', runs);
        
        runs.forEach(r => {
            const opt = document.createElement('option');
            opt.value = r.runId;
            opt.textContent = `${r.runId.slice(0, 8)}... (${r.status})`;
            select.appendChild(opt);
        });
        
        state.currentProject = state.projects.find(p => p.id === projectId);
    } catch (err) {
        console.error('Failed to load runs:', err);
    }
}

function connectToRun() {
    const projectId = document.getElementById('project-select').value;
    const runId = document.getElementById('run-select').value;
    
    if (!projectId) {
        alert('Please select a project');
        return;
    }
    
    // Disconnect existing
    if (state.eventSource) {
        state.eventSource.close();
    }
    
    // Reset state
    state.nodes.clear();
    state.edges = [];
    state.selectedNodeId = null;
    state.currentRun = runId;
    
    // Update URL
    const url = new URL(window.location);
    url.searchParams.set('project', projectId);
    if (runId) {
        url.searchParams.set('run', runId);
    }
    window.history.replaceState({}, '', url);
    
    // Update UI
    const project = state.projects.find(p => p.id === projectId);
    document.getElementById('wf-name').textContent = 
        project?.options?.cognitiveWorkflow || 'Workflow';
    document.getElementById('empty-state').classList.add('hidden');
    setConnectionStatus('connecting');
    
    // Connect SSE - 使用正确的 endpoint
    state.eventSource = new EventSource(`/api/projects/${projectId}/events`);
    
    state.eventSource.onopen = () => {
        setConnectionStatus('connected');
    };
    
    state.eventSource.onerror = () => {
        setConnectionStatus('disconnected');
    };
    
    state.eventSource.onmessage = (e) => {
        try {
            const evt = JSON.parse(e.data);
            handleEvent(evt);
        } catch (err) {
            console.error('Failed to parse event:', err);
        }
    };
}

function setConnectionStatus(status) {
    const el = document.getElementById('connection-status');
    const dot = el.querySelector('.status-dot');
    
    dot.className = `status-dot ${status}`;
    el.childNodes[1].textContent = status === 'connected' ? 'Connected' :
                                    status === 'connecting' ? 'Connecting...' : 'Disconnected';
}

// ─────────────────────────────────────────────────────────────────────────────
//  Event Handling
// ─────────────────────────────────────────────────────────────────────────────

function handleEvent(evt) {
    switch (evt.type) {
        case 'workflow_step':
            handleWorkflowStep(evt);
            break;
        case 'progress':
            handleProgress(evt);
            break;
        case 'complete':
        case 'error':
            handleComplete(evt);
            break;
    }
}

function handleWorkflowStep(evt) {
    const nodeId = evt.stepId;
    const existing = state.nodes.get(nodeId) || {};
    
    // Debug: log conversation data
    if (evt.systemPrompt || evt.userPrompt || evt.assistantResponse) {
        console.log('🗣️ Conversation data received:', {
            stepId: nodeId,
            systemPrompt: evt.systemPrompt?.substring(0, 50) + '...',
            userPrompt: evt.userPrompt?.substring(0, 50) + '...',
            assistantResponse: evt.assistantResponse?.substring(0, 50) + '...'
        });
    }
    
    // Update node - preserve existing conversation data if new event doesn't have it
    const newNode = {
        ...existing,
        id: nodeId,
        type: evt.stepType,
        status: evt.status?.toLowerCase() || 'pending',
        progress: evt.progress || 0,
        message: evt.message,
        depth: evt.depth || 0,
        parentId: evt.parentStepId || null,
        // Vote specific
        voteRound: evt.voteRound,
        voteMaxRounds: evt.voteMaxRounds,
        voteK: evt.voteK,
        voteCurrentVotes: evt.voteCurrentVotes,
        // Fan-out specific
        parallelTotal: evt.parallelTotal,
        parallelCompleted: evt.parallelCompleted,
        parallelFailed: evt.parallelFailed,
        // Stats
        durationMs: evt.durationMs,
        llmCalls: evt.llmCalls,
        tokensUsed: evt.tokensUsed,
        // Conversation - preserve existing if not in new event
        systemPrompt: evt.systemPrompt || existing.systemPrompt,
        userPrompt: evt.userPrompt || existing.userPrompt,
        assistantResponse: evt.assistantResponse || existing.assistantResponse,
        // Timestamp
        timestamp: Date.now()
    };
    
    state.nodes.set(nodeId, newNode);
    
    // Add edge from parent
    if (evt.parentStepId && !state.edges.find(e => e.from === evt.parentStepId && e.to === nodeId)) {
        state.edges.push({
            from: evt.parentStepId,
            to: nodeId,
            type: 'default'
        });
    }
    
    // Infer edges between sequential steps
    inferEdges();
    
    // Update graph
    renderGraph();
    updateStats();
    updateStructureTree();
    
    // Update detail if selected
    if (state.selectedNodeId === nodeId) {
        showNodeDetail(nodeId);
    }
    
    // Update conversation stream if visible
    updateConversationIfVisible();
}

function handleProgress(evt) {
    // Update stats
    if (evt.totalLlmCalls) {
        document.getElementById('stat-llm-calls').textContent = evt.totalLlmCalls;
    }
    if (evt.totalPromptTokens || evt.totalCompletionTokens) {
        const total = (evt.totalPromptTokens || 0) + (evt.totalCompletionTokens || 0);
        document.getElementById('stat-tokens').textContent = formatNumber(total);
    }
}

function handleComplete(evt) {
    document.getElementById('stat-status').textContent = evt.success ? 'Completed' : 'Failed';
    document.getElementById('stat-status').style.color = evt.success ? 'var(--status-completed)' : 'var(--status-failed)';
    setConnectionStatus('disconnected');
}

// Infer edges between steps based on execution order
function inferEdges() {
    const nodes = Array.from(state.nodes.values())
        .sort((a, b) => (a.timestamp || 0) - (b.timestamp || 0));
    
    // Group by depth
    const byDepth = new Map();
    nodes.forEach(n => {
        const d = n.depth || 0;
        if (!byDepth.has(d)) byDepth.set(d, []);
        byDepth.get(d).push(n);
    });
    
    // Connect sequential nodes at same depth
    byDepth.forEach((depthNodes, depth) => {
        for (let i = 0; i < depthNodes.length - 1; i++) {
            const from = depthNodes[i];
            const to = depthNodes[i + 1];
            
            // Skip if already have parent edge or if it's a sub-step
            if (to.parentId || to.id.includes('.gen[')) continue;
            
            // Check if edge exists
            if (!state.edges.find(e => e.from === from.id && e.to === to.id)) {
                state.edges.push({
                    from: from.id,
                    to: to.id,
                    type: 'sequential'
                });
            }
        }
    });
}

// ─────────────────────────────────────────────────────────────────────────────
//  Graph Rendering (SVG)
// ─────────────────────────────────────────────────────────────────────────────

function initGraphCanvas() {
    const canvas = document.getElementById('graph-canvas');
    
    // Mouse drag for panning
    canvas.addEventListener('mousedown', (e) => {
        if (e.target === canvas || e.target.tagName === 'svg') {
            state.isDragging = true;
            state.dragStartX = e.clientX - state.panX;
            state.dragStartY = e.clientY - state.panY;
            canvas.style.cursor = 'grabbing';
        }
    });
    
    document.addEventListener('mousemove', (e) => {
        if (state.isDragging) {
            state.panX = e.clientX - state.dragStartX;
            state.panY = e.clientY - state.dragStartY;
            updateTransform();
        }
    });
    
    document.addEventListener('mouseup', () => {
        state.isDragging = false;
        canvas.style.cursor = 'grab';
    });
    
    // Mouse wheel for zooming
    canvas.addEventListener('wheel', (e) => {
        e.preventDefault();
        const delta = e.deltaY > 0 ? -0.1 : 0.1;
        zoomGraph(delta, e.clientX, e.clientY);
    });
}

function renderGraph() {
    const nodesLayer = document.getElementById('nodes-layer');
    const edgesLayer = document.getElementById('edges-layer');
    
    if (!nodesLayer || !edgesLayer) return;
    
    // Calculate layout
    const layout = calculateLayout();
    
    // Render edges first (behind nodes)
    edgesLayer.innerHTML = renderEdges(layout);
    
    // Render nodes
    nodesLayer.innerHTML = renderNodes(layout);
    
    // Update counts
    document.getElementById('node-count').textContent = `${state.nodes.size} nodes`;
    document.getElementById('edge-count').textContent = `${state.edges.length} edges`;
    
    // Apply transform
    updateTransform();
}

function calculateLayout() {
    const nodes = Array.from(state.nodes.values());
    const positions = new Map();
    
    const nodeWidth = 200;
    const nodeHeight = 90;
    const hGap = 60;
    const vGap = 40;
    
    if (state.layout === 'timeline') {
        // Timeline: vertical list
        nodes.sort((a, b) => (a.timestamp || 0) - (b.timestamp || 0));
        nodes.forEach((node, i) => {
            positions.set(node.id, {
                x: 50 + (node.depth || 0) * (nodeWidth + hGap),
                y: 50 + i * (nodeHeight + vGap),
                width: nodeWidth,
                height: nodeHeight
            });
        });
    } else if (state.layout === 'tree') {
        // Tree: hierarchical by depth
        const byDepth = new Map();
        nodes.forEach(n => {
            const d = n.depth || 0;
            if (!byDepth.has(d)) byDepth.set(d, []);
            byDepth.get(d).push(n);
        });
        
        byDepth.forEach((depthNodes, depth) => {
            const startX = 50;
            depthNodes.forEach((node, i) => {
                positions.set(node.id, {
                    x: startX + i * (nodeWidth + hGap),
                    y: 50 + depth * (nodeHeight + vGap),
                    width: nodeWidth,
                    height: nodeHeight
                });
            });
        });
    } else {
        // DAG: smart positioning
        // Group main steps and sub-steps
        const mainSteps = nodes.filter(n => !n.id.includes('.gen['));
        const subSteps = nodes.filter(n => n.id.includes('.gen['));
        
        // Position main steps vertically
        mainSteps.sort((a, b) => (a.timestamp || 0) - (b.timestamp || 0));
        mainSteps.forEach((node, i) => {
            positions.set(node.id, {
                x: 50,
                y: 50 + i * (nodeHeight + vGap),
                width: nodeWidth,
                height: nodeHeight
            });
        });
        
        // Position sub-steps to the right of their parent
        subSteps.forEach(node => {
            // Extract parent id (e.g., "solve_atomic.gen[1]" -> "solve_atomic")
            const parentId = node.id.split('.gen[')[0];
            const parentPos = positions.get(parentId);
            
            if (parentPos) {
                // Find index of this sub-step
                const siblings = subSteps.filter(s => s.id.startsWith(parentId + '.gen['));
                const idx = siblings.indexOf(node);
                
                positions.set(node.id, {
                    x: parentPos.x + nodeWidth + hGap,
                    y: parentPos.y + idx * (nodeHeight * 0.6 + 10),
                    width: nodeWidth * 0.85,
                    height: nodeHeight * 0.8
                });
            }
        });
    }
    
    return positions;
}

// ─────────────────────────────────────────────────────────────────────────────
//  Drag State
// ─────────────────────────────────────────────────────────────────────────────
let dragState = {
    dragging: false,
    nodeId: null,
    startX: 0,
    startY: 0,
    nodeStartX: 0,
    nodeStartY: 0,
    hasMoved: false  // Track if actual movement occurred
};

// Store custom positions (overrides calculated layout)
const customPositions = new Map();

function renderNodes(layout) {
    let html = '';
    
    // SVG Definitions - Light Theme
    html += `
        <defs>
            <!-- Drop Shadow for cards -->
            <filter id="node-shadow" x="-20%" y="-20%" width="140%" height="140%">
                <feDropShadow dx="0" dy="2" stdDeviation="6" flood-color="rgba(0,0,0,0.1)" />
            </filter>
            <!-- Arrow markers - high visibility -->
            <marker id="arrowhead" markerWidth="12" markerHeight="9" refX="10" refY="4.5" orient="auto">
                <path d="M 0 0 L 12 4.5 L 0 9 L 3 4.5 Z" fill="#64748b" />
            </marker>
            <marker id="arrowhead-active" markerWidth="12" markerHeight="9" refX="10" refY="4.5" orient="auto">
                <path d="M 0 0 L 12 4.5 L 0 9 L 3 4.5 Z" fill="#6366f1" />
            </marker>
        </defs>
    `;
    
    state.nodes.forEach((node, id) => {
        // Use custom position if dragged, else calculated
        let pos = customPositions.get(id) || layout.get(id);
        if (!pos) return;
        
        const config = NODE_CONFIG[node.type] || NODE_CONFIG.llm_call;
        const isSelected = state.selectedNodeId === id;
        const isSubStep = id.includes('.gen[');
        const isRunning = node.status === 'running';
        const progressPercent = Math.round((node.progress || 0) * 100);
        
        const w = pos.width;
        const h = pos.height;
        
        // 状态颜色 - Light theme
        const statusColors = {
            pending: '#94a3b8',
            running: '#6366f1',
            completed: '#22c55e',
            failed: '#ef4444'
        };
        const statusColor = statusColors[node.status] || statusColors.pending;
        
        html += `
            <g class="graph-node ${isSelected ? 'selected' : ''} status-${node.status}" 
               data-node-id="${id}"
               transform="translate(${pos.x}, ${pos.y})"
               onmousedown="startDrag(event, '${id}')"
               onclick="handleNodeClick(event, '${id}')"
               onmouseenter="showTooltip(event, '${id}')"
               onmouseleave="hideTooltip()">
                
                <!-- 卡片背景 -->
                <rect class="node-card" width="${w}" height="${h}" rx="10" filter="url(#node-shadow)" />
                
                <!-- 左侧类型指示条 -->
                <rect class="node-type-stripe" x="0" y="10" width="4" height="${h - 20}" rx="2" fill="${config.color}" />
                
                <!-- 图标容器 -->
                <rect class="node-icon-bg" x="14" y="12" width="32" height="32" rx="8" fill="${config.color}22" />
                <text class="node-icon" x="30" y="34">${config.icon}</text>
                
                <!-- 类型标签 -->
                <text class="node-type" x="54" y="24">${config.label}</text>
                
                <!-- 节点 ID -->
                <text class="node-title" x="54" y="40">${truncate(id, isSubStep ? 16 : 20)}</text>
                
                <!-- 状态指示器 -->
                <g class="node-status-indicator" transform="translate(${w - 20}, 20)">
                    <circle r="6" fill="${statusColor}" class="${isRunning ? 'pulse' : ''}" />
                </g>
                
                <!-- 消息 -->
                <text class="node-message" x="14" y="${h - 22}">${truncate(node.message || '—', isSubStep ? 20 : 26)}</text>
                
                <!-- 进度条 -->
                <rect class="progress-bg" x="14" y="${h - 12}" width="${w - 28}" height="4" rx="2" />
                <rect class="progress-bar status-${node.status}" x="14" y="${h - 12}" 
                      width="${Math.max(0, (w - 28) * (node.progress || 0))}" height="4" rx="2" />
                
                <!-- 选中高亮 -->
                ${isSelected ? `<rect class="node-highlight" x="-2" y="-2" width="${w + 4}" height="${h + 4}" rx="12" />` : ''}
                
                <!-- 连接锚点 -->
                <circle class="anchor left" cx="0" cy="${h / 2}" r="5" />
                <circle class="anchor right" cx="${w}" cy="${h / 2}" r="5" />
            </g>
        `;
    });
    
    return html;
}

// ─────────────────────────────────────────────────────────────────────────────
//  Drag Handlers
// ─────────────────────────────────────────────────────────────────────────────
function startDrag(event, nodeId) {
    if (event.button !== 0) return; // Only left click
    event.preventDefault();
    
    const layout = calculateLayout();
    const pos = customPositions.get(nodeId) || layout.get(nodeId);
    if (!pos) return;
    
    dragState = {
        dragging: true,
        nodeId: nodeId,
        startX: event.clientX,
        startY: event.clientY,
        nodeStartX: pos.x,
        nodeStartY: pos.y,
        width: pos.width,
        height: pos.height,
        hasMoved: false
    };
    
    document.addEventListener('mousemove', onDrag);
    document.addEventListener('mouseup', endDrag);
}

function onDrag(event) {
    if (!dragState.dragging) return;
    event.preventDefault();
    
    // Calculate delta in screen coordinates, then adjust for zoom
    const dx = (event.clientX - dragState.startX) / state.zoom;
    const dy = (event.clientY - dragState.startY) / state.zoom;
    
    // Only mark as moved if moved more than 3px
    if (Math.abs(dx) > 3 || Math.abs(dy) > 3) {
        dragState.hasMoved = true;
        
        // Add dragging class on first move
        const nodeEl = document.querySelector(`[data-node-id="${dragState.nodeId}"]`);
        if (nodeEl && !nodeEl.classList.contains('dragging')) {
            nodeEl.classList.add('dragging');
        }
    }
    
    if (!dragState.hasMoved) return;
    
    const newX = dragState.nodeStartX + dx;
    const newY = dragState.nodeStartY + dy;
    
    // Update custom position
    customPositions.set(dragState.nodeId, {
        x: newX,
        y: newY,
        width: dragState.width,
        height: dragState.height
    });
    
    // Directly update the node's transform (no full re-render)
    const nodeEl = document.querySelector(`[data-node-id="${dragState.nodeId}"]`);
    if (nodeEl) {
        nodeEl.setAttribute('transform', `translate(${newX}, ${newY})`);
    }
    
    // Update connected edges
    updateEdgesForNode(dragState.nodeId);
}

function endDrag() {
    const nodeId = dragState.nodeId;
    const hasMoved = dragState.hasMoved;
    
    if (nodeId) {
        const nodeEl = document.querySelector(`[data-node-id="${nodeId}"]`);
        if (nodeEl) nodeEl.classList.remove('dragging');
    }
    
    dragState.dragging = false;
    dragState.nodeId = null;
    dragState.hasMoved = false;
    
    document.removeEventListener('mousemove', onDrag);
    document.removeEventListener('mouseup', endDrag);
    
    // Full re-render only if moved
    if (hasMoved) {
        renderGraph();
    }
}

// Handle click - only select if not dragged
function handleNodeClick(event, nodeId) {
    // If we just finished dragging, don't select
    if (dragState.hasMoved) {
        event.stopPropagation();
        return;
    }
    selectNode(nodeId);
}

function updateEdgesForNode(nodeId) {
    const layout = calculateLayout();
    const edgesLayer = document.getElementById('edges-layer');
    if (!edgesLayer) return;
    
    // Re-render all edges (simpler than selective update)
    edgesLayer.innerHTML = renderEdges(layout);
}

function renderEdges(layout) {
    let html = '';
    
    state.edges.forEach(edge => {
        // Use custom position if dragged
        const fromPos = customPositions.get(edge.from) || layout.get(edge.from);
        const toPos = customPositions.get(edge.to) || layout.get(edge.to);
        
        if (!fromPos || !toPos) return;
        
        const fromNode = state.nodes.get(edge.from);
        const isActive = fromNode?.status === 'running';
        
        // 起点：右侧锚点
        const x1 = fromPos.x + fromPos.width;
        const y1 = fromPos.y + fromPos.height / 2;
        // 终点：左侧锚点
        const x2 = toPos.x;
        const y2 = toPos.y + toPos.height / 2;
        
        // 贝塞尔曲线控制点
        const dx = Math.abs(x2 - x1);
        const ctrlOffset = Math.max(40, dx * 0.4);
        
        const path = `M ${x1} ${y1} C ${x1 + ctrlOffset} ${y1}, ${x2 - ctrlOffset} ${y2}, ${x2} ${y2}`;
        
        html += `
            <path class="graph-edge ${isActive ? 'active' : ''} ${edge.type}"
                  d="${path}"
                  marker-end="url(#arrowhead${isActive ? '-active' : ''})" />
        `;
    });
    
    return html;
}

function updateTransform() {
    const nodesLayer = document.getElementById('nodes-layer');
    const edgesLayer = document.getElementById('edges-layer');
    
    if (nodesLayer && edgesLayer) {
        const transform = `translate(${state.panX}, ${state.panY}) scale(${state.zoom})`;
        nodesLayer.setAttribute('transform', transform);
        edgesLayer.setAttribute('transform', transform);
    }
    
    document.getElementById('zoom-level').textContent = `${Math.round(state.zoom * 100)}%`;
}

// ─────────────────────────────────────────────────────────────────────────────
//  Zoom Controls
// ─────────────────────────────────────────────────────────────────────────────

function zoomGraph(delta, centerX, centerY) {
    const oldZoom = state.zoom;
    state.zoom = Math.max(0.2, Math.min(3, state.zoom + delta));
    
    // Zoom towards cursor position
    if (centerX !== undefined) {
        const svg = document.getElementById('graph-svg');
        const rect = svg.getBoundingClientRect();
        const x = centerX - rect.left;
        const y = centerY - rect.top;
        
        state.panX = x - (x - state.panX) * (state.zoom / oldZoom);
        state.panY = y - (y - state.panY) * (state.zoom / oldZoom);
    }
    
    updateTransform();
}

function resetZoom() {
    state.zoom = 1;
    state.panX = 0;
    state.panY = 0;
    updateTransform();
}

function fitToScreen() {
    // TODO: Calculate bounds and fit
    resetZoom();
}

function setLayout(layout) {
    state.layout = layout;
    
    // Clear custom positions when changing layout
    customPositions.clear();
    
    // Update buttons
    document.querySelectorAll('.layout-btn').forEach(btn => {
        btn.classList.toggle('active', btn.dataset.layout === layout);
    });
    
    // Re-render
    renderGraph();
}

// Reset all nodes to calculated positions
function resetNodePositions() {
    customPositions.clear();
    renderGraph();
}

// ─────────────────────────────────────────────────────────────────────────────
//  Node Selection & Detail
// ─────────────────────────────────────────────────────────────────────────────

function selectNode(nodeId) {
    state.selectedNodeId = nodeId;
    renderGraph();
    showNodeDetail(nodeId);
}

function showNodeDetail(nodeId) {
    const node = state.nodes.get(nodeId);
    if (!node) return;
    
    const config = NODE_CONFIG[node.type] || NODE_CONFIG.llm_call;
    document.getElementById('detail-title').textContent = `${config.icon} ${node.id}`;
    
    let html = `
        <div class="detail-section">
            <div class="detail-section-title">Status</div>
            <div class="detail-grid">
                <div class="detail-item">
                    <div class="detail-item-label">Status</div>
                    <div class="detail-item-value" style="color:var(--status-${node.status})">${node.status}</div>
                </div>
                <div class="detail-item">
                    <div class="detail-item-label">Progress</div>
                    <div class="detail-item-value">${Math.round((node.progress || 0) * 100)}%</div>
                </div>
                <div class="detail-item">
                    <div class="detail-item-label">Duration</div>
                    <div class="detail-item-value">${node.durationMs ? (node.durationMs / 1000).toFixed(1) + 's' : '-'}</div>
                </div>
                <div class="detail-item">
                    <div class="detail-item-label">Type</div>
                    <div class="detail-item-value">${config.label}</div>
                </div>
            </div>
        </div>
    `;
    
    // Vote specific
    if (node.type === 'vote' && node.voteRound) {
        html += `
            <div class="detail-section">
                <div class="detail-section-title">Voting</div>
                <div class="detail-grid">
                    <div class="detail-item">
                        <div class="detail-item-label">Round</div>
                        <div class="detail-item-value">${node.voteRound}/${node.voteMaxRounds || '?'}</div>
                    </div>
                    <div class="detail-item">
                        <div class="detail-item-label">Votes</div>
                        <div class="detail-item-value">${node.voteCurrentVotes || 0}/${node.voteK || '?'}</div>
                    </div>
                </div>
            </div>
        `;
    }
    
    // Fan-out specific
    if (node.type === 'fan_out' && node.parallelTotal) {
        html += `
            <div class="detail-section">
                <div class="detail-section-title">Parallel Execution</div>
                <div class="detail-grid">
                    <div class="detail-item">
                        <div class="detail-item-label">Completed</div>
                        <div class="detail-item-value">${node.parallelCompleted || 0}/${node.parallelTotal}</div>
                    </div>
                    <div class="detail-item">
                        <div class="detail-item-label">Failed</div>
                        <div class="detail-item-value" style="color:var(--status-failed)">${node.parallelFailed || 0}</div>
                    </div>
                </div>
            </div>
        `;
    }
    
    // Stats
    if (node.llmCalls || node.tokensUsed) {
        html += `
            <div class="detail-section">
                <div class="detail-section-title">Stats</div>
                <div class="detail-grid">
                    <div class="detail-item">
                        <div class="detail-item-label">LLM Calls</div>
                        <div class="detail-item-value">${node.llmCalls || 0}</div>
                    </div>
                    <div class="detail-item">
                        <div class="detail-item-label">Tokens</div>
                        <div class="detail-item-value">${formatNumber(node.tokensUsed || 0)}</div>
                    </div>
                </div>
            </div>
        `;
    }
    
    // Conversation
    if (node.systemPrompt || node.userPrompt || node.assistantResponse) {
        html += `
            <div class="detail-section">
                <div class="detail-section-title">Conversation</div>
                <div class="detail-conversation">
                    ${node.systemPrompt ? `
                        <div class="conv-message">
                            <div class="conv-role system">System</div>
                            <div class="conv-content">${escapeHtml(node.systemPrompt)}</div>
                        </div>
                    ` : ''}
                    ${node.userPrompt ? `
                        <div class="conv-message">
                            <div class="conv-role user">User</div>
                            <div class="conv-content">${escapeHtml(node.userPrompt)}</div>
                        </div>
                    ` : ''}
                    ${node.assistantResponse ? `
                        <div class="conv-message">
                            <div class="conv-role assistant">Assistant</div>
                            <div class="conv-content">${escapeHtml(node.assistantResponse)}</div>
                        </div>
                    ` : ''}
                </div>
            </div>
        `;
    }
    
    // Message
    if (node.message) {
        html += `
            <div class="detail-section">
                <div class="detail-section-title">Message</div>
                <div class="detail-item">
                    <div class="detail-item-value">${escapeHtml(node.message)}</div>
                </div>
            </div>
        `;
    }
    
    document.getElementById('detail-content').innerHTML = html;
}

function closeDetail() {
    state.selectedNodeId = null;
    renderGraph();
    document.getElementById('detail-title').textContent = 'Select a Node';
    document.getElementById('detail-content').innerHTML = `
        <div class="detail-placeholder">
            Click a node in the graph to view details
        </div>
    `;
}

// ─────────────────────────────────────────────────────────────────────────────
//  Sidebar Tabs
// ─────────────────────────────────────────────────────────────────────────────

function switchTab(tabName) {
    // Update tab buttons
    document.querySelectorAll('.tab-btn').forEach(btn => {
        btn.classList.toggle('active', btn.dataset.tab === tabName);
    });
    
    // Update tab content
    document.querySelectorAll('.tab-content').forEach(content => {
        content.classList.toggle('active', content.id === `tab-${tabName}`);
    });
    
    // Refresh conversation stream when switching to it
    if (tabName === 'conversation') {
        renderConversationStream();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Conversation Stream
// ─────────────────────────────────────────────────────────────────────────────

function renderConversationStream() {
    const container = document.getElementById('conversation-stream');
    
    // Collect all nodes with conversation data
    const conversations = [];
    state.nodes.forEach((node, id) => {
        if (node.systemPrompt || node.userPrompt || node.assistantResponse) {
            conversations.push({
                id: id,
                type: node.type,
                status: node.status,
                timestamp: node.timestamp,
                systemPrompt: node.systemPrompt,
                userPrompt: node.userPrompt,
                assistantResponse: node.assistantResponse
            });
        }
    });
    
    // Sort by timestamp
    conversations.sort((a, b) => (a.timestamp || 0) - (b.timestamp || 0));
    
    if (conversations.length === 0) {
        container.innerHTML = `
            <div class="conversation-empty">
                <div class="empty-icon">💬</div>
                <div class="empty-text">LLM conversations will appear here as the workflow runs</div>
            </div>
        `;
        return;
    }
    
    let html = '';
    conversations.forEach((conv, idx) => {
        const config = NODE_CONFIG[conv.type] || NODE_CONFIG.llm_call;
        const isExpanded = idx === conversations.length - 1; // Expand latest by default
        
        html += `
            <div class="conv-card ${isExpanded ? 'expanded' : ''}" data-conv-id="${conv.id}">
                <div class="conv-card-header" onclick="toggleConvCard('${conv.id}')">
                    <div class="conv-card-title">
                        <span class="step-icon">${config.icon}</span>
                        <span>${conv.id}</span>
                    </div>
                    <div class="conv-card-meta">
                        <span class="status-badge ${conv.status}">${conv.status}</span>
                    </div>
                </div>
                <div class="conv-card-body">
                    ${conv.systemPrompt ? `
                        <div class="chat-message">
                            <div class="chat-role system">⚙️ System</div>
                            <div class="chat-text">${escapeHtml(conv.systemPrompt)}</div>
                        </div>
                    ` : ''}
                    ${conv.userPrompt ? `
                        <div class="chat-message">
                            <div class="chat-role user">👤 User</div>
                            <div class="chat-text">${escapeHtml(conv.userPrompt)}</div>
                        </div>
                    ` : ''}
                    ${conv.assistantResponse ? `
                        <div class="chat-message">
                            <div class="chat-role assistant">🤖 Assistant</div>
                            <div class="chat-text">${escapeHtml(conv.assistantResponse)}</div>
                        </div>
                    ` : `
                        <div class="chat-message">
                            <div class="chat-role assistant">🤖 Assistant</div>
                            <div class="chat-text thinking">Thinking...</div>
                        </div>
                    `}
                </div>
            </div>
        `;
    });
    
    container.innerHTML = html;
    
    // Auto-scroll to bottom
    container.scrollTop = container.scrollHeight;
}

function toggleConvCard(convId) {
    const card = document.querySelector(`.conv-card[data-conv-id="${convId}"]`);
    if (card) {
        card.classList.toggle('expanded');
    }
}

// Update conversation stream when nodes change
function updateConversationIfVisible() {
    const convTab = document.getElementById('tab-conversation');
    if (convTab && convTab.classList.contains('active')) {
        renderConversationStream();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Tooltip
// ─────────────────────────────────────────────────────────────────────────────

function showTooltip(event, nodeId) {
    const node = state.nodes.get(nodeId);
    if (!node) return;
    
    const config = NODE_CONFIG[node.type] || NODE_CONFIG.llm_call;
    const tooltip = document.getElementById('node-tooltip');
    
    tooltip.innerHTML = `
        <div class="tooltip-header">
            <span>${config.icon}</span>
            <strong>${node.id}</strong>
        </div>
        <div class="tooltip-stats">
            <span>Status:</span><span style="color:var(--status-${node.status})">${node.status}</span>
            <span>Progress:</span><span>${Math.round((node.progress || 0) * 100)}%</span>
            ${node.llmCalls ? `<span>LLM Calls:</span><span>${node.llmCalls}</span>` : ''}
            ${node.tokensUsed ? `<span>Tokens:</span><span>${formatNumber(node.tokensUsed)}</span>` : ''}
        </div>
    `;
    
    // Position tooltip near cursor with small offset
    const offsetX = 12;
    const offsetY = 8;
    
    // Ensure tooltip doesn't go off screen
    const tooltipRect = tooltip.getBoundingClientRect();
    let left = event.clientX + offsetX;
    let top = event.clientY + offsetY;
    
    // Adjust if going off right edge
    if (left + 200 > window.innerWidth) {
        left = event.clientX - offsetX - 200;
    }
    
    // Adjust if going off bottom edge
    if (top + 100 > window.innerHeight) {
        top = event.clientY - offsetY - 100;
    }
    
    tooltip.style.left = `${left}px`;
    tooltip.style.top = `${top}px`;
    tooltip.classList.remove('hidden');
}

function hideTooltip() {
    document.getElementById('node-tooltip').classList.add('hidden');
}

// ─────────────────────────────────────────────────────────────────────────────
//  Structure Tree
// ─────────────────────────────────────────────────────────────────────────────

function updateStructureTree() {
    const container = document.getElementById('structure-tree');
    
    // Group nodes by parent
    const roots = [];
    const children = new Map();
    
    state.nodes.forEach((node, id) => {
        if (node.parentId) {
            if (!children.has(node.parentId)) children.set(node.parentId, []);
            children.get(node.parentId).push(node);
        } else if (!id.includes('.gen[')) {
            roots.push(node);
        }
    });
    
    // Sort by timestamp
    roots.sort((a, b) => (a.timestamp || 0) - (b.timestamp || 0));
    
    container.innerHTML = renderTreeNodes(roots, children);
}

function renderTreeNodes(nodes, children) {
    return nodes.map(node => {
        const config = NODE_CONFIG[node.type] || NODE_CONFIG.llm_call;
        const nodeChildren = children.get(node.id) || [];
        const isActive = state.selectedNodeId === node.id;
        
        return `
            <div class="tree-node ${isActive ? 'active' : ''}" onclick="selectNode('${node.id}')">
                <span class="tree-icon">${config.icon}</span>
                <span class="tree-label">${node.id}</span>
                <span class="tree-status" style="background:var(--status-${node.status})"></span>
            </div>
            ${nodeChildren.length > 0 ? `
                <div class="tree-children">
                    ${renderTreeNodes(nodeChildren.sort((a, b) => (a.timestamp || 0) - (b.timestamp || 0)), children)}
                </div>
            ` : ''}
        `;
    }).join('');
}

function toggleStructure() {
    const content = document.getElementById('structure-tree');
    content.style.display = content.style.display === 'none' ? 'block' : 'none';
}

// ─────────────────────────────────────────────────────────────────────────────
//  Stats
// ─────────────────────────────────────────────────────────────────────────────

function updateStats() {
    const nodes = Array.from(state.nodes.values());
    
    const completed = nodes.filter(n => n.status === 'completed').length;
    const total = nodes.filter(n => !n.id.includes('.gen[')).length; // Exclude sub-steps
    
    document.getElementById('stat-completed').textContent = completed;
    document.getElementById('stat-total').textContent = total;
    
    // Running status
    const hasRunning = nodes.some(n => n.status === 'running');
    const hasFailed = nodes.some(n => n.status === 'failed');
    
    document.getElementById('stat-status').textContent = 
        hasFailed ? 'Failed' :
        hasRunning ? 'Running' :
        completed === total && total > 0 ? 'Completed' : 'Pending';
    
    document.getElementById('stat-status').style.color = 
        hasFailed ? 'var(--status-failed)' :
        hasRunning ? 'var(--status-running)' :
        completed === total && total > 0 ? 'var(--status-completed)' : 'var(--wf-muted)';
    
    // Total stats
    const totalLlm = nodes.reduce((sum, n) => sum + (n.llmCalls || 0), 0);
    const totalTokens = nodes.reduce((sum, n) => sum + (n.tokensUsed || 0), 0);
    
    document.getElementById('stat-llm-calls').textContent = totalLlm;
    document.getElementById('stat-tokens').textContent = formatNumber(totalTokens);
}

// ─────────────────────────────────────────────────────────────────────────────
//  Utilities
// ─────────────────────────────────────────────────────────────────────────────

function formatNumber(n) {
    if (n >= 1000000) return (n / 1000000).toFixed(1) + 'M';
    if (n >= 1000) return (n / 1000).toFixed(1) + 'K';
    return n.toString();
}

function truncate(str, maxLen) {
    if (!str) return '';
    return str.length > maxLen ? str.slice(0, maxLen) + '...' : str;
}

function escapeHtml(str) {
    if (!str) return '';
    return str
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#039;');
}

