using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aevatar.CognitiveMesh.Dsl.Models;

internal sealed class StrategyKindConverter : JsonConverter<StrategyKind>
{
    private static readonly Dictionary<string, StrategyKind> StringToKind = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cot"] = StrategyKind.Cot,
        ["tot"] = StrategyKind.Tot,
        ["got"] = StrategyKind.Got,
        ["uotcomb"] = StrategyKind.UotComb,
        ["uotexpl"] = StrategyKind.UotExpl,
        ["uottrans"] = StrategyKind.UotTrans
    };

    public override StrategyKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("strategy 必须是字符串。");
        }

        var raw = reader.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new JsonException("strategy 不可为空。");
        }

        var normalized = Normalize(raw);
        if (StringToKind.TryGetValue(normalized, out var kind))
        {
            return kind;
        }

        throw new JsonException($"未知的 strategy '{raw}'。");
    }

    public override void Write(Utf8JsonWriter writer, StrategyKind value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            StrategyKind.Cot => "cot",
            StrategyKind.Tot => "tot",
            StrategyKind.Got => "got",
            StrategyKind.UotComb => "uot_comb",
            StrategyKind.UotExpl => "uot_expl",
            StrategyKind.UotTrans => "uot_trans",
            _ => value.ToString()
        });
    }

    private static string Normalize(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var index = 0;
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer[index++] = char.ToLowerInvariant(ch);
            }
        }

        return new string(buffer[..index]);
    }
}

