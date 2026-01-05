using Aevatar.Agents.Abstractions.Helpers;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Abstractions;

/// <summary>
/// EventEnvelope extension methods
/// </summary>
public static class EventEnvelopeExtensions
{
    /// <summary>
    /// Get publish timestamp (UTC)
    /// </summary>
    public static Timestamp GetPublishedTimestamp(this EventEnvelope envelope)
    {
        return envelope.Timestamp;
    }

    /// <summary>
    /// Set publish timestamp to current time (UTC)
    /// </summary>
    public static void SetPublishedTimestampToNow(this EventEnvelope envelope)
    {
        envelope.Timestamp = TimestampHelper.GetUtcNow();
    }

    /// <summary>
    /// Get event age (duration from publish to now)
    /// </summary>
    public static Duration GetEventAge(this EventEnvelope envelope)
    {
        var publishedTime = envelope.GetPublishedTimestamp();
        return TimestampHelper.GetUtcNow() - publishedTime;
    }

    /// <summary>
    /// Check if event is expired
    /// </summary>
    /// <param name="envelope">Event envelope</param>
    /// <param name="maxAge">Maximum age</param>
    /// <returns>Whether expired</returns>
    public static bool IsExpired(this EventEnvelope envelope, Duration maxAge)
    {
        return envelope.GetEventAge().Seconds > maxAge.Seconds;
    }
}