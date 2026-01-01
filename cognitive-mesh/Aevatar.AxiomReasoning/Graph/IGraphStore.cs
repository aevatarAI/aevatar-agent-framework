using Aevatar.AxiomReasoning.EventStreaming.Events;
using Aevatar.AxiomReasoning.Graph.Models;

namespace Aevatar.AxiomReasoning.Graph;

// ============================================================
//  GRAPH STORE ABSTRACTION
//
//  WHY:
//  - 先用 InMemory（开发/演示最快）
//  - 需要落盘时切换 Supabase（之后可迁移 Neo4j）
//
//  约束：
//  - 只存“原子事实”：nodes / edges
//  - 推理结果（closure / provable）由应用层计算，避免写死到存储
// ============================================================

public interface IGraphStore
{
    Task UpsertFromGraphEventAsync(string sessionId, GraphEvent graphEvent, CancellationToken ct = default);

    Task<Snapshot> GetSnapshotAsync(string sessionId, CancellationToken ct = default);

    Task<ExplainResult> ExplainAsync(string sessionId, string nodeId, CancellationToken ct = default);
}


