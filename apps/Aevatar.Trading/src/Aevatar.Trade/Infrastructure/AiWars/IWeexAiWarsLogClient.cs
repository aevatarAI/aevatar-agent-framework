namespace Aevatar.Trade.Infrastructure.AiWars;

public interface IWeexAiWarsLogClient
{
    Task<AiWarsUploadResult> UploadAiLogAsync(AiWarsUploadRequest request, CancellationToken ct = default);
}

public sealed record AiWarsUploadRequest
{
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required byte[] Content { get; init; }
    public string? CycleId { get; init; }
}

public sealed record AiWarsUploadResult
{
    public required bool Success { get; init; }
    public string? RemoteId { get; init; }
    public string? RawResponse { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}


