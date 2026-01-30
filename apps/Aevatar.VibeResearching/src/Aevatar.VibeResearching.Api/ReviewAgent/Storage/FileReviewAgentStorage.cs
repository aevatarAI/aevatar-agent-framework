using System.Text.Json;
using Microsoft.Extensions.Logging;
using VibeResearching.Vibe.ReviewAgent;

namespace Aevatar.VibeResearching.Api.ReviewAgent.Storage;

/// <summary>
/// File-based storage implementation for Review Agent.
/// Stores state, config, and iterations as JSON files in workspace/review-agent/.
/// </summary>
public sealed class FileReviewAgentStorage : IReviewAgentStorage
{
    private readonly string _basePath;
    private readonly ILogger<FileReviewAgentStorage> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string StatePath => Path.Combine(_basePath, "state.json");
    private string ConfigPath => Path.Combine(_basePath, "config.json");
    private string IterationsPath => Path.Combine(_basePath, "iterations");

    public FileReviewAgentStorage(string basePath, ILogger<FileReviewAgentStorage> logger)
    {
        _basePath = basePath ?? throw new ArgumentNullException(nameof(basePath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // Ensure directories exist
        Directory.CreateDirectory(_basePath);
        Directory.CreateDirectory(IterationsPath);
    }

    public async Task SaveStateAsync(ReviewAgentState state, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var json = JsonSerializer.Serialize(state, _jsonOptions);
            await File.WriteAllTextAsync(StatePath, json, cancellationToken);
            _logger.LogDebug("Saved Review Agent state to {Path}", StatePath);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ReviewAgentState?> LoadStateAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(StatePath))
        {
            _logger.LogDebug("No state file found at {Path}", StatePath);
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(StatePath, cancellationToken);
            return JsonSerializer.Deserialize<ReviewAgentState>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load state from {Path}", StatePath);
            return null;
        }
    }

    public async Task SaveIterationAsync(ReviewIteration iteration, CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(IterationsPath, $"{iteration.IterationId}.json");
        var json = JsonSerializer.Serialize(iteration, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
        _logger.LogDebug("Saved iteration {IterationId} to {Path}", iteration.IterationId, filePath);
    }

    public async Task<IterationListResponse> LoadIterationsAsync(int limit = 50, int offset = 0, CancellationToken cancellationToken = default)
    {
        var files = Directory.GetFiles(IterationsPath, "*.json")
            .OrderByDescending(f => Path.GetFileNameWithoutExtension(f))
            .ToList();

        var total = files.Count;
        var summaries = new List<ReviewIterationSummary>();

        foreach (var file in files.Skip(offset).Take(limit))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, cancellationToken);
                var iteration = JsonSerializer.Deserialize<ReviewIteration>(json, _jsonOptions);
                if (iteration != null)
                {
                    summaries.Add(iteration.ToSummary());
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load iteration from {Path}", file);
            }
        }

        return new IterationListResponse
        {
            Iterations = summaries,
            Total = total,
            Limit = limit,
            Offset = offset
        };
    }

    public async Task<ReviewIteration?> LoadIterationAsync(string iterationId, CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(IterationsPath, $"{iterationId}.json");
        if (!File.Exists(filePath))
        {
            _logger.LogDebug("Iteration file not found: {Path}", filePath);
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            return JsonSerializer.Deserialize<ReviewIteration>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load iteration from {Path}", filePath);
            return null;
        }
    }

    public async Task SaveConfigAsync(ReviewAgentOptions options, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var json = JsonSerializer.Serialize(options, _jsonOptions);
            await File.WriteAllTextAsync(ConfigPath, json, cancellationToken);
            _logger.LogDebug("Saved Review Agent config to {Path}", ConfigPath);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ReviewAgentOptions?> LoadConfigAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ConfigPath))
        {
            _logger.LogDebug("No config file found at {Path}", ConfigPath);
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(ConfigPath, cancellationToken);
            return JsonSerializer.Deserialize<ReviewAgentOptions>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load config from {Path}", ConfigPath);
            return null;
        }
    }

    public Task<string> GenerateIterationIdAsync(CancellationToken cancellationToken = default)
    {
        var dateStr = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
        var existingFiles = Directory.GetFiles(IterationsPath, $"{dateStr}_*.json");
        var nextSeq = existingFiles.Length + 1;
        var iterationId = $"{dateStr}_{nextSeq:D3}";
        return Task.FromResult(iterationId);
    }

    public Task<int> GetIterationCountAsync(CancellationToken cancellationToken = default)
    {
        var count = Directory.Exists(IterationsPath)
            ? Directory.GetFiles(IterationsPath, "*.json").Length
            : 0;
        return Task.FromResult(count);
    }
}
