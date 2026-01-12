using Aevatar.Agents.AI.Tool.Messages;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.AI.Tool.Messages;

/// <summary>
/// Partial class extensions for AevatarAIToolResult protobuf message
/// </summary>
public partial class AevatarAIToolResult
{
    /// <summary>
    /// Creates a successful result
    /// </summary>
    /// <param name="data">Result data (will be serialized to Any)</param>
    /// <returns>A success result</returns>
    public static AevatarAIToolResult CreateSuccess(object? data = null)
    {
        var result = new AevatarAIToolResult
        {
            Success = true
        };

        if (data != null)
        {
            // For simple types, wrap in a StringValue or other well-known type
            if (data is string strData)
            {
                result.Data = Any.Pack(new StringValue { Value = strData });
            }
            else if (data is int intData)
            {
                result.Data = Any.Pack(new Int32Value { Value = intData });
            }
            else if (data is double doubleData)
            {
                result.Data = Any.Pack(new DoubleValue { Value = doubleData });
            }
            else if (data is bool boolData)
            {
                result.Data = Any.Pack(new BoolValue { Value = boolData });
            }
            // For complex types, they should already be protobuf messages
            else if (data is Google.Protobuf.IMessage message)
            {
                result.Data = Any.Pack(message);
            }
            else
            {
                // Fallback: convert to string representation
                result.Data = Any.Pack(new StringValue { Value = data.ToString() ?? string.Empty });
            }
        }

        return result;
    }

    /// <summary>
    /// Creates a failure result
    /// </summary>
    /// <param name="errorMessage">Error message</param>
    /// <returns>A failure result</returns>
    public static AevatarAIToolResult CreateFailure(string errorMessage)
    {
        return new AevatarAIToolResult
        {
            Success = false,
            ErrorMessage = errorMessage
        };
    }

    /// <summary>
    /// Adds metadata to the result
    /// </summary>
    /// <param name="key">Metadata key</param>
    /// <param name="value">Metadata value</param>
    public void AddMetadata(string key, string value)
    {
        Metadata[key] = value;
    }

    /// <summary>
    /// Gets the data as a specific type
    /// </summary>
    /// <typeparam name="T">Type to unpack to</typeparam>
    /// <returns>The unpacked data or default</returns>
    public T? GetDataAs<T>() where T : class, Google.Protobuf.IMessage, new()
    {
        if (Data == null) return default;
        
        try
        {
            // Try to unpack directly without checking type first
            return Data.Unpack<T>();
        }
        catch
        {
            // Ignore unpacking errors
        }

        return default;
    }

    /// <summary>
    /// Gets the data as a string
    /// </summary>
    /// <returns>String representation of the data</returns>
    public string? GetDataAsString()
    {
        if (Data == null) return null;

        try
        {
            if (Data.Is(StringValue.Descriptor))
            {
                return Data.Unpack<StringValue>()?.Value;
            }
        }
        catch
        {
            // Ignore unpacking errors
        }

        return Data.ToString();
    }
}
