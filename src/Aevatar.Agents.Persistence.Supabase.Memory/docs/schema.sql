-- ============================================================
--  Aevatar AI Memory - Supabase(Postgres + pgvector) Schema
--
--  NOTE:
--  - This is a recommended baseline script.
--  - Vector dimensions here uses vector(1536). If your embedding dims differ, regenerate/update DDL.
--  - Default policy: lock down PUBLIC/anon/authenticated to reduce accidental PostgREST exposure.
-- ============================================================

CREATE EXTENSION IF NOT EXISTS vector;

CREATE SCHEMA IF NOT EXISTS aevatar_memory;

CREATE TABLE IF NOT EXISTS aevatar_memory.memory_entries (
  memory_id text NOT NULL,
  entry_id text NOT NULL,
  scope_type int NOT NULL,
  scope_id text NOT NULL DEFAULT '',
  run_id text NOT NULL DEFAULT '',
  agent_id text NOT NULL DEFAULT '',
  role text NOT NULL DEFAULT '',
  content text NOT NULL,
  tags jsonb NOT NULL DEFAULT '{}'::jsonb,
  created_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (memory_id, entry_id)
);

CREATE TABLE IF NOT EXISTS aevatar_memory.memory_vectors (
  memory_id text NOT NULL,
  entry_id text NOT NULL,
  scope_type int NOT NULL,
  scope_id text NOT NULL DEFAULT '',
  run_id text NOT NULL DEFAULT '',
  agent_id text NOT NULL DEFAULT '',
  role text NOT NULL DEFAULT '',
  content text NOT NULL,
  tags jsonb NOT NULL DEFAULT '{}'::jsonb,
  created_at timestamptz NOT NULL DEFAULT now(),
  embedding vector(1536) NOT NULL,
  PRIMARY KEY (memory_id, entry_id)
);

CREATE INDEX IF NOT EXISTS idx_memory_entries_memory_created_at
  ON aevatar_memory.memory_entries (memory_id, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_memory_vectors_memory_created_at
  ON aevatar_memory.memory_vectors (memory_id, created_at DESC);

-- Vector index (ivfflat) - use cosine distance by default
CREATE INDEX IF NOT EXISTS idx_memory_vectors_embedding_ivfflat
  ON aevatar_memory.memory_vectors
  USING ivfflat (embedding vector_cosine_ops) WITH (lists = 100);

-- ==============================
-- Lock down permissions
-- ==============================
REVOKE ALL ON SCHEMA aevatar_memory FROM PUBLIC;
REVOKE ALL ON TABLE aevatar_memory.memory_entries FROM PUBLIC;
REVOKE ALL ON TABLE aevatar_memory.memory_vectors FROM PUBLIC;

DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'anon') THEN
    EXECUTE 'REVOKE ALL ON SCHEMA aevatar_memory FROM anon';
    EXECUTE 'REVOKE ALL ON TABLE aevatar_memory.memory_entries FROM anon';
    EXECUTE 'REVOKE ALL ON TABLE aevatar_memory.memory_vectors FROM anon';
  END IF;
END
$$;

DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'authenticated') THEN
    EXECUTE 'REVOKE ALL ON SCHEMA aevatar_memory FROM authenticated';
    EXECUTE 'REVOKE ALL ON TABLE aevatar_memory.memory_entries FROM authenticated';
    EXECUTE 'REVOKE ALL ON TABLE aevatar_memory.memory_vectors FROM authenticated';
  END IF;
END
$$;


