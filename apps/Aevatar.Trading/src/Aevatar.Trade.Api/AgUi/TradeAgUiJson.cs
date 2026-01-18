using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aevatar.Trade.Api.AgUi;

internal static class TradeAgUiJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

