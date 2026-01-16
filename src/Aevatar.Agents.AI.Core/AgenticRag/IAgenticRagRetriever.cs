using Aevatar.Agents.AI.Core.Messages;

namespace Aevatar.Agents.AI.Core.AgenticRag;

// ============================================================
//  Agentic RAG - Retriever Abstraction (DI boundary)
//
//  中文 + ASCII:
//  - Retriever 是“数据获取”的边界：向量库/全文检索/DB/Web 都可以藏在实现里。
//  - 这里返回的 EvidenceSummary 是 Protobuf 类型，方便后续跨边界输出（引用/审计）。
// ============================================================

public interface IAgenticRagRetriever
{
    /// <summary>
    /// Stable name for diagnostics/logging.
    /// </summary>
    string Name { get; }

    Task<IReadOnlyList<RagEvidenceSummary>> RetrieveAsync(
        RagRetrieveRequest request,
        CancellationToken cancellationToken);
}


