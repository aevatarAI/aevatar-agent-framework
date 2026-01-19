namespace Aevatar.Trade.Infrastructure.WeexApi;

// ============================================================================
//  WEEX API Exception
// ============================================================================

public class WeexApiException : Exception
{
    public string? ErrorCode { get; }

    public WeexApiException(
        string message,
        string? errorCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}


