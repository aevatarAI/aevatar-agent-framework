using System.Globalization;

namespace Aevatar.Agents.Core.Context;

/// <summary>
/// AgentContext value converter.
///
/// - Fixes type degradation issues after cross-boundary (serialization/deserialization)
///   For example: int -> int64, float -> double, etc., causing Get&lt;T&gt; to fail reading values.
/// </summary>
internal static class AgentContextValueConverter
{
    public static bool TryConvert<T>(object value, out T result)
    {
        // Fast path: exact type match
        if (value is T typed)
        {
            result = typed;
            return true;
        }

        // ============================================================
        //  Numeric conversions
        // ============================================================
        var target = typeof(T);

        if (value is long l)
        {
            if (target == typeof(int) && l is >= int.MinValue and <= int.MaxValue)
            {
                result = (T)(object)(int)l;
                return true;
            }

            if (target == typeof(short) && l is >= short.MinValue and <= short.MaxValue)
            {
                result = (T)(object)(short)l;
                return true;
            }

            if (target == typeof(byte) && l is >= byte.MinValue and <= byte.MaxValue)
            {
                result = (T)(object)(byte)l;
                return true;
            }

            if (target == typeof(uint) && l is >= uint.MinValue and <= uint.MaxValue)
            {
                result = (T)(object)(uint)l;
                return true;
            }
        }

        if (value is double d)
        {
            if (target == typeof(float) && d is >= float.MinValue and <= float.MaxValue)
            {
                result = (T)(object)(float)d;
                return true;
            }

            if (target == typeof(decimal))
            {
                result = (T)(object)(decimal)d;
                return true;
            }
        }

        if (value is float f)
        {
            if (target == typeof(double))
            {
                result = (T)(object)(double)f;
                return true;
            }

            if (target == typeof(decimal))
            {
                result = (T)(object)(decimal)f;
                return true;
            }
        }

        // ============================================================
        //  Guid/Date/Decimal parsing from string
        // ============================================================
        if (value is string s)
        {
            if (target == typeof(Guid) && Guid.TryParse(s, out var g))
            {
                result = (T)(object)g;
                return true;
            }

            if (target == typeof(DateTime) && DateTime.TryParse(
                    s,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var dt))
            {
                result = (T)(object)dt;
                return true;
            }

            if (target == typeof(DateTimeOffset) && DateTimeOffset.TryParse(
                    s,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var dto))
            {
                result = (T)(object)dto;
                return true;
            }

            if (target == typeof(decimal) && decimal.TryParse(
                    s,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var dec))
            {
                result = (T)(object)dec;
                return true;
            }
        }

        // ============================================================
        //  DateTime <-> DateTimeOffset interop
        // ============================================================
        if (value is DateTime dateTime && target == typeof(DateTimeOffset))
        {
            // NOTE: DateTimeKind.Unspecified will be treated as local; but this is best-effort interop.
            result = (T)(object)new DateTimeOffset(dateTime);
            return true;
        }

        if (value is DateTimeOffset dateTimeOffset && target == typeof(DateTime))
        {
            result = (T)(object)dateTimeOffset.UtcDateTime;
            return true;
        }

        result = default!;
        return false;
    }
}

