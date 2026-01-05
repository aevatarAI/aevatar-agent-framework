namespace Aevatar.Agents.Abstractions;

public static class AevatarAgentsConstants
{
    /// <summary>
    /// Only temperately use.
    /// </summary>
    public const string DefaultProviderName = "deepseek";

    /// <summary>
    /// Unified execution trace output root directory.
    /// If set, the framework may export <c>ExecutionTrace</c> bundles under this directory.
    /// </summary>
    public const string TraceDirEnv = "AEVATAR_TRACE_DIR";

    /// <summary>
    /// Unified memory store output root directory (file-based store).
    /// If set, the framework may persist <c>MemoryEntry</c> bundles under this directory (best-effort).
    /// </summary>
    public const string MemoryDirEnv = "AEVATAR_MEMORY_DIR";

    /// <summary>
    /// Unified memory vector index output root directory (file-based index).
    /// If set, the framework may persist <c>MemoryVectorRecord</c> bundles under this directory (best-effort).
    /// </summary>
    public const string MemoryVectorDirEnv = "AEVATAR_MEMORY_VECTOR_DIR";
}