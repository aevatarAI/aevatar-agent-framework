import assert from "node:assert/strict";
import test from "node:test";
import { createRequire } from "node:module";

const require = createRequire(import.meta.url);
const mod = require("../dist/testExports.js");

const {
  normalizeVaultRoot,
  safeIdForPath,
  joinVaultPath,
  sessionDir,
  runDir,
  runEventsPath,
} = mod;

test("normalizeVaultRoot defaults to SRA", () => {
  assert.equal(normalizeVaultRoot(""), "SRA");
  assert.equal(normalizeVaultRoot("   "), "SRA");
});

test("normalizeVaultRoot rejects absolute and traversal paths", () => {
  assert.throws(() => normalizeVaultRoot("/abs"), /vaultRoot must be a vault-relative path/);
  assert.throws(() => normalizeVaultRoot("../x"), /invalid path segments/);
  assert.throws(() => normalizeVaultRoot("a/../b"), /invalid path segments/);
});

test("joinVaultPath rejects absolute/traversal segments", () => {
  assert.throws(() => joinVaultPath("/abs", "x"), /absolute path/);
  assert.throws(() => joinVaultPath("a", "..", "b"), /traversal/);
});

test("safeIdForPath sanitizes runId for cross-platform filesystem", () => {
  assert.equal(safeIdForPath("abc:def"), "abc-def");
  assert.equal(safeIdForPath("  "), "unknown");
});

test("layout helpers produce deterministic paths", () => {
  const root = "SRA";
  const sid = "s123";
  const rid = "s123:1";
  assert.equal(sessionDir(root, sid), "SRA/sessions/s123");
  assert.equal(runDir(root, sid, rid), "SRA/sessions/s123/runs/s123-1");
  assert.equal(runEventsPath(root, sid, rid), "SRA/sessions/s123/runs/s123-1/events.jsonl");
});


