import { useEffect, useRef, useCallback } from "react"
import { flushSync } from "react-dom"
import { useSisyphusStore, type SessionStatus } from "@/store/sisyphus-store"
import { useStreamContentStore } from "@/store/stream-content-store"
import { createAxiomEventStream, getToolsSnapshot, getDagSnapshot, getSessionStatus } from "@/lib/axiom-client"
import type { EventStream } from "@aevatar/kit-protocol"
import { parseMessageId } from "@aevatar/kit-protocol"
import type { ToolOutput, NodeKind } from "@/types"

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
    setTools,
    setCurrentRun,
    setInputMode,
    // updateAgentMessage removed - now using isolated stream store
    setAgentRoster,
    setAgentProviders,
    updateAgentStatusReport,
    updateAgentLlmStatus,
  } = useSisyphusStore()

  // Tool outputs state (per-message)
  // NOTE: Cleared on session change to prevent memory leaks
  const toolOutputsRef = useRef<Map<string, Map<string, ToolOutput>>>(new Map())

  // Clear tool outputs (called on session change)
  const clearToolOutputs = useCallback(() => {
    toolOutputsRef.current.clear()
  }, [])

  // Upsert tool output for a message
  const upsertToolOutput = useCallback((messageId: string, tool: ToolOutput) => {
    if (!toolOutputsRef.current.has(messageId)) {
      toolOutputsRef.current.set(messageId, new Map())
    }
    const msgTools = toolOutputsRef.current.get(messageId)!
    const existing = msgTools.get(tool.toolCallId)
    msgTools.set(tool.toolCallId, { ...existing, ...tool })
  }, [])

  // ── Stream Content Store Actions ──
  // Using isolated store for high-frequency streaming updates
  // This prevents cascading re-renders across the entire app
  const { 
    appendWorkerContent, 
    appendAgentContent,
    setAgentMeta,
    finalizeWorkerContent,
    finalizeAgentContent,
    clearAllStreams,
  } = useStreamContentStore.getState()

  // Handle worker streaming content - use flushSync with ISOLATED store
  // Only components subscribed to this specific workerId will re-render
  const appendWorkerStream = useCallback((workerId: string, delta: string) => {
    flushSync(() => {
      appendWorkerContent(workerId, delta)
    })
    
    // Also ensure worker exists in main store (without streaming content)
    const state = useSisyphusStore.getState()
    if (!state.workers[workerId]) {
      updateWorker({
        id: workerId,
        name: workerId,
        status: "streaming",
        streaming: true,
      })
    }
  }, [appendWorkerContent, updateWorker])

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

    // Note: resetForNewSession() is called in App.tsx before session switch
    // Avoid duplicate reset here to prevent race conditions

    // Create new stream
    const stream = createAxiomEventStream(sessionId)
    streamRef.current = stream

    // Fallback: Set connected when we receive any event (first event = connected)
    let hasReceivedEvent = false
    
    // Connection timeout: if no event received in 10 seconds, log warning
    const connectionTimeout = setTimeout(() => {
      if (!hasReceivedEvent) {
        console.warn(`[useAxiomStream] No events received after 10s for session ${sessionId}. Check backend connection.`)
      }
    }, 10000)

    // === Status Change ===
    stream.onStatusChange((status) => {
      setConnected(status === "connected")
      if (status === "connected") {
        hasReceivedEvent = true
        clearTimeout(connectionTimeout)
      }
    })

    const markConnected = () => {
      if (!hasReceivedEvent) {
        hasReceivedEvent = true
        clearTimeout(connectionTimeout)
        setConnected(true)
      }
    }

    // === Standard AG-UI Events ===

    // Messages Snapshot - marks connection as established and restores streaming content
    stream.on("MESSAGES_SNAPSHOT", (event) => {
      markConnected()
      if (import.meta.env.DEV) {
        addRawEvent(event)
      }

      // Restore messages from snapshot - this is crucial for session switching
      // When user switches back to a session that's still streaming, we need to
      // restore the accumulated content immediately
      const messages = (event as { messages?: Array<{ id: string; role: string; content: string }> }).messages || []

      // Get isolated store for direct state updates
      const streamStore = useStreamContentStore.getState()

      for (const msg of messages) {
        if (!msg.id || !msg.content) continue

        // Parse messageId format: msg:{sessionId}:{role/agent}:{runId}
        const parts = msg.id.split(":")
        if (parts.length < 4) continue

        const role = parts[2]

        // Only restore assistant messages (streaming content)
        if (role === "assistant" || (role && role !== "user" && !role.startsWith("worker"))) {
          const agentName = role === "assistant" ? "research_assistant" : role
          const parsed = parseMessageId(msg.id)

          // Restore worker streaming content to isolated store
          if (parsed.workerId) {
            const existingWorker = streamStore.workerStreams[parsed.workerId]
            if (!existingWorker?.content) {
              // Set full content directly (not append)
              useStreamContentStore.setState((state) => ({
                workerStreams: {
                  ...state.workerStreams,
                  [parsed.workerId]: {
                    content: msg.content,
                    isStreaming: true,
                    tokenCount: Math.ceil(msg.content.length / 4),
                  },
                },
              }))
              
              // Ensure worker exists in main store
              updateWorker({
                id: parsed.workerId,
                name: parsed.workerId === "coordinator" ? "Coordinator" : `Worker ${parsed.workerId}`,
                status: "streaming",
                streaming: true,
              })
            }
          }

          // Restore agent message to isolated store
          const existingAgent = streamStore.agentStreams[agentName]
          if (!existingAgent?.content) {
            useStreamContentStore.setState((state) => ({
              agentStreams: {
                ...state.agentStreams,
                [agentName]: {
                  content: msg.content,
                  isStreaming: true,
                  isFinal: false,
                  tokenCount: Math.ceil(msg.content.length / 4),
                },
              },
            }))
          }
        }
      }
    })

    // Run Started
    stream.on("RUN_STARTED", async (event) => {
      markConnected()
      addRawEvent(event)
      
      // Extract runId from event
      const runId = (event as { runId?: string }).runId || `run-${Date.now()}`
      setCurrentRun(runId)
      
      // Immediately refresh session status to show new run's steps
      // This ensures status box updates when a new research round starts
      // Fixes issue where status box shows old round's steps while DAG shows new nodes
      if (sessionId) {
        try {
          const status = await getSessionStatus(sessionId)
          if (status) {
            const { setSessionStatus } = useSisyphusStore.getState()
            const sessionStatus: SessionStatus = {
              runId: status.runId,
              updatedAt: status.updatedAt,
              steps: {
                order: status.steps.order,
                map: status.steps.map,
                running: status.steps.running,
                done: status.steps.done,
              },
              agents: status.agents,
              runningTools: status.runningTools,
            }
            setSessionStatus(sessionStatus)
            console.debug("[AxiomStream] Status refreshed immediately on run start")
          }
        } catch (err) {
          console.debug("[AxiomStream] Failed to refresh status on run start:", err)
        }
      }
      
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
      if (import.meta.env.DEV) {
        addRawEvent(event)
      }
      const parsed = parseMessageId(event.messageId)
      
      // Update worker in main store (metadata only, not streaming content)
      updateWorker({
        id: parsed.workerId,
        name: parsed.workerId === "coordinator" ? "Coordinator" : `Worker ${parsed.workerId}`,
        status: "streaming",
        streaming: true,
        stepId: parsed.stepId,
      })
      
      // Agent streaming state is managed by isolated store
      // No need to update main store here - content will flow via TEXT_MESSAGE_CONTENT
    })

    // Text Message Content
    stream.on("TEXT_MESSAGE_CONTENT", (event) => {
      // Skip rawEvents in production for performance
      if (import.meta.env.DEV) {
        addRawEvent(event)
      }
      
      const parsed = parseMessageId(event.messageId)
      appendWorkerStream(parsed.workerId, event.delta)

      // Also update agent message - use ISOLATED store for immediate render
      // Only components subscribed to this specific agent will re-render
      const parts = event.messageId.split(":")
      if (parts.length >= 4) {
        const agent = parts[2]
        if (agent && agent !== "user" && !agent.startsWith("worker")) {
          const agentName = agent === "assistant" ? "research_assistant" : agent
          flushSync(() => {
            appendAgentContent(agentName, event.delta)
          })
        }
      }
    })

    // Text Message End
    stream.on("TEXT_MESSAGE_END", (event) => {
      if (import.meta.env.DEV) {
        addRawEvent(event)
      }
      const parsed = parseMessageId(event.messageId)
      
      // Finalize worker content from isolated store and sync to main store
      const finalContent = finalizeWorkerContent(parsed.workerId)
      
      updateWorker({
        id: parsed.workerId,
        status: "completed",
        streaming: false,
        lastResponse: finalContent,
      })
      
      // Finalize agent message in isolated store
      const parts = event.messageId.split(":")
      if (parts.length >= 4) {
        const agent = parts[2]
        if (agent && agent !== "user" && !agent.startsWith("worker")) {
          const agentName = agent === "assistant" ? "research_assistant" : agent
          finalizeAgentContent(agentName)
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

    // LLM Trace Event (from ExecutionTraceProgressHook)
    // Provides real-time LLM request/response status
    stream.onCustom("aevatar.llm.trace", (event) => {
      if (import.meta.env.DEV) {
        addRawEvent(event)
      }
      const value = event.value as {
        phase?: string
        status?: string
        sessionId?: string
        executionId?: string
      }
      
      // Try to extract agent_id and model from rawEvent fields
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const rawEvent = (event as any).rawEvent
      const fields = rawEvent?.fields as Record<string, { stringValue?: string }> | undefined
      const agentId = fields?.agent_id?.stringValue
      const model = fields?.llm_model?.stringValue
      
      if (agentId && value.phase) {
        const phase = value.phase as "llm.request" | "llm.response" | "idle"
        updateAgentLlmStatus(agentId, { phase, model })
      }
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

    // Run Steps Snapshot (bootstrap event containing runId)
    // This restores currentRunId on page refresh
    stream.onCustom("aevatar.ui.run_steps_snapshot", (event) => {
      addRawEvent(event)
      const data = event.value as {
        sessionId?: string
        runId?: string
        order?: string[]
        map?: Record<string, unknown>
      }
      // Only set if runId is non-empty (empty string means no active run)
      if (data?.runId && data.runId.length > 0) {
        setCurrentRun(data.runId)
        setInputMode('vibe')
      }
    })

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

    // Agent Status Report (real-time work status from agents)
    stream.onCustom("aevatar.vibe.agent_status_report", (event) => {
      addRawEvent(event)
      const data = event.value as {
        agentId?: string
        agentName?: string
        statusText?: string
        progress?: number
      }
      if (data?.agentName && data?.statusText) {
        updateAgentStatusReport({
          agentId: data.agentId || data.agentName,
          agentName: data.agentName,
          statusText: data.statusText,
          progress: data.progress,
          timestamp: Date.now(),
        })
      }
    })

    // Agent Message Meta (per-agent card labels: agent/stepName/providerName)
    stream.onCustom("aevatar.vibe.message_meta", (event) => {
      if (import.meta.env.DEV) {
        addRawEvent(event)
      }
      const data = event.value as {
        messageId?: string
        agent?: string
        stepName?: string
        providerName?: string
      }
      if (data?.messageId && data?.agent) {
        const { setAgentMessageMeta } = useSisyphusStore.getState()
        setAgentMessageMeta({
          messageId: data.messageId,
          agent: data.agent,
          stepName: data.stepName,
          providerName: data.providerName,
        })
        // Update agent metadata in isolated store
        const agentName = data.agent === "assistant" ? "research_assistant" : data.agent
        setAgentMeta(agentName, {
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
      
      // Try to find nodes/edges at various paths
      const dagData = (raw?.dag || raw) as Record<string, unknown>
      const rawNodes = (dagData?.nodes || []) as unknown[]
      const rawEdges = (dagData?.edges || []) as unknown[]
      
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
          kind: node.kind as NodeKind | undefined,
          owner: node.owner as string | undefined,
          proof: node.proof as string | undefined,
          attestations: node.attestations as { pubkey?: string; signature?: string }[] | undefined,
          attestationsCount: node.attestationsCount as number | undefined,
        }
      })
      // Transform edges from {fromId, toId} to {source, target} format
      const edges = rawEdges.map((e: unknown) => {
        const edge = e as Record<string, unknown>
        return {
          source: String(edge.fromId || edge.source || ''),
          target: String(edge.toId || edge.target || ''),
          type: edge.type as string | undefined,
        }
      })
      setDag({ nodes, edges })
    })

    // DAG Updated - Trigger refresh by fetching latest DAG
    stream.onCustom("aevatar.vibe.dag_updated", async (event) => {
      addRawEvent(event)

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
              kind: n.kind as NodeKind | undefined,
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
            setDag({ nodes, edges })
          }
        } catch (err) {
          console.error("[AxiomStream] Failed to refresh DAG:", err)
        }
      }
    })

    // Milestone Started - Highlight the active plan node
    stream.onCustom("aevatar.vibe.milestone_started", (event) => {
      addRawEvent(event)
      const data = event.value as {
        sessionId?: string
        milestoneNodeId?: string
        milestoneIndex?: number
        totalMilestones?: number
      }
      if (data.milestoneNodeId) {
        const { setActiveMilestoneNodeId } = useSisyphusStore.getState()
        // Pass sessionId to store milestone per-session
        setActiveMilestoneNodeId(data.milestoneNodeId, data.sessionId || sessionId)
      }
    })

    // Milestone Finished - Clear the active highlight
    stream.onCustom("aevatar.vibe.milestone_finished", (event) => {
      addRawEvent(event)
      const data = event.value as {
        sessionId?: string
        milestoneNodeId?: string
        milestoneIndex?: number
      }
      // Clear the active milestone highlight for this session
      const { setActiveMilestoneNodeId } = useSisyphusStore.getState()
      setActiveMilestoneNodeId(null, data.sessionId || sessionId)
    })

    // System Reply - Dynamic user input response from interruption analysis
    stream.onCustom("aevatar.scientific.system_reply", (event) => {
      addRawEvent(event)
      const data = event.value as {
        sessionId?: string
        messageType?: "acknowledgment" | "direction_change" | "progress_inquiry" | "other"
        content?: string
      }
      console.log("[AxiomStream] System reply:", data)
      if (data?.content) {
        addMessage({
          role: "system",
          content: data.content,
          agentName: "SYSTEM",
        })
      }
    })

    // Run Interrupted - Notifies UI that a run was interrupted by new input
    stream.onCustom("aevatar.scientific.run_interrupted", (event) => {
      addRawEvent(event)
      const data = event.value as {
        threadId?: string
        oldRunId?: string
        newRunId?: string
        reason?: string
      }
      console.log("[AxiomStream] Run interrupted:", data)
      // Update current run to the new run
      if (data.newRunId) {
        setCurrentRun(data.newRunId)
      }
      // Clear isolated streams for the new run
      clearAllStreams()
    })

    // Catch-all handler - extract worker data from ProgressEvent
    stream.onAny((event) => {
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
      clearTimeout(connectionTimeout)
      stream.disconnect()
      setConnected(false)
      // Clear isolated streaming state on disconnect
      clearAllStreams()
      // Clear tool outputs to prevent memory leaks
      clearToolOutputs()
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps -- Store functions are stable, only sessionId/enabled should trigger reconnect
  }, [sessionId, enabled])

  return {
    isConnected: useSisyphusStore((s) => s.isConnected),
  }
}
