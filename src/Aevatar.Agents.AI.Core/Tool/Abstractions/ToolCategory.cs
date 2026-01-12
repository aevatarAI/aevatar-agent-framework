namespace Aevatar.Agents.AI.Tool.Abstractions;

/// <summary>
/// Tool category
/// </summary>
public enum ToolCategory
{
    /// <summary>
    /// Core system tools (events, state, lifecycle management)
    /// Examples: PublishEvent, UpdateState, GetAgentInfo
    /// </summary>
    Core,
    
    /// <summary>
    /// Memory management tools
    /// Examples: StoreMemory, RetrieveMemory, SearchSemanticMemory
    /// </summary>
    Memory,
    
    /// <summary>
    /// Communication and messaging tools
    /// Examples: SendMessage, BroadcastEvent, CallAPI
    /// </summary>
    Communication,
    
    /// <summary>
    /// Data processing and transformation tools
    /// Examples: ParseJSON, TransformData, AggregateResults
    /// </summary>
    DataProcessing,
    
    /// <summary>
    /// External system integration tools
    /// Examples: ConnectDatabase, CallWebService, SyncData
    /// </summary>
    Integration,
    
    /// <summary>
    /// Information retrieval and query tools
    /// Examples: GetWeather, SearchWeb, QueryDatabase
    /// </summary>
    Information,
    
    /// <summary>
    /// Utility calculation tools
    /// Examples: Calculate, ConvertUnits, FormatText
    /// </summary>
    Utility,
    
    /// <summary>
    /// Analytics and insight tools
    /// Examples: AnalyzeData, GenerateReport, PredictTrend
    /// </summary>
    Analytics,
    
    /// <summary>
    /// Security and validation tools
    /// Examples: ValidateInput, CheckPermissions, EncryptData
    /// </summary>
    Security,
    
    /// <summary>
    /// Monitoring and observability tools
    /// Examples: LogEvent, TrackMetric, CreateAlert
    /// </summary>
    Monitoring,
    
    /// <summary>
    /// Workflow and orchestration tools
    /// Examples: StartWorkflow, WaitForCondition, Parallelize
    /// </summary>
    Orchestration,
    
    /// <summary>
    /// Custom business tools
    /// </summary>
    Custom
}