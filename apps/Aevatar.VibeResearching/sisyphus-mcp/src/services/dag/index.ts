import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { HttpClient } from "../../http-client.js";
import { registerCreateKnowledgesTool } from "./create-knowledges.js";
import { registerGetSnapshotTool } from "./get-snapshot.js";
import { registerUpdateKnowledgesTool } from "./update-knowledges.js";
import { registerDeleteKnowledgesTool } from "./delete-knowledges.js";
import { registerExplainKnowledgeTool } from "./explain-knowledge.js";

/**
 * Registers all DAG service tools on the MCP server.
 */
export function registerDagTools(server: McpServer, client: HttpClient): void {
  registerCreateKnowledgesTool(server, client);
  registerGetSnapshotTool(server, client);
  registerUpdateKnowledgesTool(server, client);
  registerDeleteKnowledgesTool(server, client);
  registerExplainKnowledgeTool(server, client);
}
