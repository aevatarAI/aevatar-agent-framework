using Microsoft.Extensions.Options;
using Supabase;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace Aevatar.AxiomReasoning.Services;

// ============================================================
//  SUPABASE SERVICE
//  定理产物持久化 - 直接存 JSON（state.json / theorems.json）
//
//  设计说明：
//  - 只做“会话级别”的持久化：每个 session 一行记录。
//  - theorems/state 以 TEXT 存储 JSON 字符串，避免 jsonb 映射细节导致的 SDK 兼容问题。
//  - 对外展示的“最短证明链”后续可以在读取 theorems_json 后做图算法/预计算。
// ============================================================

/// <summary>
/// Supabase 配置。
/// </summary>
public sealed class SupabaseConfig
{
    public const string SectionName = "Supabase";

    /// <summary>Supabase 项目 URL</summary>
    public string Url { get; set; } = "";

    /// <summary>Supabase 公钥 (anon key)</summary>
    public string Key { get; set; } = "";

    /// <summary>存储推理结果的表名</summary>
    public string ResultsTable { get; set; } = "axiom_reasoning_results";

    /// <summary>
    /// 存储 DAG 节点的表名。
    /// NOTE: 当前 Supabase .NET SDK 的 [Table] 映射为编译期固定表名，建议保持默认值不改。
    /// </summary>
    public string DagNodesTable { get; set; } = "axiom_reasoning_dag_nodes";

    /// <summary>
    /// 存储 DAG 边的表名。
    /// NOTE: 当前 Supabase .NET SDK 的 [Table] 映射为编译期固定表名，建议保持默认值不改。
    /// </summary>
    public string DagEdgesTable { get; set; } = "axiom_reasoning_dag_edges";

    /// <summary>是否启用 DAG 持久化（GraphStore）</summary>
    public bool DagEnabled { get; set; } = false;

    /// <summary>
    /// 兼容 PaperReview 的历史字段名（ReviewsTable）。
    /// </summary>
    public string ReviewsTable
    {
        get => ResultsTable;
        set => ResultsTable = value;
    }

    /// <summary>是否启用 Supabase</summary>
    public bool Enabled { get; set; } = false;
}

/// <summary>
/// 推理结果记录（对应 Supabase 表结构）。
/// </summary>
[Table("axiom_reasoning_results")]
public sealed class AxiomReasoningResultRecord : BaseModel
{
    [PrimaryKey("id")]
    public string? Id { get; set; }

    [Column("session_id")]
    public string SessionId { get; set; } = "";

    [Column("axioms_text")]
    public string? AxiomsText { get; set; }

    [Column("goal")]
    public string? Goal { get; set; }

    [Column("status")]
    public string Status { get; set; } = "";

    // JSON artifacts (stored as TEXT)
    [Column("state_json")]
    public string? StateJson { get; set; }

    [Column("theorems_json")]
    public string? TheoremsJson { get; set; }

    // raw result (optional)
    [Column("content")]
    public string? Content { get; set; }

    [Column("error")]
    public string? Error { get; set; }

    [Column("llm_calls")]
    public int LlmCalls { get; set; }

    [Column("total_tokens")]
    public long TotalTokens { get; set; }

    [Column("duration_seconds")]
    public double DurationSeconds { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// Supabase 服务 - 处理推理产物的持久化。
/// </summary>
public sealed class SupabaseService
{
    private readonly SupabaseConfig _config;
    private readonly ILogger<SupabaseService> _logger;
    private Client? _client;
    private bool _initialized;
    private bool _tableExists;

    public SupabaseService(
        IOptions<SupabaseConfig> config,
        ILogger<SupabaseService> logger)
    {
        _config = config.Value;
        _logger = logger;
    }

    /// <summary>
    /// 是否启用 Supabase。
    /// </summary>
    public bool IsEnabled =>
        _config.Enabled &&
        !string.IsNullOrWhiteSpace(_config.Url) &&
        !string.IsNullOrWhiteSpace(_config.Key);

    /// <summary>
    /// 初始化 Supabase 客户端，并检查表是否存在。
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized || !IsEnabled) return;

        try
        {
            var options = new SupabaseOptions
            {
                AutoConnectRealtime = false
            };

            _client = new Client(_config.Url, _config.Key, options);
            await _client.InitializeAsync();
            _initialized = true;

            _logger.LogInformation("✓ Supabase client initialized: {Url}", _config.Url);

            await CheckTableExistsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "✗ Failed to initialize Supabase");
        }
    }

    private async Task CheckTableExistsAsync()
    {
        if (_client == null) return;

        try
        {
            // 尝试查询一条记录来验证表存在
            await _client.From<AxiomReasoningResultRecord>().Limit(1).Get();
            _tableExists = true;
            _logger.LogInformation("✓ Table '{Table}' exists", _config.ResultsTable);
        }
        catch (Exception ex)
        {
            _tableExists = false;
            _logger.LogWarning("✗ Table '{Table}' not found: {Message}", _config.ResultsTable, ex.Message);
            PrintTableCreationSQL();
        }
    }

    private void PrintTableCreationSQL()
    {
        var sql = $"""

                  ╔══════════════════════════════════════════════════════════════════════════════╗
                  ║  请在 Supabase SQL Editor 中执行以下 SQL 创建表:                             ║
                  ╚══════════════════════════════════════════════════════════════════════════════╝

                  CREATE TABLE {_config.ResultsTable} (
                    id UUID DEFAULT gen_random_uuid() PRIMARY KEY,
                    session_id TEXT NOT NULL UNIQUE,
                    axioms_text TEXT,
                    goal TEXT,
                    status TEXT NOT NULL,
                    state_json TEXT,
                    theorems_json TEXT,
                    content TEXT,
                    error TEXT,
                    llm_calls INTEGER DEFAULT 0,
                    total_tokens BIGINT DEFAULT 0,
                    duration_seconds DOUBLE PRECISION DEFAULT 0,
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
                    completed_at TIMESTAMP WITH TIME ZONE
                  );

                  -- 索引
                  CREATE INDEX idx_{_config.ResultsTable}_session_id ON {_config.ResultsTable}(session_id);
                  CREATE INDEX idx_{_config.ResultsTable}_created_at ON {_config.ResultsTable}(created_at DESC);

                  -- RLS (允许匿名访问；若对外展示需加鉴权，请自行收紧策略)
                  ALTER TABLE {_config.ResultsTable} ENABLE ROW LEVEL SECURITY;
                  CREATE POLICY "Allow anonymous access" ON {_config.ResultsTable}
                    FOR ALL USING (true) WITH CHECK (true);

                  ══════════════════════════════════════════════════════════════════════════════════
                  """;

        _logger.LogWarning(sql);
    }

    /// <summary>
    /// 保存推理结果（best-effort）。
    /// </summary>
    public async Task<string?> SaveResultAsync(
        string sessionId,
        string axiomsText,
        string goal,
        string status,
        string? stateJson,
        string? theoremsJson,
        string? content,
        string? error,
        int llmCalls,
        long totalTokens,
        double durationSeconds)
    {
        if (!IsEnabled || _client == null)
        {
            _logger.LogDebug("Supabase not enabled, skipping save");
            return null;
        }

        if (!_tableExists)
        {
            _logger.LogWarning("Table not exists, skipping save for session {SessionId}", sessionId);
            return null;
        }

        try
        {
            var record = new AxiomReasoningResultRecord
            {
                SessionId = sessionId,
                AxiomsText = axiomsText,
                Goal = goal,
                Status = status,
                StateJson = stateJson,
                TheoremsJson = theoremsJson,
                Content = content,
                Error = error,
                LlmCalls = llmCalls,
                TotalTokens = totalTokens,
                DurationSeconds = durationSeconds,
                CreatedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            };

            _logger.LogInformation("Saving axiom reasoning result to Supabase: {SessionId}", sessionId);
            var response = await _client.From<AxiomReasoningResultRecord>().Insert(record);

            var inserted = response.Models.FirstOrDefault();
            if (inserted != null)
            {
                _logger.LogInformation("✓ Saved axiom reasoning result: {SessionId} -> {Id}", sessionId, inserted.Id);
                return inserted.Id;
            }

            _logger.LogWarning("Insert returned no models for session {SessionId}", sessionId);
            return null;
        }
        catch (Exception ex)
        {
            // 常见：session_id UNIQUE 冲突（重复写入）
            _logger.LogWarning(ex, "Insert failed (maybe duplicate), trying update: {SessionId}", sessionId);
            await TryUpdateResultAsync(
                sessionId,
                status,
                stateJson,
                theoremsJson,
                content,
                error,
                llmCalls,
                totalTokens,
                durationSeconds);
            return null;
        }
    }

    /// <summary>
    /// 更新推理结果（best-effort）。
    /// </summary>
    public async Task TryUpdateResultAsync(
        string sessionId,
        string status,
        string? stateJson,
        string? theoremsJson,
        string? content,
        string? error,
        int llmCalls,
        long totalTokens,
        double durationSeconds)
    {
        if (!IsEnabled || _client == null || !_tableExists) return;

        try
        {
            await _client
                .From<AxiomReasoningResultRecord>()
                .Where(r => r.SessionId == sessionId)
                .Set(r => r.Status, status)
                .Set(r => r.StateJson, stateJson)
                .Set(r => r.TheoremsJson, theoremsJson)
                .Set(r => r.Content, content)
                .Set(r => r.Error, error)
                .Set(r => r.LlmCalls, llmCalls)
                .Set(r => r.TotalTokens, totalTokens)
                .Set(r => r.DurationSeconds, durationSeconds)
                .Set(r => r.CompletedAt, DateTime.UtcNow)
                .Update();

            _logger.LogInformation("✓ Updated axiom reasoning result: {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "✗ Failed to update axiom reasoning result: {SessionId}", sessionId);
        }
    }

    public object GetDiagnostics() => new
    {
        enabled = IsEnabled,
        initialized = _initialized,
        tableExists = _tableExists,
        url = _config.Url,
        table = _config.ResultsTable
    };
}


