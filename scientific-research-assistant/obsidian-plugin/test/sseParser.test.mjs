import assert from "node:assert/strict";
import test from "node:test";
import { createRequire } from "node:module";

const require = createRequire(import.meta.url);
const mod = require("../dist/testExports.js");
const { SseParser } = mod;

test("SseParser parses single SSE frame", () => {
  const p = new SseParser();
  const frames = p.push('data: {"type":"X"}\n\n');
  assert.deepEqual(frames, ['{"type":"X"}']);
});

test("SseParser handles CRLF and multi-line data", () => {
  const p = new SseParser();
  const frames = p.push("data: line1\r\ndata: line2\r\n\r\n");
  assert.deepEqual(frames, ["line1\nline2"]);
});

test("SseParser handles chunked input", () => {
  const p = new SseParser();
  assert.deepEqual(p.push('data: {"type":"A"}\n'), []);
  assert.deepEqual(p.push("\n"), ['{"type":"A"}']);
});

test("SseParser flush parses trailing data without delimiter (best-effort)", () => {
  const p = new SseParser();
  p.push('data: {"type":"Z"}\n');
  const rest = p.flush();
  assert.deepEqual(rest, ['{"type":"Z"}']);
});


