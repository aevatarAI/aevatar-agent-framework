using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using System.Reflection;

namespace Aevatar.Agents.Abstractions.Rpc;

/// <summary>
/// Shared Protobuf packing/unpacking utilities for RPC.
/// Supports: primitives, Guid, DateTime, TimeSpan, Enum, IMessage
/// </summary>
public static class ProtobufPacker
{
    private static readonly MethodInfo? UnpackMethod = typeof(Any)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .FirstOrDefault(m => m.Name == "Unpack" && m.IsGenericMethod && m.GetParameters().Length == 0);

    /// <summary>
    /// Pack a value into Protobuf Any
    /// </summary>
    public static Any Pack(object? value)
    {
        if (value == null || value.GetType().FullName == "System.Threading.Tasks.VoidTaskResult")
            return Any.Pack(new Empty());

        return value switch
        {
            // Signed integers
            int i => Any.Pack(new Int32Value { Value = i }),
            long l => Any.Pack(new Int64Value { Value = l }),
            short s => Any.Pack(new Int32Value { Value = s }),
            sbyte sb => Any.Pack(new Int32Value { Value = sb }),
            
            // Unsigned integers
            uint ui => Any.Pack(new UInt32Value { Value = ui }),
            ulong ul => Any.Pack(new UInt64Value { Value = ul }),
            ushort us => Any.Pack(new UInt32Value { Value = us }),
            byte b => Any.Pack(new UInt32Value { Value = b }),
            
            // Floating point
            double d => Any.Pack(new DoubleValue { Value = d }),
            float f => Any.Pack(new FloatValue { Value = f }),
            decimal m => Any.Pack(new DoubleValue { Value = (double)m }),
            
            // String and bool
            string str => Any.Pack(new StringValue { Value = str }),
            bool bl => Any.Pack(new BoolValue { Value = bl }),
            
            // Binary
            byte[] bytes => Any.Pack(new BytesValue { Value = ByteString.CopyFrom(bytes) }),
            
            // Common types (serialized as string)
            Guid g => Any.Pack(new StringValue { Value = g.ToString() }),
            DateTime dt => Any.Pack(Timestamp.FromDateTime(dt.ToUniversalTime())),
            DateTimeOffset dto => Any.Pack(Timestamp.FromDateTimeOffset(dto)),
            TimeSpan ts => Any.Pack(Duration.FromTimeSpan(ts)),
            
            // Enum (as int)
            System.Enum e => Any.Pack(new Int32Value { Value = Convert.ToInt32(e) }),
            
            // Protobuf message
            IMessage msg => Any.Pack(msg),
            
            _ => throw new NotSupportedException(
                $"Type '{value.GetType().Name}' is not supported. " +
                $"Use Protobuf IMessage for complex types.")
        };
    }

    /// <summary>
    /// Unpack a Protobuf Any to target type
    /// </summary>
    public static object? Unpack(Any any, System.Type targetType)
    {
        // Handle null Any (should be treated as null/Empty)
        if (any == null || string.IsNullOrEmpty(any.TypeUrl))
            return null;

        var underlyingType = Nullable.GetUnderlyingType(targetType);
        var actualType = underlyingType ?? targetType;

        // Handle Empty (null)
        if (any.TypeUrl.EndsWith("/google.protobuf.Empty"))
            return null;

        // Handle Protobuf message types
        if (typeof(IMessage).IsAssignableFrom(actualType) && UnpackMethod != null)
        {
            var genericUnpack = UnpackMethod.MakeGenericMethod(actualType);
            return genericUnpack.Invoke(any, null);
        }

        // Handle Enum
        if (actualType.IsEnum)
        {
            var intValue = any.Unpack<Int32Value>().Value;
            return System.Enum.ToObject(actualType, intValue);
        }

        // Handle primitive and common types
        return actualType switch
        {
            // Signed integers
            var t when t == typeof(int) => any.Unpack<Int32Value>().Value,
            var t when t == typeof(long) => any.Unpack<Int64Value>().Value,
            var t when t == typeof(short) => (short)any.Unpack<Int32Value>().Value,
            var t when t == typeof(sbyte) => (sbyte)any.Unpack<Int32Value>().Value,
            
            // Unsigned integers
            var t when t == typeof(uint) => any.Unpack<UInt32Value>().Value,
            var t when t == typeof(ulong) => any.Unpack<UInt64Value>().Value,
            var t when t == typeof(ushort) => (ushort)any.Unpack<UInt32Value>().Value,
            var t when t == typeof(byte) => (byte)any.Unpack<UInt32Value>().Value,
            
            // Floating point
            var t when t == typeof(double) => any.Unpack<DoubleValue>().Value,
            var t when t == typeof(float) => any.Unpack<FloatValue>().Value,
            var t when t == typeof(decimal) => (decimal)any.Unpack<DoubleValue>().Value,
            
            // String and bool
            var t when t == typeof(string) => any.Unpack<StringValue>().Value,
            var t when t == typeof(bool) => any.Unpack<BoolValue>().Value,
            
            // Binary
            var t when t == typeof(byte[]) => any.Unpack<BytesValue>().Value.ToByteArray(),
            
            // Common types
            var t when t == typeof(Guid) => Guid.Parse(any.Unpack<StringValue>().Value),
            var t when t == typeof(DateTime) => any.Unpack<Timestamp>().ToDateTime(),
            var t when t == typeof(DateTimeOffset) => any.Unpack<Timestamp>().ToDateTimeOffset(),
            var t when t == typeof(TimeSpan) => any.Unpack<Duration>().ToTimeSpan(),
            
            _ => throw new NotSupportedException(
                $"Type '{targetType.Name}' is not supported. " +
                $"Use Protobuf IMessage for complex types.")
        };
    }

    /// <summary>
    /// Unpack a Protobuf Any to generic type T
    /// </summary>
    public static T Unpack<T>(Any any) => (T)Unpack(any, typeof(T))!;
}

