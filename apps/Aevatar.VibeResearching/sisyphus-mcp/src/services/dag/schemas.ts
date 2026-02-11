import { z } from "zod";

/** Schema for a single knowledge item in a create request. */
export const KnowledgeDtoSchema = z.object({
  sessionId: z
    .string()
    .optional()
    .describe("Research session ID. Defaults to empty string on server."),
  title: z
    .string()
    .min(1, "Title must not be blank.")
    .describe("Concise title for the knowledge node."),
  description: z
    .string()
    .min(1, "Description must not be blank.")
    .describe("Detailed description of the knowledge."),
  deriveDetail: z
    .string()
    .min(1, "DeriveDetail must not be blank.")
    .describe("How the knowledge was derived (methodology, reasoning)."),
  resourceUri: z
    .string()
    .optional()
    .describe("URI to an external resource (paper, dataset, etc.)."),
  references: z
    .array(z.string())
    .optional()
    .describe("List of reference citations."),
  dependsOn: z
    .array(z.string().uuid("Each dependsOn value must be a valid UUID."))
    .optional()
    .describe("IDs of existing nodes this knowledge depends on."),
});

/** Input schema for dag_create_knowledges tool. */
export const CreateKnowledgesInputSchema = z.object({
  knowledgeList: z
    .array(KnowledgeDtoSchema)
    .min(1, "knowledgeList must contain at least 1 item.")
    .describe("List of knowledge items to create."),
  indexDependencies: z
    .record(z.string(), z.array(z.number().int().min(0)))
    .optional()
    .describe(
      "Cross-reference map: parent index (string key) -> child indices within the batch.",
    ),
});

/** Schema for a single knowledge update value (no dependsOn field). */
export const KnowledgeUpdateDtoSchema = z.object({
  sessionId: z
    .string()
    .optional()
    .describe("Research session ID."),
  title: z
    .string()
    .min(1, "Title must not be blank.")
    .describe("Updated title."),
  description: z
    .string()
    .min(1, "Description must not be blank.")
    .describe("Updated description."),
  deriveDetail: z
    .string()
    .min(1, "DeriveDetail must not be blank.")
    .describe("Updated derivation details."),
  resourceUri: z
    .string()
    .optional()
    .describe("Updated resource URI."),
  references: z
    .array(z.string())
    .optional()
    .describe("Updated references."),
});

/** Input schema for dag_update_knowledges tool (SDK-compatible wrapper). */
export const UpdateKnowledgesInputSchema = z.object({
  updates: z
    .record(z.string(), KnowledgeUpdateDtoSchema)
    .refine((obj) => Object.keys(obj).length > 0, {
      message: "Updates map must contain at least one entry.",
    })
    .describe("Map of node UUID -> updated fields."),
});

/** Input schema for dag_delete_knowledges tool. */
export const DeleteKnowledgesInputSchema = z.object({
  ids: z
    .array(z.string().uuid("Each ID must be a valid UUID."))
    .min(1, "ids must contain at least 1 item.")
    .describe("Array of knowledge node IDs to delete."),
});

/** Input schema for dag_explain_knowledge tool. */
export const ExplainKnowledgeInputSchema = z.object({
  id: z
    .string()
    .uuid("id must be a valid UUID.")
    .describe("The knowledge node ID to explain."),
  level: z
    .number()
    .int("level must be an integer.")
    .min(1, "level must be at least 1.")
    .max(50, "level must be at most 50.")
    .optional()
    .describe("BFS traversal depth for derivation chains. Default: 10."),
});

/** Input schema for dag_get_snapshot tool (empty -- no parameters needed). */
export const GetSnapshotInputSchema = z.object({});
