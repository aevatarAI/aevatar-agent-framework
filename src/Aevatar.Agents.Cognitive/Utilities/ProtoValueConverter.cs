using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Cognitive.Utilities;

// ============================================================
//  Protobuf Value Converter
//  Responsibility: Convert between C# objects and Protobuf Value
// ============================================================

/// <summary>
/// Protobuf Value conversion utility.
/// </summary>
public static class ProtoValueConverter
{
    /// <summary>
    /// Convert Protobuf Value to C# object.
    /// </summary>
    public static object FromProto(Value value) => value.KindCase switch
    {
        Value.KindOneofCase.NullValue => null!,
        Value.KindOneofCase.NumberValue => value.NumberValue,
        Value.KindOneofCase.StringValue => value.StringValue,
        Value.KindOneofCase.BoolValue => value.BoolValue,
        Value.KindOneofCase.StructValue => FromProtoStruct(value.StructValue),
        Value.KindOneofCase.ListValue => value.ListValue.Values.Select(FromProto).ToList(),
        _ => value.ToString()
    };
    
    /// <summary>
    /// Convert Protobuf Struct to Dictionary.
    /// </summary>
    public static Dictionary<string, object> FromProtoStruct(Struct protoStruct)
    {
        var result = new Dictionary<string, object>();
        foreach (var (key, value) in protoStruct.Fields)
        {
            result[key] = FromProto(value);
        }
        return result;
    }
    
    /// <summary>
    /// Convert C# object to Protobuf Value.
    /// </summary>
    public static Value ToProto(object? value) => value switch
    {
        null => Value.ForNull(),
        bool b => Value.ForBool(b),
        int i => Value.ForNumber(i),
        long l => Value.ForNumber(l),
        float f => Value.ForNumber(f),
        double d => Value.ForNumber(d),
        string s => Value.ForString(s),
        IEnumerable<object> list => Value.ForList(list.Select(ToProto).ToArray()),
        IDictionary<string, object> dict => ToProtoStruct(dict),
        _ => Value.ForString(value.ToString() ?? "")
    };
    
    /// <summary>
    /// Convert Dictionary to Protobuf Value (Struct).
    /// </summary>
    public static Value ToProtoStruct(IDictionary<string, object> dict)
    {
        var structValue = new Struct();
        foreach (var (key, value) in dict)
        {
            structValue.Fields[key] = ToProto(value);
        }
        return Value.ForStruct(structValue);
    }
}
