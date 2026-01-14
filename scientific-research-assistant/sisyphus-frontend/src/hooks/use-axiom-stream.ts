import { useEffect, useRef, useCallback } from "react"
import { useSisyphusStore } from "@/store/sisyphus-store"
import { createAxiomEventStream, getToolsSnapshot, getDagSnapshot } from "@/lib/axiom-client"
import type { EventStream } from "@aevatar/kit-protocol"
import { parseMessageId } from "@aevatar/kit-protocol"
import type { ToolOutput } from "@/types"

// ============================================================================
//  Axiom Event Stream Hook
//  Connects to AxiomReasoning backend via @aevatar/kit-protocol
// ============================================================================

interface UseAxiomStreamOptions {
  sessionId: string | null
  enabled?: boolean
}

export function useAxiomStream({ sessionId, enabled = true }: UseAxiomStreamOptions) {
  const streamRef = useRef<EventStream | null>(null)
  
  const {
    setConnected,
    updateSession,
    updateWorker,
    appendWorkerHistory,
    addMessage,
    updateStats,
    addRawEvent,
    resetForNewSession,
    setTools,
    setCurrentRun,
    updateAgentMessage,
    setAgentRoster,
    setAgentProviders,
  } = useSisyphusStore()

  // Tool outputs state (per-message)
  const toolOutputsRef = useRef<Map<string, Map<string, ToolOutput>>>(new Map())

  // Upsert tool output for a message
  const upsertToolOutput = useCallback((messageId: string, tool: ToolOutput) => {
    if (!toolOutputsRef.current.has(messageId)) {
      toolOutputsRef.current.set(messageId, new Map())
    }
    const msgTools = toolOutputsRef.current.get(messageId)!
    const existing = msgTools.get(tool.toolCallId)
    msgTools.set(tool.toolCallId, { ...existing, ...tool })
  }, [])

  // Handle worker streaming content
  const appendWorkerStream = useCallback((workerId: string, delta: string) => {
    useSisyphusStore.setState((state) => {
      const worker = state.workers[workerId]
      if (!worker) return state
      return {
        workers: {
          ...state.workers,
          [workerId]: {
            ...worker,
            streamContent: (worker.streamContent || "") + delta,
          },
        },
      }
    })
  }, [])

  useEffect(() => {
    if (!sessionId || !enabled) {
      setConnected(false)
      return
    }

    // Clean up previous stream
    if (streamRef.current) {
      streamRef.current.disconnect()
      streamRef.current = null
    }

    // Reset state for new session
    resetForNewSession()

    // Create new stream
    const stream = createAxiomEventStream(sessionId)
    streamRef.current = stream

    // === Status Change ===
    // Note: stream.onStatusChange may not work as expected with @aevatar/kit-protocol
    // We'll set connected = true when we receive the first event
    stream.onStatusChange((status) => {
      console.log(`[useAxiomStream] Status changed: ${status}`)
      setConnected(status === "connected")
    })

    // Fallback: Set connected when we receive any event (first event = connected)
    let hasReceivedEvent = false
    const markConnected = () => {
      if (!hasReceivedEvent) {
        hasReceivedEvent = true
        console.log(`[useAxiomStream] First event received, marking connected`)
        setConnected(true)
      }
    }

    // === Standard AG-UI Events ===

    // Messages Snapshot - marks connection as established
    stream.on("MESSAGES_SNAPSHOT", (event) => {
      markConnected()
      addRawEvent(event)
      console.log("[AxiomStream] MESSAGES_SNAPSHOT received")
    })

    // Run Started
    stream.on("RUN_STARTED", (event) => {
      markConnected()
      addRawEvent(event)
      
      // Extract runId from event
      const runId = (event as { runId?: string }).runId || `run-${Date.now()}`
      setCurrentRun(runId)
      
      addMessage({
        role: "agent",
        content: "Session started. Initializing research pipeline...",
        agentId: "sisyphus",
        agentName: "Sisyphus",
      })
    })

    // Run Finished
    stream.on("RUN_FINISHED", (event) => {
      addRawEvent(event)
      updateSession({
        id: sessionId,
        status: "completed",
        progressPercent: 100,
      })
      addMessage({
        role: "agent",
        content: "Research complete! View the results below.",
        agentId: "sisyphus",
        agentName: "Sisyphus",
      })
    })

    // Run Error
    stream.on("RUN_ERROR", (event) => {
      addRawEvent(event)
      updateSession({
        id: sessionId,
        status: "failed",
      })
      addMessage({
        role: "agent",
        content: `Error: ${(event as { message?: string }).message || "Unknown error"}`,
        agentId: "sisyphus",
        agentName: "Sisyphus",
      })
    })

    // Text Message Start
    stream.on("TEXT_MESSAGE_START", (event) => {
      addRawEvent(event)
      const parsed = parseMessageId(event.messageId)
      
      // Update worker
      updateWorker({
        id: parsed.workerId,
        name: parsed.workerId === "coordinator" ? "Coordinator" : `Worker ${parsed.workerId}`,
        status: "streaming",
        streaming: true,
        stepId: parsed.stepId,
        streamContent: "",
      })
      
      // Also update agent message if this is an agent (not worker-N)
      // messageId format: msg:sessionId:agent:runId
      const parts = event.messageId.split(":")
      if (parts.length >= 4) {
        const agent = parts[2]
        // Check if it's an agent name (not "user" or "worker-*")
        if (agent && agent !== "user" && !agent.startsWith("worker")) {
          const agentName = agent === "assistant" ? "research_assistant" : agent
          updateAgentMessage(agentName, {
            agent: agentName,
            isStreaming: true,
            isFinal: false,
            content: "", // Will accumulate via TEXT_MESSAGE_CONTENT
          })
        }
      }
    })

    // Text Message Content
    stream.on("TEXT_MESSAGE_CONTENT", (event) => {
      addRawEvent(event)
      const parsed = parseMessageId(event.messageId)
      appendWorkerStream(parsed.workerId, event.delta)
      
      // Also update agent message
      const parts = event.messageId.split(":")
      if (parts.length >= 4) {
        const agent = parts[2]
        if (agent && agent !== "user" && !agent.startsWith("worker")) {
          const agentName = agent === "assistant" ? "research_assistant" : agent
          updateAgentMessage(agentName, {
            agent: agentName,
            isStreaming: true,
            content: event.delta, // Will be accumulated by store
          })
        }
      }
    })

    // Text Message End
    stream.on("TEXT_MESSAGE_END", (event) => {
      addRawEvent(event)
      const parsed = parseMessageId(event.messageId)
      
      // Get final content and move to lastResponse
      const state = useSisyphusStore.getState()
      const worker = state.workers[parsed.workerId]
      const finalContent = worker?.streamContent || ""
      
      updateWorker({
        id: parsed.workerId,
        status: "completed",
        streaming: false,
        lastResponse: finalContent,
        streamContent: "",
      })
      
      // Also finalize agent message
      const parts = event.messageId.split(":")
      if (parts.length >= 4) {
        const agent = parts[2]
        if (agent && agent !== "user" && !agent.startsWith("worker")) {
          const agentName = agent === "assistant" ? "research_assistant" : agent
          updateAgentMessage(agentName, {
            agent: agentName,
            isStreaming: false,
            isFinal: true,
          })
        }
      }
    })

    // Step Started
    stream.on("STEP_STARTED", (event) => {
      addRawEvent(event)
      updateWorker({
        id: event.stepName || "coordinator",
        name: event.stepName || "Coordinator",
        status: "running",
        stepType: event.stepName,
      })
    })

    // Step Finished
    stream.on("STEP_FINISHED", (event) => {
      addRawEvent(event)
      updateWorker({
        id: (event as { stepName?: string }).stepName || "coordinator",
        status: "completed",
      })
    })

    // Tool Call Start
    stream.on("TOOL_CALL_START", (event) => {
      addRawEvent(event)
      const toolEvent = event as { messageId?: string; toolCallId?: string; toolName?: string }
      if (toolEvent.messageId && toolEvent.toolCallId && toolEvent.toolName) {
        upsertToolOutput(toolEvent.messageId, {
          toolCallId: toolEvent.toolCallId,
          name: toolEvent.toolName,
          status: "running",
        })
      }
      updateWorker({
        id: event.toolCallId || "tool",
        name: event.toolName || "Tool",
        status: "running",
        stepType: "tool_call",
      })
    })

    // Tool Call Result
    stream.on("TOOL_CALL_RESULT", (event) => {
      addRawEvent(event)
      const toolEvent = event as { messageId?: string; toolCallId?: string; result?: string }
      if (toolEvent.messageId && toolEvent.toolCallId) {
        const msgTools = toolOutputsRef.current.get(toolEvent.messageId)
        const existing = msgTools?.get(toolEvent.toolCallId)
        if (existing) {
          upsertToolOutput(toolEvent.messageId, {
            ...existing,
            resultPreview: toolEvent.result,
          })
        }
      }
    })

    // Tool Call End
    stream.on("TOOL_CALL_END", (event) => {
      addRawEvent(event)
      const toolEvent = event as { messageId?: string; toolCallId?: string }
      if (toolEvent.messageId && toolEvent.toolCallId) {
        const msgTools = toolOutputsRef.current.get(toolEvent.messageId)
        const existing = msgTools?.get(toolEvent.toolCallId)
        if (existing) {
          upsertToolOutput(toolEvent.messageId, {
            ...existing,
            status: "done",
            success: existing.success ?? true,
          })
        }
      }
      updateWorker({
        id: event.toolCallId || "tool",
        status: "completed",
      })
    })

    // State Snapshot - Extract workers and DAG from snapshot
    stream.on("STATE_SNAPSHOT", (event) => {
      markConnected()
      addRawEvent(event)
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const snapshot = (event as any).snapshot as Record<string, unknown> | undefined
      console.log("[AxiomStream] STATE_SNAPSHOT snapshot:", snapshot) // Debug
      if (snapshot) {
        // Extract workers from snapshot
        if (snapshot.workers && Array.isArray(snapshot.workers)) {
          const workersList = snapshot.workers as Array<{
            id: string
            name?: string
            status?: string
            provider?: string
            tokenIndex?: number
            streaming?: boolean
            streamContent?: string
            stepId?: string
            stepType?: string
            history?: unknown[]
          }>
          workersList.forEach(w => {
            updateWorker({
              id: w.id,
              name: w.name || w.id,
              status: (w.status as "pending" | "running" | "completed" | "streaming" | "error") || "completed",
              provider: w.provider,
              tokenIndex: w.tokenIndex,
              streaming: w.streaming,
              streamContent: w.streamContent,
              stepId: w.stepId,
              stepType: w.stepType,
            })
          })
        }
        
        // Extract DAG from snapshot if available
        if (snapshot.graph) {
          const graphData = snapshot.graph as { nodes?: unknown[]; edges?: unknown[] }
          if (graphData.nodes && graphData.edges) {
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            useSisyphusStore.getState().setDag(graphData as any)
          }
        }
      }
    })

    // === Aevatar Extension Events ===

    // Progress Event
    stream.onAevatar("aevatar.progress", (event) => {
      addRawEvent(event)
      const progress = event.value as {
        phase?: string
        progressPercent?: number
        stepId?: string
        stepType?: string
        workerId?: string
        message?: string
      }
      
      updateSession({
        id: sessionId,
        phase: progress.phase || "",
        progressPercent: progress.progressPercent || 0,
        status: "running",
      })

      if (progress.workerId) {
        updateWorker({
          id: progress.workerId,
          stepId: progress.stepId,
          stepType: progress.stepType,
          status: "running",
        })
      }

      updateStats({
        computeLoad: (progress.progressPercent || 0) / 100,
      })
    })

    // Graph Event
    stream.onAevatar("aevatar.graph", (event) => {
      addRawEvent(event)
      // Could update DAG visualization here
    })

    // Worker Started
    stream.onAevatar("aevatar.worker_started", (event) => {
      addRawEvent(event)
      const data = event.value as { workerId?: string; taskDescription?: string }
      if (data.workerId) {
        updateWorker({
          id: data.workerId,
          status: "running",
          lastResponse: data.taskDescription || "",
        })
      }
    })

    // Worker Completed
    stream.onAevatar("aevatar.worker_completed", (event) => {
      addRawEvent(event)
      const data = event.value as { workerId?: string; tokensUsed?: number }
      if (data.workerId) {
        updateWorker({
          id: data.workerId,
          status: "completed",
          tokenIndex: data.tokensUsed || 0,
        })
      }
    })

    // Custom: Status Snapshot (AxiomReasoning specific)
    stream.onCustom("aevatar.axiom.status_snapshot", (event) => {
      addRawEvent(event)
      const snapshot = event.value as {
        status?: string
        phase?: string
        progressPercent?: number
        totalTokens?: number
        totalLlmCalls?: number
      }
      
      // Validate status is one of the expected values
      const validStatuses = ["running", "pending", "completed", "failed"] as const
      const status = validStatuses.includes(snapshot.status as typeof validStatuses[number])
        ? (snapshot.status as "running" | "pending" | "completed" | "failed")
        : "running"
      
      updateSession({
        id: sessionId,
        status,
        phase: snapshot.phase || "",
        progressPercent: snapshot.progressPercent || 0,
        totalTokens: snapshot.totalTokens || 0,
        totalLlmCalls: snapshot.totalLlmCalls || 0,
      })
    })

    // === Aevatar Scientific Custom Events ===

    // Tools Snapshot
    stream.onCustom("aevatar.scientific.tools_snapshot", (event) => {
      addRawEvent(event)
      const data = event.value as { tools?: unknown[] }
      if (Array.isArray(data?.tools)) {
        setTools(data.tools as typeof useSisyphusStore.getState extends () => { tools: infer T } ? T : never)
      }
    })

    // MCP Reconnect Events
    stream.onCustom("aevatar.scientific.mcp_reconnect_started", (event) => {
      addRawEvent(event)
      addMessage({
        role: "system",
        content: "🔄 Reconnecting MCP servers...",
      })
    })

    stream.onCustom("aevatar.scientific.mcp_reconnect_finished", (event) => {
      addRawEvent(event)
      addMessage({
        role: "system",
        content: "✅ MCP servers reconnected",
      })
      // Refresh tools after MCP reconnect
      void (async () => {
        try {
          const res = await getToolsSnapshot(sessionId)
          setTools(res.tools || [])
        } catch { /* best-effort */ }
      })()
    })

    stream.onCustom("aevatar.scientific.mcp_reconnect_error", (event) => {
      addRawEvent(event)
      const data = event.value as { error?: string }
      addMessage({
        role: "system",
        content: `❌ MCP reconnect error: ${data?.error || "unknown"}`,
      })
    })

    // Tool Start (legacy custom event)
    stream.onCustom("aevatar.scientific.tool_start", (event) => {
      addRawEvent(event)
      const data = event.value as {
        runId?: string
        toolCallId?: string
        toolName?: string
        isMcp?: boolean
      }
      if (data.runId && data.toolCallId && data.toolName) {
        const messageId = `msg:${sessionId}:assistant:${data.runId}`
        upsertToolOutput(messageId, {
          toolCallId: data.toolCallId,
          name: data.toolName,
          status: "running",
          isMcp: Boolean(data.isMcp),
        })
      }
    })

    // Tool End (legacy custom event)
    stream.onCustom("aevatar.scientific.tool_end", (event) => {
      addRawEvent(event)
      const data = event.value as {
        runId?: string
        toolCallId?: string
        toolName?: string
        isMcp?: boolean
        success?: boolean
        durationMs?: number
        error?: string
        resultPreview?: string
      }
      if (data.runId && data.toolCallId && data.toolName) {
        const messageId = `msg:${sessionId}:assistant:${data.runId}`
        upsertToolOutput(messageId, {
          toolCallId: data.toolCallId,
          name: data.toolName,
          status: "done",
          isMcp: Boolean(data.isMcp),
          success: Boolean(data.success),
          durationMs: data.durationMs,
          error: data.error,
          resultPreview: data.resultPreview,
        })
      }
    })

    // === Aevatar Vibe Custom Events (Agent Cards View) ===

    // Agents Roster Snapshot
    stream.onCustom("aevatar.vibe.agents_snapshot", (event) => {
      addRawEvent(event)
      const data = event.value as { agents?: Array<{ agent: string; agentId?: string }> }
      if (Array.isArray(data?.agents)) {
        setAgentRoster(data.agents)
      }
    })

    // Agent Providers Snapshot
    stream.onCustom("aevatar.vibe.agent_providers_snapshot", (event) => {
      addRawEvent(event)
      const data = event.value as { providers?: Record<string, string> }
      if (data?.providers) {
        setAgentProviders(data.providers)
      }
    })

    // Agent Message Meta (per-agent card labels: agent/stepName/providerName)
    stream.onCustom("aevatar.vibe.message_meta", (event) => {
      addRawEvent(event)
      const data = event.value as {
        messageId?: string
        agent?: string
        stepName?: string
        providerName?: string
      }
      if (data?.messageId && data?.agent) {
        const { setAgentMessageMeta, updateAgentMessage } = useSisyphusStore.getState()
        setAgentMessageMeta({
          messageId: data.messageId,
          agent: data.agent,
          stepName: data.stepName,
          providerName: data.providerName,
        })
        // Also update the agentMessage with providerName/stepName
        const agentName = data.agent === "assistant" ? "research_assistant" : data.agent
        updateAgentMessage(agentName, {
          agent: agentName,
          providerName: data.providerName,
          stepName: data.stepName,
        })
      }
    })

    // Session/Run info
    stream.onCustom("aevatar.scientific.session", (event) => {
      addRawEvent(event)
      const data = event.value as { runId?: string; prompt?: string }
      if (data?.runId) {
        setCurrentRun(data.runId, data.prompt)
      }
    })

    // DAG Snapshot - Full DAG data from backend
    stream.onCustom("aevatar.vibe.dag_snapshot", (event) => {
      addRawEvent(event)
      // Flexible parsing: data may be nested differently
      const raw = event.value as Record<string, unknown>
      console.log("[AxiomStream] dag_snapshot raw:", JSON.stringify(raw, null, 2))
      
      // Try to find nodes/edges at various paths
      const dagData = (raw?.dag || raw) as Record<string, unknown>
      const rawNodes = (dagData?.nodes || []) as unknown[]
      const rawEdges = (dagData?.edges || []) as unknown[]
      
      console.log("[AxiomStream] Parsed nodes/edges:", { nodes: rawNodes.length, edges: rawEdges.length })
      
      // Always update dag state even if empty (to clear stale data)
      const { setDag } = useSisyphusStore.getState()
      // Transform nodes
      const nodes = rawNodes.map((n: unknown) => {
        const node = n as Record<string, unknown>
        return {
          id: String(node.id || ''),
          label: String(node.label || node.id || ''),
          status: String(node.status || 'pending'),
          type: String(node.type || ''),
          kind: node.kind as string | undefined,
          owner: node.owner as string | undefined,
          proof: node.proof as string | undefined,
          attestations: node.attestations as unknown[] | undefined,
          attestationsCount: node.attestationsCount as number | undefined,
        }
      })
      // Transform edges from {fromId, toId} to {source, target} format
      const edges = rawEdges.map((e: unknown) => {
        const edge = e as Record<string, unknown>
        return {
          source: String(edge.fromId || edge.source || ''),
          target: String(edge.toId || edge.target || ''),
          type: edge.type,
        }
      })
      console.log("[AxiomStream] Setting DAG:", { nodes: nodes.length, edges: edges.length })
      setDag({ nodes, edges })
    })

    // DAG Updated - Trigger refresh by fetching latest DAG
    stream.onCustom("aevatar.vibe.dag_updated", async (event) => {
      addRawEvent(event)
      console.log("[AxiomStream] DAG updated notification received, fetching latest DAG...")
      
      // Fetch updated DAG from API
      if (sessionId) {
        try {
          const dagSnapshot = await getDagSnapshot(sessionId)
          if (dagSnapshot?.nodes && dagSnapshot?.edges) {
            const { setDag } = useSisyphusStore.getState()
            // Transform to DAGNode format (with status)
            const nodes = dagSnapshot.nodes.map(n => ({
              id: n.id,
              label: n.label || n.id,
              status: 'pending', // Default status - backend DagNode doesn't have status
              type: n.type || '',
              kind: n.kind,
              owner: n.owner,
              proof: n.proof,
              attestations: n.attestations,
              attestationsCount: n.attestationsCount,
            }))
            // Transform DagEdge {fromId, toId} to DAGEdge {source, target}
            const edges = dagSnapshot.edges.map(e => ({
              source: e.fromId,
              target: e.toId,
            }))
            console.log("[AxiomStream] DAG refreshed:", { nodes: nodes.length, edges: edges.length })
            setDag({ nodes, edges })
          }
        } catch (err) {
          console.error("[AxiomStream] Failed to refresh DAG:", err)
        }
      }
    })

    // Catch-all handler - extract worker data from ProgressEvent
    stream.onAny((event) => {
      console.log("[AxiomStream] Event:", event.type, event)
      
      // Handle ProgressEvent to extract worker data (like reference project)
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const eventData = event as any
      if (eventData.type === 'ProgressEvent') {
        // Extract worker from ProgressEvent
        if (eventData.workerId && eventData.workerId !== 'coordinator') {
          const workerId = eventData.workerId as string
          
          // Determine worker status from stepStatus
          let workerStatus: "pending" | "running" | "completed" | "streaming" | "error" = "running"
          if (eventData.stepStatus === 'Completed') workerStatus = "completed"
          else if (eventData.stepStatus === 'Failed') workerStatus = "error"
          
          // Extract name from workerId (worker-0 -> Worker 0)
          const getWorkerName = (id: string) => {
            if (id === 'coordinator') return 'Coordinator'
            const match = id.match(/worker-(\d+)/)
            if (match) return `Worker ${match[1]}`
            return id
          }

          // Check if this event contains prompts/response for history
          const stepId = eventData.stepId || ''
          const systemPrompt = eventData.systemPrompt || ''
          const userPrompt = eventData.userPrompt || ''
          const tokenDelta = eventData.tokenDelta || ''
          const responseContent = eventData.assistantResponse || eventData.assistantResponsePreview || ''
          
          // Update worker basic info
          updateWorker({
            id: workerId,
            name: getWorkerName(workerId),
            status: workerStatus,
            stepId: stepId || undefined,
            stepType: eventData.stepType || undefined,
            streaming: eventData.stepStatus === 'Running' && !!tokenDelta,
            streamContent: tokenDelta || undefined,
            tokenIndex: eventData.tokenIndex ?? undefined,
            provider: eventData.providerName || undefined,
            lastResponse: responseContent || eventData.message || undefined,
          })
          
          // Build history if we have stepId
          if (stepId && (systemPrompt || userPrompt || tokenDelta || responseContent)) {
            appendWorkerHistory(workerId, {
              stepId,
              timestamp: Date.now(),
              phase: eventData.phase || '',
              status: eventData.stepStatus || 'Running',
              system: systemPrompt || undefined,
              user: userPrompt || undefined,
              response: tokenDelta || responseContent || undefined,
            })
          }
        }
        
        // Update session progress from ProgressEvent
        if (eventData.phase || eventData.progressPercent !== undefined) {
          updateSession({
            id: sessionId,
            phase: eventData.phase || undefined,
            progressPercent: eventData.progressPercent ?? undefined,
            totalTokens: eventData.totalTokens ?? undefined,
            totalLlmCalls: eventData.totalLlmCalls ?? undefined,
          })
        }
      }
    })

    // Connect
    stream.connect()

    return () => {
      stream.disconnect()
      setConnected(false)
    }
  }, [sessionId, enabled, setConnected, updateSession, updateWorker, appendWorkerHistory, addMessage, updateStats, addRawEvent, resetForNewSession, appendWorkerStream, setTools, upsertToolOutput])

  return {
    isConnected: useSisyphusStore((s) => s.isConnected),
  }
}
