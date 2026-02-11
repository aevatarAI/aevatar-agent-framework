import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { HttpClient } from "../../http-client.js";
import { HttpClientError } from "../../http-client.js";
import { mcpText, mcpError } from "../../types.js";
import { ExplainKnowledgeInputSchema } from "./schemas.js";

/**
 * Registers the dag_explain_knowledge tool on the MCP server.
 * Maps to GET /api/knowledges/{id}/explain on the DAG service.
 */
export function registerExplainKnowledgeTool(
  server: McpServer,
  client: HttpClient,
): void {
  server.registerTool(
    "dag_explain_knowledge",
    {
      title: "Explain Knowledge Node",
      description:
        "Generates a comprehensive markdown explanation of a knowledge node, " +
        "including metadata, full content, and multi-level derivation chain " +
        "(upstream parents and downstream children via BFS traversal).",
      inputSchema: ExplainKnowledgeInputSchema,
      annotations: {
        readOnlyHint: true,
        idempotentHint: true,
      },
    },
    async (args) => {
      try {
        const params: Record<string, string> = {};
        if (args.level !== undefined) {
          params.level = String(args.level);
        }

        const markdown = await client.getText(
          `/api/knowledges/${args.id}/explain`,
          params,
        );
        return mcpText(markdown);
      } catch (error) {
        if (!(error instanceof HttpClientError)) {
          return mcpError(`Unexpected error: ${(error as Error).message}`);
        }
        if (error.statusCode === 404) {
          return mcpError(`Knowledge node ${args.id} not found.`);
        }
        return mcpError(error.message);
      }
    },
  );
}
