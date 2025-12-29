namespace Aevatar.Agents.Persistence.Graph.Providers.Neo4j;

/// <summary>
/// Neo4j 持久化连接选项。
/// </summary>
public sealed class Neo4jPersistenceOptions
{
    /// <summary>
    /// Neo4j 连接 URI（bolt/neo4j 协议）。
    /// 例：bolt://localhost:7687 或 neo4j+s://demo.neo4jlabs.com
    /// </summary>
    public string Uri { get; set; } = string.Empty;

    /// <summary>
    /// 基本认证用户名。
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// 基本认证密码。
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// 目标数据库名称，默认 "neo4j"。
    /// </summary>
    public string Database { get; set; } = "neo4j";
}
