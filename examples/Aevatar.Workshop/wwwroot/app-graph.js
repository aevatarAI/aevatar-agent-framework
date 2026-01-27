// ============================================================
//  app-graph.js
//
//  中文 + ASCII:
//  - 统一绘制 DAG/层级图
//  - 支持平移/缩放
// ============================================================

function renderGraph(graph, container = el.workflowGraph, graphState = state.graph) {
  if (!container) return;
  if (!graph) {
    container.innerHTML = '<div class="panel-subtitle">No graph loaded.</div>';
    return;
  }

  const nodes = graph.nodes || [];
  const edges = graph.edges || [];
  const nodeWidth = 170;
  const nodeHeight = 72;
  const colGap = 90;
  const rowGap = 36;
  const padding = 56;
  const palette = ['#7dd3fc', '#fcd34d', '#86efac', '#c4b5fd', '#fca5a5'];

  const levels = {};
  nodes.forEach((node) => {
    const depth = node.depth || 0;
    if (!levels[depth]) levels[depth] = [];
    levels[depth].push(node);
  });

  const maxDepth = Math.max(0, ...nodes.map((n) => n.depth || 0));
  const maxRows = Math.max(1, ...Object.values(levels).map((arr) => arr.length));
  const width = padding * 2 + (maxDepth + 1) * nodeWidth + maxDepth * colGap;
  const height = padding * 2 + maxRows * nodeHeight + (maxRows - 1) * rowGap;

  container.innerHTML = '';
  const stage = document.createElement('div');
  stage.className = 'graph-stage';

  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('class', 'graph-edges');
  svg.setAttribute('width', width);
  svg.setAttribute('height', height);
  svg.setAttribute('viewBox', `0 0 ${width} ${height}`);

  const defs = document.createElementNS('http://www.w3.org/2000/svg', 'defs');
  const edgeGradient = document.createElementNS('http://www.w3.org/2000/svg', 'linearGradient');
  edgeGradient.setAttribute('id', 'edgeGradient');
  edgeGradient.setAttribute('x1', '0%');
  edgeGradient.setAttribute('y1', '0%');
  edgeGradient.setAttribute('x2', '100%');
  edgeGradient.setAttribute('y2', '0%');
  const stopA = document.createElementNS('http://www.w3.org/2000/svg', 'stop');
  stopA.setAttribute('offset', '0%');
  stopA.setAttribute('stop-color', '#7dd3fc');
  stopA.setAttribute('stop-opacity', '0.2');
  const stopB = document.createElementNS('http://www.w3.org/2000/svg', 'stop');
  stopB.setAttribute('offset', '50%');
  stopB.setAttribute('stop-color', '#7dd3fc');
  stopB.setAttribute('stop-opacity', '0.7');
  const stopC = document.createElementNS('http://www.w3.org/2000/svg', 'stop');
  stopC.setAttribute('offset', '100%');
  stopC.setAttribute('stop-color', '#7dd3fc');
  stopC.setAttribute('stop-opacity', '0.2');
  edgeGradient.appendChild(stopA);
  edgeGradient.appendChild(stopB);
  edgeGradient.appendChild(stopC);
  defs.appendChild(edgeGradient);

  const marker = document.createElementNS('http://www.w3.org/2000/svg', 'marker');
  marker.setAttribute('id', 'arrow');
  marker.setAttribute('markerWidth', '10');
  marker.setAttribute('markerHeight', '10');
  marker.setAttribute('refX', '6');
  marker.setAttribute('refY', '3');
  marker.setAttribute('orient', 'auto');
  const arrowPath = document.createElementNS('http://www.w3.org/2000/svg', 'path');
  arrowPath.setAttribute('d', 'M0,0 L0,6 L6,3 z');
  arrowPath.setAttribute('fill', '#7dd3fc');
  marker.appendChild(arrowPath);
  defs.appendChild(marker);
  svg.appendChild(defs);

  stage.appendChild(svg);

  const positions = {};
  Object.keys(levels).forEach((depthKey) => {
    const depth = Number(depthKey);
    const list = levels[depth];
    list.forEach((node, index) => {
      const x = padding + depth * (nodeWidth + colGap);
      const y = padding + index * (nodeHeight + rowGap);
      positions[node.id] = { x, y };

      const div = document.createElement('div');
      div.className = 'graph-node';
      div.dataset.nodeId = node.id;
      div.style.left = `${x}px`;
      div.style.top = `${y}px`;
      div.style.width = `${nodeWidth}px`;
      div.style.height = `${nodeHeight}px`;
      const accent = palette[depth % palette.length];
      div.style.setProperty('--node-accent', accent);
      div.style.setProperty('--node-accent-soft', hexToRgba(accent, 0.2));
      div.style.setProperty('--node-accent-glow', hexToRgba(accent, 0.45));
      const label = node.label || node.id;
      const subtitle = label !== node.id ? label : (node.type || '');
      div.innerHTML = `<div class="node-header">
          <span class="node-dot"></span>
          <div class="node-title">${escapeHtml(node.id)}</div>
        </div>
        <div class="node-subtitle">${escapeHtml(subtitle)}</div>`;
      stage.appendChild(div);
    });
  });

  edges.forEach((edge) => {
    const from = positions[edge.from];
    const to = positions[edge.to];
    if (!from || !to) return;
    const startX = from.x + nodeWidth;
    const startY = from.y + nodeHeight / 2;
    const endX = to.x;
    const endY = to.y + nodeHeight / 2;
    const midX = (startX + endX) / 2;

    const glowPath = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    glowPath.setAttribute('d', `M${startX},${startY} C${midX},${startY} ${midX},${endY} ${endX},${endY}`);
    glowPath.setAttribute('fill', 'none');
    glowPath.setAttribute('stroke', '#7dd3fc');
    glowPath.setAttribute('stroke-width', '5');
    glowPath.setAttribute('stroke-opacity', '0.18');
    glowPath.setAttribute('stroke-linecap', 'round');
    svg.appendChild(glowPath);

    const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    path.setAttribute('d', `M${startX},${startY} C${midX},${startY} ${midX},${endY} ${endX},${endY}`);
    path.setAttribute('fill', 'none');
    path.setAttribute('stroke', '#7dd3fc');
    path.setAttribute('stroke-width', '1.6');
    path.setAttribute('stroke-opacity', '0.9');
    path.setAttribute('stroke-linecap', 'round');
    path.style.stroke = 'url(#edgeGradient)';
    path.setAttribute('marker-end', 'url(#arrow)');
    svg.appendChild(path);
  });

  stage.style.width = `${width}px`;
  stage.style.height = `${height}px`;
  container.appendChild(stage);

  graphState.stage = stage;
  graphState.scale = 1;
  const rect = container.getBoundingClientRect();
  graphState.offsetX = Math.max(0, (rect.width - width) / 2);
  graphState.offsetY = Math.max(0, (rect.height - height) / 2);
  applyGraphTransform(graphState);
  bindGraphInteractions(container, graphState);
}

function applyGraphTransform(graphState = state.graph) {
  if (!graphState.stage) return;
  graphState.stage.style.transform = `translate(${graphState.offsetX}px, ${graphState.offsetY}px) scale(${graphState.scale})`;
}

function bindGraphInteractions(container, graphState = state.graph) {
  if (!container || graphState.bound) return;
  graphState.bound = true;

  container.addEventListener('wheel', (e) => {
    e.preventDefault();
    const rect = container.getBoundingClientRect();
    const cx = e.clientX - rect.left;
    const cy = e.clientY - rect.top;
    const delta = e.deltaY > 0 ? -0.1 : 0.1;
    const nextScale = Math.min(2.5, Math.max(0.4, graphState.scale + delta));
    const scaleFactor = nextScale / graphState.scale;
    graphState.offsetX = cx - (cx - graphState.offsetX) * scaleFactor;
    graphState.offsetY = cy - (cy - graphState.offsetY) * scaleFactor;
    graphState.scale = nextScale;
    applyGraphTransform(graphState);
  }, { passive: false });

  container.addEventListener('mousedown', (e) => {
    graphState.isPanning = true;
    graphState.startX = e.clientX - graphState.offsetX;
    graphState.startY = e.clientY - graphState.offsetY;
    container.classList.add('grabbing');
  });

  window.addEventListener('mousemove', (e) => {
    if (!graphState.isPanning) return;
    graphState.offsetX = e.clientX - graphState.startX;
    graphState.offsetY = e.clientY - graphState.startY;
    applyGraphTransform(graphState);
  });

  window.addEventListener('mouseup', () => {
    graphState.isPanning = false;
    container.classList.remove('grabbing');
  });
}
