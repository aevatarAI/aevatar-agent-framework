using System.Text.Json;
using System.Linq;
using Aevatar.Agents.AI;
using Aevatar.Agents.Knowledge.Graph;
using Aevatar.Agents.Knowledge.Graph.Models;
using Aevatar.Agents.Cognitive.Researching.Runtime;

namespace Aevatar.Agents.Cognitive.Researching.Uploads;

/// <summary>
/// Extracts knowledge points from uploaded files using LLM and creates KnowledgeNodes.
/// </summary>
public sealed class UploadExtractionService
{
    private readonly IResearchingRuntime _runtime;
    private readonly IKnowledgeGraphClientFactory _graphFactory;
    private readonly FileTextParser _textParser;
    private readonly UploadsStore _uploadsStore;
    private readonly ILogger<UploadExtractionService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public UploadExtractionService(
        IResearchingRuntime runtime,
        IKnowledgeGraphClientFactory graphFactory,
        FileTextParser textParser,
        UploadsStore uploadsStore,
        ILogger<UploadExtractionService> logger)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _graphFactory = graphFactory ?? throw new ArgumentNullException(nameof(graphFactory));
        _textParser = textParser ?? throw new ArgumentNullException(nameof(textParser));
        _uploadsStore = uploadsStore ?? throw new ArgumentNullException(nameof(uploadsStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Processes an uploaded file: saves it, extracts text, uses LLM to identify knowledge points,
    /// and creates KnowledgeNodes in the graph.
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <param name="openRead">Factory to open a fresh stream for reading the file</param>
    /// <param name="fileName">Original file name (used for extension detection)</param>
    /// <param name="length">File length in bytes</param>
    /// <param name="providerName">Optional LLM provider name</param>
    /// <param name="maxKnowledgePoints">Max knowledge points to extract (0 = unlimited, extract all)</param>
    /// <param name="ct">Cancellation token</param>
    public async Task<ExtractionResult> ExtractAndCreateNodesAsync(
        string sessionId,
        Func<Stream> openRead,
        string fileName,
        long length,
        string? providerName = null,
        int maxKnowledgePoints = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(openRead);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var safeFileName = fileName ?? "unknown";
        _logger.LogInformation(
            "Starting extraction for file {FileName} in session {SessionId}",
            safeFileName, sessionId);

        try
        {
            // Step 1: Save file to workspace
            await using (var saveStream = openRead())
            {
                var savedPath = await _uploadsStore.SaveAsync(
                    sessionId,
                    saveStream,
                    safeFileName,
                    length,
                    ct);
                _logger.LogDebug("File saved to {Path}", savedPath);

                // Step 2: Extract text content (100K chars to support longer documents)
                await using var textStream = openRead();
                var textResult = await _textParser.ExtractTextAsync(textStream, safeFileName, maxChars: 100_000, ct);
                if (!textResult.Success)
                {
                    return new ExtractionResult
                    {
                        Success = false,
                        ErrorMessage = $"Failed to extract text: {textResult.ErrorMessage}",
                        FilePath = savedPath,
                        FileName = safeFileName
                    };
                }

                // Step 3: Use LLM to extract knowledge points
                var knowledgePoints = await ExtractKnowledgePointsWithLlmAsync(
                    sessionId,
                    textResult.Content!,
                    safeFileName,
                    providerName,
                    maxKnowledgePoints,
                    ct);

                if (knowledgePoints.Count == 0)
                {
                    _logger.LogWarning("No knowledge points extracted from {FileName}", safeFileName);
                    return new ExtractionResult
                    {
                        Success = true,
                        FilePath = savedPath,
                        FileName = safeFileName,
                        ExtractedNodes = [],
                        Message = "File processed but no distinct knowledge points were identified."
                    };
                }

                // Step 4: Create KnowledgeNodes
                var createdNodes = await CreateKnowledgeNodesAsync(
                    sessionId,
                    knowledgePoints,
                    safeFileName,
                    savedPath,
                    ct);

                _logger.LogInformation(
                    "Successfully extracted {Count} knowledge points from {FileName}",
                    createdNodes.Count, safeFileName);

                return new ExtractionResult
                {
                    Success = true,
                    FilePath = savedPath,
                    FileName = safeFileName,
                    ExtractedNodes = createdNodes,
                    Message = $"Extracted {createdNodes.Count} knowledge points from {safeFileName}"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract knowledge from {FileName}", safeFileName);
            return new ExtractionResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                FileName = safeFileName
            };
        }
    }

    private async Task<List<ExtractedKnowledgePoint>> ExtractKnowledgePointsWithLlmAsync(
        string sessionId,
        string textContent,
        string fileName,
        string? providerName,
        int maxPoints,
        CancellationToken ct)
    {
        var prompt = BuildExtractionPrompt(textContent, fileName, maxPoints);

        try
        {
            // Use research_assistant agent for extraction
            var (agent, agentId) = await _runtime.GetResearchAssistantAgentAsync(sessionId, providerName, ct);

            var request = new ChatRequest
            {
                Message = prompt,
                RequestId = Guid.NewGuid().ToString("N"),
                StageHint = "upload:extract_knowledge"
            };
            request.Context["agent_id"] = agentId;

            var response = await agent.ChatAsync(request, ct);
            var responseText = response?.Content ?? "";

            // Parse JSON from response
            return ParseKnowledgePoints(responseText, maxPoints);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM extraction failed for {FileName}", fileName);
            return [];
        }
    }

    private static string BuildExtractionPrompt(string textContent, string fileName, int maxPoints)
    {
        // Truncate content if too long for prompt (allow up to 80K for comprehensive extraction)
        var truncatedContent = textContent.Length > 80_000
            ? textContent[..80_000] + "\n\n[Content truncated...]"
            : textContent;

        // JSON example as separate string to avoid raw string literal issues with braces
        const string jsonExample = """
            [
              {
                "title": "Main Finding Title",
                "content": "Detailed description of the finding with key facts...",
                "keywords": ["keyword1", "keyword2", "keyword3"]
              }
            ]
            """;

        // Build extraction instruction based on whether there's a limit
        var extractionInstruction = maxPoints > 0
            ? $"extract up to {maxPoints} knowledge points"
            : "extract all knowledge points";

        return $"""
            You are analyzing the following document to extract key knowledge points.

            Document: {fileName}
            Content:
            {truncatedContent}

            Task:
            - {extractionInstruction} from this document
            - Each knowledge point should be a distinct, actionable insight or fact
            - Provide a clear title and detailed description
            - Include relevant keywords for searchability

            Return your response as a JSON array in this exact format:
            {jsonExample}
            """;
    }

    private List<ExtractedKnowledgePoint> ParseKnowledgePoints(string responseText, int maxPoints)
    {
        try
        {
            // Extract JSON array from response
            var jsonStart = responseText.IndexOf('[');
            var jsonEnd = responseText.LastIndexOf(']');

            if (jsonStart < 0 || jsonEnd <= jsonStart)
                return [];

            var json = responseText[jsonStart..(jsonEnd + 1)];
            var points = JsonSerializer.Deserialize<List<ExtractedKnowledgePoint>>(json, JsonOptions);

            if (points == null)
                return [];

            // Filter and limit
            var validPoints = points
                .Where(p => !string.IsNullOrWhiteSpace(p.Title) && !string.IsNullOrWhiteSpace(p.Content))
                .ToList();

            if (maxPoints > 0 && validPoints.Count > maxPoints)
                validPoints = validPoints.Take(maxPoints).ToList();

            return validPoints;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse knowledge points JSON");
            return [];
        }
    }

    private async Task<List<CreatedKnowledgeNode>> CreateKnowledgeNodesAsync(
        string sessionId,
        List<ExtractedKnowledgePoint> points,
        string fileName,
        string savedPath,
        CancellationToken ct)
    {
        var createdNodes = new List<CreatedKnowledgeNode>();

        // Get graph client for the session
        var client = _graphFactory.CreateClient(sessionId);
        var now = DateTimeOffset.UtcNow;

        foreach (var point in points)
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                // Create unique node ID
                var nodeId = $"upload_{Guid.NewGuid():N}";

                // Build node content with metadata
                var keywordsStr = point.Keywords != null && point.Keywords.Count > 0
                    ? string.Join(", ", point.Keywords)
                    : "None";

                var detailedDesc = $"""
                    {point.Content}

                    ---
                    Source: User Upload
                    File: {fileName}
                    Keywords: {keywordsStr}
                    Uploaded: {now:yyyy-MM-dd HH:mm:ss} UTC
                    Session: {sessionId}
                    """;

                await client.UpsertNodeAsync(
                    nodeId: nodeId,
                    nodeType: KnowledgeNodeType.Reference,
                    coreDescription: point.Title ?? "Extracted Knowledge",
                    detailedDescription: detailedDesc.Trim(),
                    cancellationToken: ct);

                createdNodes.Add(new CreatedKnowledgeNode
                {
                    Id = nodeId,
                    Title = point.Title ?? "",
                    Content = point.Content ?? "",
                    Keywords = point.Keywords ?? []
                });

                _logger.LogDebug("Created knowledge node {NodeId}: {Title}", nodeId, point.Title);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create knowledge node for point: {Title}", point.Title);
            }
        }

        return createdNodes;
    }
}

/// <summary>
/// Result of the extraction process.
/// </summary>
public sealed class ExtractionResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? FilePath { get; init; }
    public string FileName { get; init; } = "";
    public string? Message { get; init; }
    public IReadOnlyList<CreatedKnowledgeNode> ExtractedNodes { get; init; } = [];
}

/// <summary>
/// Knowledge point extracted by LLM.
/// </summary>
public sealed class ExtractedKnowledgePoint
{
    public string? Title { get; set; }
    public string? Content { get; set; }
    public List<string>? Keywords { get; set; }
}

/// <summary>
/// Information about a created knowledge node.
/// </summary>
public sealed class CreatedKnowledgeNode
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Content { get; init; } = "";
    public IReadOnlyList<string> Keywords { get; init; } = [];
}

