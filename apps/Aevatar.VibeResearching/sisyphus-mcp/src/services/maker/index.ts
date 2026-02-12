import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { HttpClient } from "../../http-client.js";
import { registerMakerHealthTool } from "./health.js";
import { registerMakerVerifyTool } from "./verify.js";

/**
 * Registers all sisyphus-maker service tools on the MCP server.
 */
export function registerMakerTools(server: McpServer, client: HttpClient): void {
  registerMakerHealthTool(server, client);
  registerMakerVerifyTool(server, client);
}
