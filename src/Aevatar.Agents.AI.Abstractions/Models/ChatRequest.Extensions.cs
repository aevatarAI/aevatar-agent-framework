using System;

namespace Aevatar.Agents.AI;

/// <summary>
/// Partial class extensions for ChatRequest protobuf message
/// </summary>
public partial class ChatRequest
{
    public const string SessionIdKey = "session_id";
    public const string SessionIdKeyCamel = "sessionId";

    /// <summary>
    /// Creates a new ChatRequest with a generated ID
    /// </summary>
    /// <param name="message">The message content</param>
    /// <returns>A new ChatRequest instance</returns>
    public static ChatRequest Create(string message)
    {
        return new ChatRequest
        {
            Message = message,
            RequestId = Guid.NewGuid().ToString()
        };
    }

    /// <summary>
    /// Adds or updates a context value
    /// </summary>
    /// <param name="key">Context key</param>
    /// <param name="value">Context value</param>
    public void AddContext(string key, string value)
    {
        Context[key] = value;
    }

    /// <summary>
    /// Sets session id for cross-agent session persistence.
    /// </summary>
    public void SetSessionId(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return;

        Context[SessionIdKey] = sessionId.Trim();
    }

    /// <summary>
    /// Gets session id from context (if any).
    /// </summary>
    public string? GetSessionId()
    {
        if (Context.TryGetValue(SessionIdKey, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        if (Context.TryGetValue(SessionIdKeyCamel, out var camel) && !string.IsNullOrWhiteSpace(camel))
            return camel;

        return null;
    }

    /// <summary>
    /// Sets temperature if not already set
    /// </summary>
    /// <param name="temperature">Temperature value (0-1)</param>
    public void SetTemperatureIfNotSet(double temperature)
    {
        if (Temperature == 0)
        {
            Temperature = temperature;
        }
    }

    /// <summary>
    /// Sets max tokens if not already set
    /// </summary>
    /// <param name="maxTokens">Maximum tokens</param>
    public void SetMaxTokensIfNotSet(int maxTokens)
    {
        if (MaxTokens == 0)
        {
            MaxTokens = maxTokens;
        }
    }

    /// <summary>
    /// Adds an image key for multimodal input.
    /// Image keys reference blob storage entries that will be resolved at runtime.
    /// </summary>
    /// <param name="imageKey">Blob storage key for the image</param>
    public void AddImageKey(string imageKey)
    {
        if (!string.IsNullOrWhiteSpace(imageKey))
        {
            ImageKeys.Add(imageKey.Trim());
        }
    }

    /// <summary>
    /// Adds multiple image keys for multimodal input.
    /// </summary>
    /// <param name="imageKeys">Collection of blob storage keys</param>
    public void AddImageKeys(IEnumerable<string> imageKeys)
    {
        foreach (var key in imageKeys)
        {
            AddImageKey(key);
        }
    }

    /// <summary>
    /// Returns true if this request contains image keys for multimodal processing.
    /// </summary>
    public bool HasImages => ImageKeys.Count > 0;

    /// <summary>
    /// Creates a new ChatRequest with a message and image keys.
    /// </summary>
    /// <param name="message">The text message</param>
    /// <param name="imageKeys">Blob storage keys for images</param>
    /// <returns>A new ChatRequest instance with images</returns>
    public static ChatRequest CreateWithImages(string message, IEnumerable<string> imageKeys)
    {
        var request = Create(message);
        request.AddImageKeys(imageKeys);
        return request;
    }

    /// <summary>
    /// Creates a new ChatRequest with a message and a single image key.
    /// </summary>
    /// <param name="message">The text message</param>
    /// <param name="imageKey">Blob storage key for the image</param>
    /// <returns>A new ChatRequest instance with image</returns>
    public static ChatRequest CreateWithImage(string message, string imageKey)
    {
        var request = Create(message);
        request.AddImageKey(imageKey);
        return request;
    }
}
