// using System.Collections.Concurrent;
// using System.Text.RegularExpressions;
// using Aevatar.AxiomReasoning.Models;
// using Microsoft.Extensions.Options;
// using Supabase;
// using Supabase.Postgrest.Attributes;
// using Supabase.Postgrest.Models;

// namespace Aevatar.AxiomReasoning.Graph;

// // ============================================================
// //  SUPABASE GRAPH STORE
// //
// //  存储模型：
// //  - dag_nodes: (session_id, node_id) 唯一
// //  - dag_edges: (session_id, from_id, to_id, kind) 唯一
// //
// //  NOTE:
// //  - GraphEvent 是“业务状态快照”；我们只落盘 nodes/edges 事实表
// //  - 推理（closure/cycle/provable）在应用层做，避免绑定数据库特性
// // ============================================================

// [Table("axiom_reasoning_dag_nodes")]
// public sealed class DagNodeRecord : BaseModel
// {
//     [PrimaryKey("id")]
//     public string? Id { get; set; }

//     [Column("session_id")]
//     public string SessionId { get; set; } = "";

//     [Column("node_id")]
//     public string NodeId { get; set; } = "";

//     [Column("kind")]
//     public string Kind { get; set; } = "";

//     [Column("label")]
//     public string? Label { get; set; }

//     [Column("proof")]
//     public string? Proof { get; set; }

//     [Column("updated_at")]
//     public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
// }

// [Table("axiom_reasoning_dag_edges")]
// public sealed class DagEdgeRecord : BaseModel
// {
//     [PrimaryKey("id")]
//     public string? Id { get; set; }

//     [Column("session_id")]
//     public string SessionId { get; set; } = "";

//     [Column("from_id")]
//     public string FromId { get; set; } = "";

//     [Column("to_id")]
//     public string ToId { get; set; } = "";

//     [Column("kind")]
//     public string Kind { get; set; } = "depends_on";

//     [Column("updated_at")]
//     public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
// }

// public sealed class SupabaseGraphStore : IGraphStore
// {
//     // NOTE:
//     // Supabase .NET 的 Postgrest 映射使用 [Table("...")]，表名目前是“编译期固定”的。
//     // 因此这里的表名必须和 DagNodeRecord/DagEdgeRecord 的 [Table] 一致。
//     private const string NodesTable = "axiom_reasoning_dag_nodes";
//     private const string EdgesTable = "axiom_reasoning_dag_edges";

//     private static readonly Regex AxiomIdRegex = new(@"^([A-Za-z]\w*)\s*:", RegexOptions.Compiled);

//     private readonly SupabaseConfig _config;
//     private readonly ILogger<SupabaseGraphStore> _logger;

//     private Client? _client;
//     private bool _initialized;
//     private bool _nodesTableExists;
//     private bool _edgesTableExists;

//     // Serialize "delete edges then insert edges" per session to avoid races.
//     private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new(StringComparer.Ordinal);

//     // Hot cache (per-process) to avoid read-after-write races in UI and reduce DB reads.
//     private readonly ConcurrentDictionary<string, DagSnapshot> _cache = new(StringComparer.Ordinal);

//     public SupabaseGraphStore(IOptions<SupabaseConfig> config, ILogger<SupabaseGraphStore> logger)
//     {
//         _config = config.Value;
//         _logger = logger;
//     }

//     public bool IsEnabled =>
//         _config.Enabled &&
//         _config.DagEnabled &&
//         !string.IsNullOrWhiteSpace(_config.Url) &&
//         !string.IsNullOrWhiteSpace(_config.Key);

//     public object GetDiagnostics() => new
//     {
//         type = "supabase",
//         enabled = IsEnabled,
//         initialized = _initialized,
//         tables = new
//         {
//             nodes = new { name = NodesTable, exists = _nodesTableExists },
//             edges = new { name = EdgesTable, exists = _edgesTableExists }
//         },
//         cache = new
//         {
//             sessions = _cache.Count
//         },
//         config = new
//         {
//             dagEnabled = _config.DagEnabled,
//             url = _config.Url,
//             dagNodesTable = _config.DagNodesTable,
//             dagEdgesTable = _config.DagEdgesTable
//         }
//     };

//     public async Task InitializeAsync(CancellationToken ct = default)
//     {
//         if (_initialized || !IsEnabled) return;

//         try
//         {
//             if (!string.Equals(_config.DagNodesTable, NodesTable, StringComparison.OrdinalIgnoreCase) ||
//                 !string.Equals(_config.DagEdgesTable, EdgesTable, StringComparison.OrdinalIgnoreCase))
//             {
//                 _logger.LogWarning(
//                     "SupabaseGraphStore table names are currently fixed by [Table] attributes. Using '{Nodes}'/'{Edges}' (config='{CfgNodes}'/'{CfgEdges}').",
//                     NodesTable, EdgesTable, _config.DagNodesTable, _config.DagEdgesTable);
//             }

//             var options = new SupabaseOptions { AutoConnectRealtime = false };
//             _client = new Client(_config.Url, _config.Key, options);
//             await _client.InitializeAsync();
//             _initialized = true;

//             _logger.LogInformation("✓ SupabaseGraphStore initialized: {Url}", _config.Url);
//             await CheckTablesAsync(ct);
//         }
//         catch (Exception ex)
//         {
//             _logger.LogError(ex, "✗ Failed to initialize SupabaseGraphStore");
//         }
//     }

//     private async Task CheckTablesAsync(CancellationToken ct)
//     {
//         if (_client == null) return;

//         _nodesTableExists = await CheckTableExistsAsync<DagNodeRecord>(NodesTable, ct);
//         _edgesTableExists = await CheckTableExistsAsync<DagEdgeRecord>(EdgesTable, ct);

//         if (!_nodesTableExists || !_edgesTableExists)
//         {
//             PrintTableCreationSQL();
//         }
//     }

//     private async Task<bool> CheckTableExistsAsync<T>(string tableName, CancellationToken ct) where T : BaseModel, new()
//     {
//         if (_client == null) return false;

//         try
//         {
//             await _client.From<T>().Limit(1).Get();
//             _logger.LogInformation("✓ Table '{Table}' exists", tableName);
//             return true;
//         }
//         catch (Exception ex)
//         {
//             _logger.LogWarning("✗ Table '{Table}' not found: {Message}", tableName, ex.Message);
//             return false;
//         }
//     }

//     private void PrintTableCreationSQL()
//     {
//         var nodes = NodesTable;
//         var edges = EdgesTable;

//         var sql = $"""

//                   ╔══════════════════════════════════════════════════════════════════════════════╗
//                   ║  请在 Supabase SQL Editor 中执行以下 SQL 创建 DAG 表:                        ║
//                   ╚══════════════════════════════════════════════════════════════════════════════╝

//                   -- DAG nodes: (session_id, node_id) 唯一
//                   CREATE TABLE {nodes} (
//                     id UUID DEFAULT gen_random_uuid() PRIMARY KEY,
//                     session_id TEXT NOT NULL,
//                     node_id TEXT NOT NULL,
//                     kind TEXT NOT NULL,
//                     label TEXT,
//                     proof TEXT,
//                     updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
//                     UNIQUE(session_id, node_id)
//                   );

//                   -- DAG edges: (session_id, from_id, to_id, kind) 唯一
//                   CREATE TABLE {edges} (
//                     id UUID DEFAULT gen_random_uuid() PRIMARY KEY,
//                     session_id TEXT NOT NULL,
//                     from_id TEXT NOT NULL,
//                     to_id TEXT NOT NULL,
//                     kind TEXT NOT NULL DEFAULT 'depends_on',
//                     updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
//                     UNIQUE(session_id, from_id, to_id, kind)
//                   );

//                   -- Indexes
//                   CREATE INDEX idx_{nodes}_session ON {nodes}(session_id);
//                   CREATE INDEX idx_{nodes}_node_id ON {nodes}(node_id);
//                   CREATE INDEX idx_{edges}_session ON {edges}(session_id);
//                   CREATE INDEX idx_{edges}_to ON {edges}(session_id, to_id);
//                   CREATE INDEX idx_{edges}_from ON {edges}(session_id, from_id);

//                   -- RLS (允许匿名访问；若对外展示需加鉴权，请自行收紧策略)
//                   ALTER TABLE {nodes} ENABLE ROW LEVEL SECURITY;
//                   CREATE POLICY "Allow anonymous access" ON {nodes}
//                     FOR ALL USING (true) WITH CHECK (true);

//                   ALTER TABLE {edges} ENABLE ROW LEVEL SECURITY;
//                   CREATE POLICY "Allow anonymous access" ON {edges}
//                     FOR ALL USING (true) WITH CHECK (true);

//                   ══════════════════════════════════════════════════════════════════════════════════
//                   """;

//         _logger.LogWarning(sql);
//     }

//     public async Task UpsertFromGraphEventAsync(string sessionId, GraphEvent graphEvent, CancellationToken ct = default)
//     {
//         if (!IsEnabled) return;
//         await InitializeAsync(ct);
//         if (_client == null || !_nodesTableExists || !_edgesTableExists) return;

//         if (string.IsNullOrWhiteSpace(sessionId)) return;

//         // Serialize per session to keep (delete edges -> insert edges) consistent.
//         var gate = _sessionLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
//         await gate.WaitAsync(ct);
//         try
//         {
//             var (nodes, edges) = BuildDag(graphEvent);
//             _cache[sessionId] = new DagSnapshot { SessionId = sessionId, Nodes = nodes, Edges = edges };
//             var now = DateTime.UtcNow;

//             // ─────────────────────────────────────────────
//             //  Nodes UPSERT
//             // ─────────────────────────────────────────────
//             var nodeRecords = nodes.Select(n => new DagNodeRecord
//             {
//                 SessionId = sessionId,
//                 NodeId = n.Id,
//                 Kind = n.Kind.ToString(),
//                 Label = string.IsNullOrWhiteSpace(n.Label) ? null : n.Label,
//                 Proof = string.IsNullOrWhiteSpace(n.Proof) ? null : n.Proof,
//                 UpdatedAt = now
//             }).ToList();

//             if (nodeRecords.Count > 0)
//             {
//                 await UpsertNodesAsync(nodeRecords, ct);
//             }

//             // ─────────────────────────────────────────────
//             //  Edges: rebuild per session (simple + consistent)
//             // ─────────────────────────────────────────────
//             await _client
//                 .From<DagEdgeRecord>()
//                 .Where(e => e.SessionId == sessionId)
//                 .Delete();

//             var edgeRecords = edges.Select(e => new DagEdgeRecord
//             {
//                 SessionId = sessionId,
//                 FromId = e.FromId,
//                 ToId = e.ToId,
//                 Kind = string.IsNullOrWhiteSpace(e.Kind) ? "depends_on" : e.Kind,
//                 UpdatedAt = now
//             }).ToList();

//             if (edgeRecords.Count > 0)
//             {
//                 await _client.From<DagEdgeRecord>().Insert(edgeRecords);
//             }
//         }
//         catch (Exception ex)
//         {
//             _logger.LogWarning(ex, "SupabaseGraphStore upsert failed (session={SessionId})", sessionId);
//         }
//         finally
//         {
//             gate.Release();
//         }
//     }

//     private async Task UpsertNodesAsync(List<DagNodeRecord> nodeRecords, CancellationToken ct)
//     {
//         if (_client == null) return;

//         try
//         {
//             // Prefer server-side UPSERT to avoid N round-trips.
//             // NOTE: requires UNIQUE(session_id, node_id).
//             var options = new Supabase.Postgrest.QueryOptions
//             {
//                 Upsert = true,
//                 OnConflict = "session_id,node_id"
//             };

//             await _client.From<DagNodeRecord>().Insert(nodeRecords, options);
//         }
//         catch (Exception ex)
//         {
//             // Fallback: best-effort update for each node (slower but reliable).
//             _logger.LogWarning(ex, "Node bulk upsert failed, falling back to per-row update");
//             foreach (var n in nodeRecords)
//             {
//                 try
//                 {
//                     await _client
//                         .From<DagNodeRecord>()
//                         .Where(x => x.SessionId == n.SessionId && x.NodeId == n.NodeId)
//                         .Set(x => x.Kind, n.Kind)
//                         .Set(x => x.Label, n.Label)
//                         .Set(x => x.Proof, n.Proof)
//                         .Set(x => x.UpdatedAt, n.UpdatedAt)
//                         .Update();
//                 }
//                 catch
//                 {
//                     // ignore single-row failure
//                 }
//             }
//         }
//     }

//     public async Task<DagSnapshot> GetSnapshotAsync(string sessionId, CancellationToken ct = default)
//     {
//         if (_cache.TryGetValue(sessionId, out var cached))
//             return cached;

//         if (!IsEnabled) return new DagSnapshot { SessionId = sessionId };
//         await InitializeAsync(ct);
//         if (_client == null || !_nodesTableExists || !_edgesTableExists) return new DagSnapshot { SessionId = sessionId };

//         try
//         {
//             // NOTE: if you expect >50k rows, add paging later.
//             var nodesResp = await _client
//                 .From<DagNodeRecord>()
//                 .Where(n => n.SessionId == sessionId)
//                 .Limit(50_000)
//                 .Get();

//             var edgesResp = await _client
//                 .From<DagEdgeRecord>()
//                 .Where(e => e.SessionId == sessionId)
//                 .Limit(200_000)
//                 .Get();

//             var nodes = nodesResp.Models.Select(r => new DagNode
//             {
//                 Id = r.NodeId,
//                 Kind = ParseKind(r.Kind),
//                 Label = r.Label ?? "",
//                 Proof = r.Proof ?? "",
//                 UpdatedAt = r.UpdatedAt
//             }).ToList();

//             var edges = edgesResp.Models.Select(r => new DagEdge
//             {
//                 FromId = r.FromId,
//                 ToId = r.ToId,
//                 Kind = string.IsNullOrWhiteSpace(r.Kind) ? "depends_on" : r.Kind
//             }).ToList();

//             var snapshot = new DagSnapshot { SessionId = sessionId, Nodes = nodes, Edges = edges };
//             _cache[sessionId] = snapshot;
//             return snapshot;
//         }
//         catch (Exception ex)
//         {
//             _logger.LogWarning(ex, "Failed to load DAG from Supabase (session={SessionId})", sessionId);
//             return new DagSnapshot { SessionId = sessionId };
//         }
//     }

//     public async Task<DagExplainResult> ExplainAsync(string sessionId, string nodeId, CancellationToken ct = default)
//     {
//         var snapshot = await GetSnapshotAsync(sessionId, ct);
//         return ExplainFromSnapshot(snapshot, nodeId);
//     }

//     // ============================================================
//     //  Pure functions (DB-agnostic reasoning)
//     // ============================================================

//     private static (List<DagNode> nodes, List<DagEdge> edges) BuildDag(GraphEvent graphEvent)
//     {
//         var nodes = new Dictionary<string, DagNode>(StringComparer.Ordinal);
//         var edges = new Dictionary<string, DagEdge>(StringComparer.Ordinal);

//         var axiomIds = new HashSet<string>(StringComparer.Ordinal);
//         for (var i = 0; i < (graphEvent.Axioms?.Count ?? 0); i++)
//         {
//             var line = graphEvent.Axioms[i] ?? "";
//             var id = ExtractAxiomId(line, i);
//             axiomIds.Add(id);
//             nodes[id] = new DagNode
//             {
//                 Id = id,
//                 Kind = DagNodeKind.Axiom,
//                 Label = line,
//                 UpdatedAt = DateTimeOffset.UtcNow
//             };
//         }

//         var assumptionIds = new HashSet<string>(StringComparer.Ordinal);
//         foreach (var a in graphEvent.Assumptions ?? [])
//         {
//             var id = (a.Id ?? "").Trim();
//             if (string.IsNullOrWhiteSpace(id)) continue;
//             assumptionIds.Add(id);
//             nodes[id] = new DagNode
//             {
//                 Id = id,
//                 Kind = DagNodeKind.Assumption,
//                 Label = a.Statement ?? "",
//                 Proof = a.Motivation ?? "",
//                 UpdatedAt = DateTimeOffset.UtcNow
//             };
//         }

//         var theoremIds = new HashSet<string>(StringComparer.Ordinal);
//         foreach (var t in graphEvent.Theorems ?? [])
//         {
//             var id = (t.Id ?? "").Trim();
//             if (string.IsNullOrWhiteSpace(id)) continue;
//             theoremIds.Add(id);
//             nodes[id] = new DagNode
//             {
//                 Id = id,
//                 Kind = DagNodeKind.Theorem,
//                 Label = t.Statement ?? "",
//                 Proof = t.Proof ?? "",
//                 UpdatedAt = DateTimeOffset.UtcNow
//             };
//         }

//         foreach (var t in graphEvent.Theorems ?? [])
//         {
//             var toId = (t.Id ?? "").Trim();
//             if (string.IsNullOrWhiteSpace(toId)) continue;

//             foreach (var depRaw in t.DependsOn ?? [])
//             {
//                 var fromId = (depRaw ?? "").Trim();
//                 if (string.IsNullOrWhiteSpace(fromId)) continue;

//                 if (!nodes.ContainsKey(fromId))
//                 {
//                     var kind = axiomIds.Contains(fromId)
//                         ? DagNodeKind.Axiom
//                         : assumptionIds.Contains(fromId)
//                             ? DagNodeKind.Assumption
//                         : theoremIds.Contains(fromId)
//                             ? DagNodeKind.Theorem
//                             : DagNodeKind.Hypothesis;

//                     nodes[fromId] = new DagNode
//                     {
//                         Id = fromId,
//                         Kind = kind,
//                         Label = fromId,
//                         UpdatedAt = DateTimeOffset.UtcNow
//                     };
//                 }

//                 var k = $"{fromId}->{toId}";
//                 edges[k] = new DagEdge { FromId = fromId, ToId = toId, Kind = "depends_on" };
//             }
//         }

//         return (nodes.Values.ToList(), edges.Values.ToList());
//     }

//     private static DagExplainResult ExplainFromSnapshot(DagSnapshot snapshot, string nodeId)
//     {
//         var byId = snapshot.Nodes.ToDictionary(n => n.Id, n => n, StringComparer.Ordinal);
//         byId.TryGetValue(nodeId, out var node);

//         // incoming adjacency: to -> [from]
//         var deps = new Dictionary<string, List<string>>(StringComparer.Ordinal);
//         foreach (var e in snapshot.Edges)
//         {
//             if (!deps.TryGetValue(e.ToId, out var list))
//             {
//                 list = new List<string>();
//                 deps[e.ToId] = list;
//             }
//             list.Add(e.FromId);
//         }

//         var direct = deps.TryGetValue(nodeId, out var d0)
//             ? d0.Distinct().OrderBy(x => x).ToList()
//             : [];

//         var visited = new HashSet<string>(StringComparer.Ordinal);
//         var stack = new HashSet<string>(StringComparer.Ordinal);
//         var topo = new List<string>();
//         var hasCycle = false;

//         void Dfs(string cur)
//         {
//             if (hasCycle) return;
//             if (stack.Contains(cur))
//             {
//                 hasCycle = true;
//                 return;
//             }
//             if (visited.Contains(cur)) return;

//             visited.Add(cur);
//             stack.Add(cur);

//             if (deps.TryGetValue(cur, out var prev))
//             {
//                 foreach (var p in prev)
//                 {
//                     Dfs(p);
//                     if (hasCycle) return;
//                 }
//             }

//             stack.Remove(cur);
//             topo.Add(cur);
//         }

//         if (!string.IsNullOrWhiteSpace(nodeId))
//             Dfs(nodeId);

//         var missing = new List<DagNode>();
//         foreach (var id in visited)
//         {
//             if (string.Equals(id, nodeId, StringComparison.Ordinal)) continue;
//             if (!byId.TryGetValue(id, out var n2)) continue;
//             if (n2.Kind is DagNodeKind.Hypothesis or DagNodeKind.Assumption or DagNodeKind.Unknown)
//                 missing.Add(n2);
//         }

//         var provable = node != null && !hasCycle && missing.Count == 0;
//         topo.Reverse();

//         return new DagExplainResult
//         {
//             SessionId = snapshot.SessionId,
//             Node = node,
//             DirectDependencies = direct,
//             TopologicalOrder = topo,
//             HasCycle = hasCycle,
//             ProvableFromAxioms = provable,
//             MissingDependencies = missing
//         };
//     }

//     private static DagNodeKind ParseKind(string? kind)
//     {
//         if (string.IsNullOrWhiteSpace(kind)) return DagNodeKind.Unknown;
//         return Enum.TryParse<DagNodeKind>(kind, ignoreCase: true, out var k) ? k : DagNodeKind.Unknown;
//     }

//     private static string ExtractAxiomId(string line, int idx)
//     {
//         if (string.IsNullOrWhiteSpace(line)) return $"A{idx + 1}";
//         var m = AxiomIdRegex.Match(line.Trim());
//         return m.Success ? m.Groups[1].Value : $"A{idx + 1}";
//     }
// }