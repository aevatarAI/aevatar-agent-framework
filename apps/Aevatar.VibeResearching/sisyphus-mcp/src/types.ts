import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { HttpClient } from "./http-client.js";

/**
 * Function signature for registering a group of tools for a microservice.
 * Each service barrel (e.g., services/dag/index.ts) exports a function matching this type.
 */
export type ToolRegistrar = (server: McpServer, client: HttpClient) => void;

/**
 * Utility type: the MCP text content response returned by tool handlers.
 * The index signature is required for compatibility with the MCP SDK's
 * tool handler return type.
 */
export interface McpTextContent {
  // TODO: Remove index signature if MCP SDK improves return type constraints.
  [key: string]: unknown;
  content: Array<{ type: "text"; text: string }>;
  isError?: boolean;
}

/**
 * Creates a successful MCP text content response.
 */
export function mcpText(text: string): McpTextContent {
  return {
    content: [{ type: "text", text }],
  };
}

/**
 * Creates an error MCP text content response.
 */
export function mcpError(message: string): McpTextContent {
  return {
    content: [{ type: "text", text: message }],
    isError: true,
  };
}
