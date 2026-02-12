import { z } from "zod";

/** Input schema for maker_health tool (no parameters). */
export const HealthInputSchema = z.object({});

/** Prompt definition: system instruction + user template. */
const InlinePromptSchema = z.object({
  systemPrompt: z
    .string()
    .min(1, "systemPrompt must not be empty.")
    .describe(
      "System-level instruction for the LLM. Defines the role/persona and output format. " +
      "For best results, instruct the LLM to respond with STRICT JSON: " +
      '{ "passed": true|false, "reason": "...", "confidence": 0.0-1.0 }.'
    ),
  userPromptTemplate: z
    .string()
    .min(1, "userPromptTemplate must not be empty.")
    .describe(
      "User-level prompt template with {{placeholder}} variables that will be " +
      "substituted from the variables map at runtime."
    ),
});

/** Per-request engine configuration overrides. All fields optional. */
const MakerConfigSchema = z.object({
  workerCount: z
    .number()
    .int("workerCount must be an integer.")
    .min(1, "workerCount must be at least 1.")
    .max(20, "workerCount must be at most 20.")
    .optional()
    .describe("Number of parallel LLM workers (1-20). Default: 3."),
  consensusK: z
    .number()
    .int("consensusK must be an integer.")
    .min(1, "consensusK must be at least 1.")
    .optional()
    .describe("Ahead-by-K consensus threshold. Must be >= 1 and <= workerCount. Default: 2."),
  maxRounds: z
    .number()
    .int("maxRounds must be an integer.")
    .min(1, "maxRounds must be at least 1.")
    .max(20, "maxRounds must be at most 20.")
    .optional()
    .describe("Maximum voting rounds before best-effort result (1-20). Default: 5."),
  timeoutSeconds: z
    .number()
    .int("timeoutSeconds must be an integer.")
    .min(10, "timeoutSeconds must be at least 10.")
    .max(600, "timeoutSeconds must be at most 600.")
    .optional()
    .describe("Per-request timeout in seconds (10-600). Default: 120."),
  model: z
    .string()
    .min(1, "model must not be empty if provided.")
    .optional()
    .describe('LLM provider name override (e.g., "deepseek", "openai"). Uses server default if omitted.'),
});

/** Input schema for maker_verify tool. */
export const VerifyInputSchema = z.object({
  prompt: InlinePromptSchema
    .describe("Prompt definition containing system instruction and user template."),
  variables: z
    .record(z.string(), z.string())
    .optional()
    .describe(
      "Flat string-to-string map of template variables. Each key maps to a " +
      "{{placeholder}} in userPromptTemplate. Omit if the prompt has no placeholders."
    ),
  config: MakerConfigSchema
    .optional()
    .describe(
      "Optional engine configuration overrides. Omitted fields use server defaults " +
      "(workerCount=3, consensusK=2, maxRounds=5, timeoutSeconds=120)."
    ),
});
