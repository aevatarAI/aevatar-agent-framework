import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { HttpClient } from "../../http-client.js";
import { HttpClientError } from "../../http-client.js";
import { mcpText, mcpError } from "../../types.js";
import { GetSnapshotInputSchema } from "./schemas.js";

/**
 * Registers the dag_get_snapshot tool on the MCP server.
 * Maps to GET /api/knowledges on the DAG service.
 */
export function registerGetSnapshotTool(
  server: McpServer,
  client: HttpClient,
): void {
  server.registerTool(
    "dag_get_snapshot",
    {
      title: "Get DAG Snapshot",
      description:
        "Returns a complete snapshot of the entire knowledge DAG, including all " +
        "nodes, edges, and pre-computed lookup maps (nodeMap, parentsMap, childrenMap).",
      inputSchema: GetSnapshotInputSchema,
      annotations: {
        readOnlyHint: true,
        idempotentHint: true,
      },
    },
    async () => {
      try {
        const result = await client.get<unknown>("/api/knowledges");
        return mcpText(JSON.stringify(result, null, 2));
      } catch (error) {
        if (error instanceof HttpClientError) {
          return mcpError(error.message);
        }
        return mcpError(`Unexpected error: ${(error as Error).message}`);
      }
    },
  );
}
