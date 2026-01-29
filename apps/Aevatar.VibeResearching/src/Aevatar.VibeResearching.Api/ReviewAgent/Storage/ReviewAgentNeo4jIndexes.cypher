// Review Agent Neo4j Indexes
// ==========================
// These indexes optimize the Review Agent queries for stale node detection and cleanup.
// Run these statements against your Neo4j instance when deploying with Neo4j backend.
//
// Reference: tasks.md T082, T083

// T082: Index for stale node queries (GetStaleKnowledgeNodes)
// Used to efficiently find nodes that need review based on activation status and review timestamp
CREATE INDEX review_agent_stale IF NOT EXISTS
FOR (n:KnowledgeNode)
ON (n.is_activated, n.last_reviewed_at);

// T083: Index for cleanup queries (GetNodesForCleanup)
// Used to efficiently find deactivated nodes that should be permanently removed
CREATE INDEX review_agent_cleanup IF NOT EXISTS
FOR (n:KnowledgeNode)
ON (n.is_activated, n.deactivated_timestamp);

// Additional recommended indexes for Review Agent performance

// Index for querying nodes by session (common query pattern)
CREATE INDEX review_agent_session IF NOT EXISTS
FOR (n:KnowledgeNode)
ON (n.session_id);

// Composite index for active nodes by session (common filter)
CREATE INDEX review_agent_active_session IF NOT EXISTS
FOR (n:KnowledgeNode)
ON (n.session_id, n.is_activated);
