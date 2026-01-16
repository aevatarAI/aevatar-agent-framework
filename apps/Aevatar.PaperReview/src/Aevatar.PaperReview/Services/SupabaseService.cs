using System.Text.Json;
using Microsoft.Extensions.Options;
using Supabase;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace Aevatar.PaperReview.Services;

// ============================================================
//  SUPABASE SERVICE
//  评审结果持久化 - 上传到 Supabase
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
    
    /// <summary>存储评审结果的表名</summary>
    public string ReviewsTable { get; set; } = "paper_reviews";
    
    /// <summary>存储 bucket 名称</summary>
    public string StorageBucket { get; set; } = "reviews";
    
    /// <summary>是否启用 Supabase</summary>
    public bool Enabled { get; set; } = false;
}

/// <summary>
/// 评审结果记录（对应 Supabase 表结构）。
/// </summary>
[Table("paper_reviews")]
public sealed class ReviewRecord : BaseModel
{
    [PrimaryKey("id")]
    public string? Id { get; set; }
    
    [Column("session_id")]
    public string SessionId { get; set; } = "";
    
    [Column("paper_title")]
    public string PaperTitle { get; set; } = "";
    
    [Column("authors")]
    public string? Authors { get; set; }
    
    [Column("review_type")]
    public string ReviewType { get; set; } = "";
    
    [Column("venue_type")]
    public string VenueType { get; set; } = "";
    
    [Column("status")]
    public string Status { get; set; } = "";
    
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
    
    [Column("report_url")]
    public string? ReportUrl { get; set; }
    
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// Supabase 服务 - 处理评审结果的持久化。
/// </summary>
public sealed class SupabaseService
{
    private readonly SupabaseConfig _config;
    private readonly ILogger<SupabaseService> _logger;
    private Client? _client;
    private bool _initialized;
    private bool _tableExists;
    private bool _bucketExists;

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
    public bool IsEnabled => _config.Enabled && !string.IsNullOrEmpty(_config.Url) && !string.IsNullOrEmpty(_config.Key);

    /// <summary>
    /// 初始化 Supabase 客户端，并自动创建所需资源。
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

            // 自动创建 Bucket
            await EnsureBucketExistsAsync();

            // 检查表是否存在
            await CheckTableExistsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "✗ Failed to initialize Supabase");
        }
    }

    /// <summary>
    /// 确保 Storage Bucket 存在，不存在则自动创建。
    /// </summary>
    private async Task EnsureBucketExistsAsync()
    {
        if (_client == null) return;

        try
        {
            // 尝试列出 buckets
            var buckets = await _client.Storage.ListBuckets();
            _bucketExists = buckets.Any(b => b.Id == _config.StorageBucket);

            if (_bucketExists)
            {
                _logger.LogInformation("✓ Storage bucket '{Bucket}' exists", _config.StorageBucket);
                return;
            }

            // 尝试创建 bucket
            _logger.LogInformation("Creating storage bucket '{Bucket}'...", _config.StorageBucket);
            
            await _client.Storage.CreateBucket(_config.StorageBucket, new Supabase.Storage.BucketUpsertOptions
            {
                Public = true
            });
            
            _bucketExists = true;
            _logger.LogInformation("✓ Storage bucket '{Bucket}' created successfully", _config.StorageBucket);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("✗ Could not create bucket '{Bucket}': {Message}", _config.StorageBucket, ex.Message);
            _logger.LogWarning("  → Please create it manually in Supabase Dashboard → Storage → New bucket");
            _logger.LogWarning("  → Name: {Bucket}, Public: true", _config.StorageBucket);
        }
    }

    /// <summary>
    /// 检查数据库表是否存在。
    /// </summary>
    private async Task CheckTableExistsAsync()
    {
        if (_client == null) return;

        try
        {
            // 尝试查询一条记录来验证表存在
            await _client.From<ReviewRecord>().Limit(1).Get();
            _tableExists = true;
            _logger.LogInformation("✓ Table '{Table}' exists", _config.ReviewsTable);
        }
        catch (Exception ex)
        {
            _tableExists = false;
            _logger.LogWarning("✗ Table '{Table}' not found: {Message}", _config.ReviewsTable, ex.Message);
            PrintTableCreationSQL();
        }
    }

    /// <summary>
    /// 打印建表 SQL。
    /// </summary>
    private void PrintTableCreationSQL()
    {
        var sql = $"""
            
            ╔══════════════════════════════════════════════════════════════════════════════╗
            ║  请在 Supabase SQL Editor 中执行以下 SQL 创建表:                             ║
            ╚══════════════════════════════════════════════════════════════════════════════╝
            
            CREATE TABLE {_config.ReviewsTable} (
              id UUID DEFAULT gen_random_uuid() PRIMARY KEY,
              session_id TEXT NOT NULL UNIQUE,
              paper_title TEXT NOT NULL,
              authors TEXT,
              review_type TEXT NOT NULL,
              venue_type TEXT NOT NULL,
              status TEXT NOT NULL,
              content TEXT,
              error TEXT,
              llm_calls INTEGER DEFAULT 0,
              total_tokens BIGINT DEFAULT 0,
              duration_seconds DOUBLE PRECISION DEFAULT 0,
              report_url TEXT,
              created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
              completed_at TIMESTAMP WITH TIME ZONE
            );
            
            -- 索引
            CREATE INDEX idx_{_config.ReviewsTable}_session_id ON {_config.ReviewsTable}(session_id);
            CREATE INDEX idx_{_config.ReviewsTable}_created_at ON {_config.ReviewsTable}(created_at DESC);
            
            -- RLS (允许匿名访问)
            ALTER TABLE {_config.ReviewsTable} ENABLE ROW LEVEL SECURITY;
            CREATE POLICY "Allow anonymous access" ON {_config.ReviewsTable}
              FOR ALL USING (true) WITH CHECK (true);
            
            ══════════════════════════════════════════════════════════════════════════════════
            """;
        
        _logger.LogWarning(sql);
    }

    /// <summary>
    /// 保存评审结果到数据库。
    /// </summary>
    public async Task<string?> SaveReviewAsync(
        string sessionId,
        string paperTitle,
        string? authors,
        string reviewType,
        string venueType,
        string status,
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
            var record = new ReviewRecord
            {
                SessionId = sessionId,
                PaperTitle = paperTitle,
                Authors = authors,
                ReviewType = reviewType,
                VenueType = venueType,
                Status = status,
                Content = content,
                Error = error,
                LlmCalls = llmCalls,
                TotalTokens = totalTokens,
                DurationSeconds = durationSeconds,
                CreatedAt = DateTime.UtcNow,
                CompletedAt = status == "Completed" || status == "Failed" ? DateTime.UtcNow : null
            };

            _logger.LogInformation("Saving review to Supabase: {SessionId}", sessionId);
            var response = await _client.From<ReviewRecord>().Insert(record);

            var inserted = response.Models.FirstOrDefault();
            if (inserted != null)
            {
                _logger.LogInformation("✓ Saved review to Supabase: {SessionId} -> {Id}", sessionId, inserted.Id);
                return inserted.Id;
            }

            _logger.LogWarning("Insert returned no models for session {SessionId}", sessionId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "✗ Failed to save review to Supabase: {SessionId}", sessionId);
            return null;
        }
    }

    /// <summary>
    /// 更新评审结果。
    /// </summary>
    public async Task UpdateReviewAsync(
        string sessionId,
        string status,
        string? content,
        string? error,
        int llmCalls,
        long totalTokens,
        double durationSeconds,
        string? reportUrl = null)
    {
        if (!IsEnabled || _client == null || !_tableExists) return;

        try
        {
            _logger.LogInformation("Updating review in Supabase: {SessionId}", sessionId);
            
            await _client
                .From<ReviewRecord>()
                .Where(r => r.SessionId == sessionId)
                .Set(r => r.Status, status)
                .Set(r => r.Content, content)
                .Set(r => r.Error, error)
                .Set(r => r.LlmCalls, llmCalls)
                .Set(r => r.TotalTokens, totalTokens)
                .Set(r => r.DurationSeconds, durationSeconds)
                .Set(r => r.ReportUrl, reportUrl)
                .Set(r => r.CompletedAt, DateTime.UtcNow)
                .Update();

            _logger.LogInformation("✓ Updated review in Supabase: {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "✗ Failed to update review in Supabase: {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// 上传报告文件到 Storage。
    /// </summary>
    public async Task<string?> UploadReportAsync(string sessionId, string content)
    {
        if (!IsEnabled || _client == null || !_bucketExists) return null;

        try
        {
            var fileName = $"{sessionId}/review_report.md";
            var bytes = System.Text.Encoding.UTF8.GetBytes(content);

            _logger.LogInformation("Uploading report to Supabase Storage: {FileName}", fileName);

            await _client.Storage
                .From(_config.StorageBucket)
                .Upload(bytes, fileName, new Supabase.Storage.FileOptions
                {
                    ContentType = "text/markdown",
                    Upsert = true
                });

            var publicUrl = _client.Storage
                .From(_config.StorageBucket)
                .GetPublicUrl(fileName);

            _logger.LogInformation("✓ Uploaded report to Supabase Storage: {Url}", publicUrl);
            return publicUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "✗ Failed to upload report to Supabase: {SessionId}", sessionId);
            return null;
        }
    }

    /// <summary>
    /// 获取历史评审记录。
    /// </summary>
    public async Task<IEnumerable<ReviewRecord>> GetReviewsAsync(int limit = 50)
    {
        if (!IsEnabled || _client == null || !_tableExists) return [];

        try
        {
            var response = await _client
                .From<ReviewRecord>()
                .Order(r => r.CreatedAt, Supabase.Postgrest.Constants.Ordering.Descending)
                .Limit(limit)
                .Get();

            return response.Models;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get reviews from Supabase");
            return [];
        }
    }

    /// <summary>
    /// 获取诊断状态。
    /// </summary>
    public object GetDiagnostics() => new
    {
        enabled = IsEnabled,
        initialized = _initialized,
        tableExists = _tableExists,
        bucketExists = _bucketExists,
        url = _config.Url,
        table = _config.ReviewsTable,
        bucket = _config.StorageBucket
    };
}
