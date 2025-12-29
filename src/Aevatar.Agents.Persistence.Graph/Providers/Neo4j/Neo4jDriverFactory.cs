using Neo4j.Driver;

namespace Aevatar.Agents.Persistence.Graph.Providers.Neo4j;

/// <summary>
/// 默认的 Neo4j Driver 工厂。
/// </summary>
public sealed class Neo4jDriverFactory : INeo4jDriverFactory
{
    /// <summary>
    /// 根据提供的选项创建 <see cref="IDriver" />。
    /// </summary>
    /// <param name="options">包含连接 URI、用户名、密码的配置。</param>
    /// <returns>新建的 Neo4j Driver 实例。</returns>
    /// <exception cref="ArgumentException">配置缺失或格式无效时抛出。</exception>
    public IDriver Create(Neo4jPersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Validate(options);

        var uri = new Uri(options.Uri, UriKind.Absolute);
        return GraphDatabase.Driver(uri, AuthTokens.Basic(options.Username, options.Password));
    }

    private static void Validate(Neo4jPersistenceOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Uri))
        {
            throw new ArgumentException("Neo4j Uri is required.", nameof(options));
        }

        if (!Uri.IsWellFormedUriString(options.Uri, UriKind.Absolute))
        {
            throw new ArgumentException("Neo4j Uri format is invalid.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.Username))
        {
            throw new ArgumentException("Neo4j Username is required.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.Password))
        {
            throw new ArgumentException("Neo4j Password is required.", nameof(options));
        }
    }
}
