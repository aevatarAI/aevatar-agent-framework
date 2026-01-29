using System.Text.Json;
using Aevatar.Agents.AI;
using Aevatar.Agents.Knowledge.Graph;
using Aevatar.Agents.Knowledge.Graph.Models;
using VibeResearching.Api.Sessions;

namespace VibeResearching.Api.Vibe.Uploads;

/// <summary>
/// Extracts knowledge points from uploaded files using LLM and creates KnowledgeNodes.
/// </summary>
public sealed class UploadExtractionService
{
    private readonly ResearchRuntime _runtime;
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
        ResearchRuntime runtime,
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
    /// <param name="file">The uploaded file</param>
    /// <param name="providerName">Optional LLM provider name</param>
    /// <param name="maxKnowledgePoints">Max knowledge points to extract (0 = unlimited, extract all)</param>
    /// <param name="ct">Cancellation token</param>
    public async Task<ExtractionResult> ExtractAndCreateNodesAsync(
        string sessionId,
        IFormFile file,
        string? providerName = null,
        int maxKnowledgePoints = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var fileName = file.FileName ?? "unknown";
        _logger.LogInformation(
            "Starting extraction for file {FileName} in session {SessionId}",
            fileName, sessionId);

        try
        {
            // Step 1: Save file to workspace
            var savedPath = await _uploadsStore.SaveAsync(sessionId, file, ct);
            _logger.LogDebug("File saved to {Path}", savedPath);

            // Step 2: Extract text content (100K chars to support longer documents)
            var textResult = await _textParser.ExtractTextAsync(file, maxChars: 100_000, ct);
            if (!textResult.Success)
            {
                return new ExtractionResult
                {
                    Success = false,
                    ErrorMessage = $"Failed to extract text: {textResult.ErrorMessage}",
                    FilePath = savedPath,
                    FileName = fileName
                };
            }

            // Step 2.5: Validate extracted content quality
            var content = textResult.Content?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning("Extracted content is empty for {FileName}", fileName);
                return new ExtractionResult
                {
                    Success = false,
                    ErrorMessage = "File content could not be extracted or is empty",
                    FilePath = savedPath,
                    FileName = fileName
                };
            }

            // Check if content is an error message (from PDF extraction failure)
            if (content.StartsWith("[PDF content could not be extracted", StringComparison.OrdinalIgnoreCase) ||
                content.StartsWith("[Error", StringComparison.OrdinalIgnoreCase) ||
                content.Length < 50)  // Too short to be meaningful
            {
                _logger.LogWarning("Extracted content is invalid or too short for {FileName}: {ContentPreview}", 
                    fileName, content.Length > 100 ? content[..100] : content);
                return new ExtractionResult
                {
                    Success = false,
                    ErrorMessage = "File content could not be extracted or is invalid. The file may be corrupted, image-based, or encrypted.",
                    FilePath = savedPath,
                    FileName = fileName
                };
            }

            // Step 3: Use LLM to extract knowledge points
            _logger.LogInformation("Extracting knowledge points from {FileName}, content length: {Length} characters", 
                fileName, content.Length);
            
            var knowledgePoints = await ExtractKnowledgePointsWithLlmAsync(
                sessionId,
                content,
                fileName,
                providerName,
                maxKnowledgePoints,
                ct);

            if (knowledgePoints.Count == 0)
            {
                _logger.LogWarning(
                    "No knowledge points extracted from {FileName}. Possible reasons: 1) LLM failed to extract, 2) JSON parsing failed, 3) All points filtered as low-quality, 4) Document content is not suitable for knowledge extraction. Content preview: {ContentPreview}",
                    fileName, content.Length > 200 ? content[..200] : content);
                return new ExtractionResult
                {
                    Success = true,
                    FilePath = savedPath,
                    FileName = fileName,
                    ExtractedNodes = [],
                    Message = "File processed but no distinct knowledge points were identified. This may happen if: 1) The document content is not suitable for knowledge extraction, 2) The LLM failed to parse the content, 3) All extracted points were filtered as low-quality. Please check the document content and try again."
                };
            }

            // Step 4: Create KnowledgeNodes
            var createdNodes = await CreateKnowledgeNodesAsync(
                sessionId,
                knowledgePoints,
                fileName,
                savedPath,
                ct);

            _logger.LogInformation(
                "Successfully extracted {Count} knowledge points from {FileName}",
                createdNodes.Count, fileName);

            return new ExtractionResult
            {
                Success = true,
                FilePath = savedPath,
                FileName = fileName,
                ExtractedNodes = createdNodes,
                Message = $"Extracted {createdNodes.Count} knowledge points from {fileName}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract knowledge from {FileName}", fileName);
            return new ExtractionResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                FileName = fileName
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

            if (string.IsNullOrWhiteSpace(responseText))
            {
                _logger.LogWarning("LLM returned empty response for {FileName}", fileName);
                return [];
            }

            _logger.LogDebug("LLM response length: {Length} characters for {FileName}", responseText.Length, fileName);
            if (responseText.Length > 500)
            {
                _logger.LogDebug("LLM response preview: {Preview}", responseText[..500]);
            }

            // Parse JSON from response
            return ParseKnowledgePoints(responseText, maxPoints);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM extraction failed for {FileName}: {Error}", fileName, ex.Message);
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
            : "extract ALL distinct knowledge points you can identify (no limit)";

        return $"""
            You are a knowledge extraction assistant. Analyze the following document thoroughly and {extractionInstruction} that would be valuable for research.

            IMPORTANT: Extract as many distinct, meaningful knowledge points as possible. Each point should represent a unique concept, fact, finding, or insight from the document. Do not artificially limit the number - if the document contains 20 valuable points, extract all 20.

            For each knowledge point, provide:
            - title: A concise, descriptive title (max 100 characters)
            - content: A detailed description with key facts, context, and implications (max 800 characters)
            - keywords: 3-5 relevant keywords as an array

            Categories to look for:
            - Key findings and conclusions
            - Important definitions and concepts
            - Methodologies and approaches
            - Data points and statistics
            - Relationships and connections
            - Implications and applications
            - Historical context
            - Technical details
            - Examples and case studies

            Respond ONLY with a valid JSON array. No other text or explanation.

            Example format:
            {jsonExample}

            Document filename: {fileName}

            Document content:
            ---
            {truncatedContent}
            ---

            Extract all knowledge points as JSON:
            """;
    }

    private List<ExtractedKnowledgePoint> ParseKnowledgePoints(string responseText, int maxPoints)
    {
        try
        {
            // Find JSON array in response
            var jsonStart = responseText.IndexOf('[');
            if (jsonStart < 0)
            {
                _logger.LogWarning("No JSON array start found in LLM response. Response preview: {Preview}", 
                    responseText.Length > 500 ? responseText[..500] : responseText);
                return [];
            }

            var jsonEnd = responseText.LastIndexOf(']');
            string jsonText;

            if (jsonEnd > jsonStart)
            {
                // Complete JSON array found
                jsonText = responseText[jsonStart..(jsonEnd + 1)];
            }
            else
            {
                // JSON was truncated (hit token limit) - try to recover complete objects
                _logger.LogWarning("JSON array appears truncated, attempting to recover complete objects");
                jsonText = TryRecoverTruncatedJson(responseText[jsonStart..]);
            }

            var points = JsonSerializer.Deserialize<List<ExtractedKnowledgePoint>>(jsonText, JsonOptions);

            if (points == null || points.Count == 0)
            {
                _logger.LogWarning("No knowledge points parsed from JSON");
                return [];
            }

            _logger.LogInformation("Successfully parsed {Count} knowledge points", points.Count);

            // Filter out low-quality knowledge points
            var validPoints = points.Where(p =>
                !string.IsNullOrWhiteSpace(p.Title) &&
                !string.IsNullOrWhiteSpace(p.Content) &&
                p.Content.Length >= 20 &&  // Minimum content length
                !p.Title.Contains("Error", StringComparison.OrdinalIgnoreCase) &&
                !p.Title.Contains("Failed", StringComparison.OrdinalIgnoreCase) &&
                !p.Content.Contains("[PDF content could not be extracted", StringComparison.OrdinalIgnoreCase) &&
                !p.Content.Contains("[Error", StringComparison.OrdinalIgnoreCase)
            ).ToList();

            if (validPoints.Count == 0)
            {
                _logger.LogWarning(
                    "All {TotalCount} extracted knowledge points were filtered out as low-quality. Original points: {OriginalPoints}",
                    points.Count,
                    points.Count > 0 
                        ? string.Join("; ", points.Take(5).Select(p => $"Title: {p.Title}, ContentLength: {p.Content?.Length ?? 0}"))
                        : "none");
                return [];
            }

            if (validPoints.Count < points.Count)
            {
                _logger.LogInformation("Filtered {FilteredCount} low-quality knowledge points, keeping {ValidCount} valid points",
                    points.Count - validPoints.Count, validPoints.Count);
            }

            // If maxPoints <= 0, return all valid points (unlimited)
            // Otherwise, limit to maxPoints
            return maxPoints > 0 ? validPoints.Take(maxPoints).ToList() : validPoints;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse knowledge points JSON, attempting recovery");

            // Try to recover partial JSON
            var recovered = TryRecoverPartialJson(responseText);
            if (recovered.Count > 0)
            {
                _logger.LogInformation("Recovered {Count} knowledge points from truncated JSON", recovered.Count);
                return maxPoints > 0 ? recovered.Take(maxPoints).ToList() : recovered;
            }

            return [];
        }
    }

    /// <summary>
    /// Attempts to recover a truncated JSON array by finding the last complete object.
    /// </summary>
    private static string TryRecoverTruncatedJson(string partialJson)
    {
        // Find the last complete object by looking for "}," or "}\n" patterns
        // and then close the array properly
        var lastCompleteObject = -1;

        // Look for the pattern: "}" followed by anything that's not part of the object
        for (var i = partialJson.Length - 1; i >= 0; i--)
        {
            if (partialJson[i] == '}')
            {
                // Check if this looks like a complete object end
                // by seeing if what follows could be array continuation or end
                var afterBrace = partialJson[(i + 1)..].TrimStart();
                if (afterBrace.Length == 0 || afterBrace.StartsWith(',') || afterBrace.StartsWith(']'))
                {
                    lastCompleteObject = i;
                    break;
                }
                // Also check if we're at the end of the string (truncated right after })
                if (i == partialJson.Length - 1 || partialJson[(i + 1)..].Trim().Length == 0)
                {
                    lastCompleteObject = i;
                    break;
                }
            }
        }

        if (lastCompleteObject > 0)
        {
            // Take up to the last complete object and close the array
            var recovered = partialJson[..(lastCompleteObject + 1)].TrimEnd();
            if (recovered.EndsWith(','))
                recovered = recovered[..^1];
            return recovered + "]";
        }

        // Couldn't recover, return empty array
        return "[]";
    }

    /// <summary>
    /// Attempts to parse individual JSON objects from a potentially malformed response.
    /// </summary>
    private List<ExtractedKnowledgePoint> TryRecoverPartialJson(string responseText)
    {
        var points = new List<ExtractedKnowledgePoint>();

        // Find all complete JSON objects using regex-like pattern matching
        var jsonStart = responseText.IndexOf('[');
        if (jsonStart < 0) return points;

        var content = responseText[jsonStart..];
        var objectStart = -1;
        var braceCount = 0;
        var inString = false;
        var escape = false;

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];

            if (escape)
            {
                escape = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                escape = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString) continue;

            if (c == '{')
            {
                if (braceCount == 0)
                    objectStart = i;
                braceCount++;
            }
            else if (c == '}')
            {
                braceCount--;
                if (braceCount == 0 && objectStart >= 0)
                {
                    // Found a complete object
                    var objectJson = content[objectStart..(i + 1)];
                    try
                    {
                        var point = JsonSerializer.Deserialize<ExtractedKnowledgePoint>(objectJson, JsonOptions);
                        if (point != null && !string.IsNullOrWhiteSpace(point.Title))
                        {
                            points.Add(point);
                        }
                    }
                    catch
                    {
                        // Skip malformed objects
                    }
                    objectStart = -1;
                }
            }
        }

        return points;
    }

    private async Task<List<CreatedKnowledgeNode>> CreateKnowledgeNodesAsync(
        string sessionId,
        List<ExtractedKnowledgePoint> knowledgePoints,
        string fileName,
        string filePath,
        CancellationToken ct)
    {
        // Write to current session's DAG so knowledge is available for this research
        var client = _graphFactory.CreateClient(sessionId);
        var createdNodes = new List<CreatedKnowledgeNode>();
        var now = DateTimeOffset.UtcNow;

        foreach (var point in knowledgePoints)
        {
            // Skip invalid knowledge points (defensive check)
            if (string.IsNullOrWhiteSpace(point.Title) ||
                string.IsNullOrWhiteSpace(point.Content) ||
                point.Content.Length < 20)
            {
                _logger.LogDebug("Skipping invalid knowledge point: Title={Title}, ContentLength={Length}",
                    point.Title, point.Content?.Length ?? 0);
                continue;
            }

            // Skip if content appears to be an error message (defensive check)
            if (point.Content.Contains("[PDF content could not be extracted", StringComparison.OrdinalIgnoreCase) ||
                point.Content.Contains("[Error", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Skipping knowledge point with error message content: {Title}", point.Title);
                continue;
            }

            var nodeId = $"upload_{now:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..32];

            try
            {
                // Build detailed description with metadata (since tags are not supported)
                var keywordsStr = point.Keywords is { Count: > 0 }
                    ? string.Join(", ", point.Keywords)
                    : "";

                var detailedDesc = $"""
                    {point.Content ?? ""}

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
