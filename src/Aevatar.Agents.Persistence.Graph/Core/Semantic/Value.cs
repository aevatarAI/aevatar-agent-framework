namespace Aevatar.Agents.Persistence.Graph.Core.Semantic;

/// <summary>
/// 图属性值抽象基类；使用具体派生类型表达不同标量或嵌套 Map。
/// </summary>
public abstract record Value;

/// <summary>字符串属性。</summary>
/// <param name="Data">字符串值。</param>
public sealed record StringValue(string Data) : Value;
/// <summary>整数属性（long）。</summary>
/// <param name="Data">整数值。</param>
public sealed record IntValue(long Data) : Value;
/// <summary>布尔属性。</summary>
/// <param name="Data">布尔值。</param>
public sealed record BoolValue(bool Data) : Value;
/// <summary>浮点属性（double）。</summary>
/// <param name="Data">浮点值。</param>
public sealed record FloatValue(double Data) : Value;

/// <summary>
/// Map 属性，允许嵌套多个键/值（值类型仍为 <see cref="Value"/>）。
/// </summary>
/// <param name="Fields">键值对集合，值仍为 Value 派生类型。</param>
public sealed record MapValue(
    IReadOnlyDictionary<string, Value> Fields) : Value;
