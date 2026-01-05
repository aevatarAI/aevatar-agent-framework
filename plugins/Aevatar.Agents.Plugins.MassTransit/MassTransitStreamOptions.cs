using System;
using System.Collections.Generic;

namespace Aevatar.Agents.Plugins.MassTransit;

/// <summary>
/// MassTransit Stream 配置选项
/// </summary>
public class MassTransitStreamOptions
{
    /// <summary>
    /// Topic 前缀（用于生成 Agent 的 Topic，作为默认 Topic）
    /// </summary>
    public string TopicPrefix { get; set; } = "agent-events";

    /// <summary>
    /// 动态 Topic 映射表（用于 Producer 路由）
    /// Key: Category (Agent Type Name)
    /// Value: Kafka Topic Name
    /// </summary>
    public Dictionary<string, string> TopicMapping { get; set; } = new();

    /// <summary>
    /// Producer 配置
    /// </summary>
    public ProducerOptions Producer { get; set; } = new();

    /// <summary>
    /// Consumer 配置
    /// </summary>
    public ConsumerOptions Consumer { get; set; } = new();

    /// <summary>
    /// 传输方式：InMemory, Kafka, RabbitMQ
    /// </summary>
    public MassTransitTransportType TransportType { get; set; } = MassTransitTransportType.InMemory;

    /// <summary>
    /// 当前 Runtime 的名称，用于 Topic 命名区分
    /// </summary>
    public string RuntimeName { get; set; } = "Default";

    /// <summary>
    /// Kafka 配置（当 TransportType = Kafka 时使用）
    /// </summary>
    public KafkaOptions? Kafka { get; set; }

    /// <summary>
    /// RabbitMQ 配置（当 TransportType = RabbitMQ 时使用）
    /// </summary>
    public RabbitMQOptions? RabbitMQ { get; set; }
}

/// <summary>
/// Producer 配置
/// </summary>
public class ProducerOptions
{
    /// <summary>
    /// 是否启用 Producer（默认 true）
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Consumer 配置
/// </summary>
public class ConsumerOptions
{
    /// <summary>
    /// 是否启用 Consumer（默认 true）
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Consumer 订阅的 Topics 列表
    /// 如果为空，Silo 会自动扫描 [StreamTopic] 属性添加
    /// </summary>
    public List<string> Topics { get; set; } = new();

    /// <summary>
    /// 是否自动添加 TopicPrefix 到订阅列表（默认 true）
    /// </summary>
    public bool IncludeTopicPrefix { get; set; } = true;

    /// <summary>
    /// 是否自动扫描 [StreamTopic] 属性添加到订阅列表（默认 true，仅 Silo 端有效）
    /// </summary>
    public bool AutoScanAgentTopics { get; set; } = true;

    /// <summary>
    /// Dispatch handler type for message routing
    /// - GrainHandler: Dispatch to Grain handlers (Silo, best performance)
    /// - LocalHandler: Dispatch to local memory streams (HttpApi Client)
    /// </summary>
    public DispatchHandler DispatchHandler { get; set; } = DispatchHandler.GrainHandler;
}

/// <summary>
/// Dispatch handler type for MassTransit message routing
/// </summary>
public enum DispatchHandler
{
    /// <summary>
    /// Dispatch to Grain handlers (Orleans/ProtoActor actors) - for Silo
    /// </summary>
    GrainHandler,
    
    /// <summary>
    /// Dispatch to local memory stream subscribers - for HttpApi Client
    /// </summary>
    LocalHandler
}

public enum MassTransitTransportType
{
    InMemory,
    Kafka,
    RabbitMQ
}

public class KafkaOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string ConsumerGroupId { get; set; } = "aevatar-agents";
    // 可以添加更多 Kafka Producer/Consumer 配置
}

public class RabbitMQOptions
{
    public string Host { get; set; } = "localhost";
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    // 可以添加更多 RabbitMQ 配置
}
