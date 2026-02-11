using Aevatar.Agents.AI.Abstractions;
using Microsoft.Extensions.Logging;

// ReSharper disable InconsistentNaming
namespace Aevatar.Agents.AI.Core;

/// <summary>
/// Vision / multimodal support for AI agents.
///
/// Architecture:
/// 1. ChatRequest.ImageKeys contains blob storage references
/// 2. ResolveImageKeysAsync converts keys to AevatarImageData (raw bytes)
/// 3. AevatarLLMRequest.Images carries resolved data to providers
/// 4. Providers (MEAI/LLMTornado) build multimodal messages
///
/// Subclasses override ResolveImageKeysAsync to plug in actual blob storage.
/// </summary>
public abstract partial class AIGAgentBase
{
    /// <summary>
    /// Resolve image keys to actual image data for multimodal LLM requests.
    /// Override this method in subclasses to provide actual blob storage integration.
    /// </summary>
    /// <param name="imageKeys">List of image keys from blob storage</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of resolved image data, or null if not supported</returns>
    protected virtual Task<IList<AevatarImageData>?> ResolveImageKeysAsync(
        IEnumerable<string> imageKeys,
        CancellationToken cancellationToken = default)
    {
        // Default: no image resolution.
        // Subclasses should override to provide actual blob storage integration.
        Logger.LogWarning("Image resolution not implemented. Override ResolveImageKeysAsync in subclass.");
        return Task.FromResult<IList<AevatarImageData>?>(null);
    }

    /// <summary>
    /// Resolve image keys from a ChatRequest and attach to the AevatarLLMRequest.
    /// Called after BuildLLMRequest in both ChatAsync and ChatStreamAsync.
    /// </summary>
    protected async Task ResolveAndAttachImagesAsync(
        ChatRequest request,
        AevatarLLMRequest llmRequest,
        CancellationToken cancellationToken)
    {
        if (request.ImageKeys.Count == 0)
            return;

        Logger.LogDebug("Resolving {Count} image keys for multimodal request", request.ImageKeys.Count);

        try
        {
            llmRequest.Images = await ResolveImageKeysAsync(request.ImageKeys, cancellationToken);

            if (llmRequest.Images?.Count > 0)
            {
                Logger.LogInformation("Resolved {Count} images for multimodal request", llmRequest.Images.Count);
            }
            else
            {
                Logger.LogWarning("Image resolution returned no images for {Count} keys", request.ImageKeys.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogError(ex, "Failed to resolve image keys for multimodal request");
            // Best-effort: continue without images rather than failing the entire request
        }
    }
}
