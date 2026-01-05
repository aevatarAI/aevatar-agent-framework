using Aevatar.Agents.Abstractions.Rpc;
using Google.Protobuf.WellKnownTypes;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Abstractions.Tests;

public class ProtobufPackerTests
{
    [Fact]
    public void Pack_Unpack_Primitives_ShouldRoundtrip()
    {
        ProtobufPacker.Unpack<int>(ProtobufPacker.Pack(123)).ShouldBe(123);
        ProtobufPacker.Unpack<long>(ProtobufPacker.Pack(123L)).ShouldBe(123L);
        ProtobufPacker.Unpack<string>(ProtobufPacker.Pack("hi")).ShouldBe("hi");
        ProtobufPacker.Unpack<bool>(ProtobufPacker.Pack(true)).ShouldBeTrue();
        ProtobufPacker.Unpack<double>(ProtobufPacker.Pack(1.25d)).ShouldBe(1.25d);
        ProtobufPacker.Unpack<float>(ProtobufPacker.Pack(1.5f)).ShouldBe(1.5f);
    }

    [Fact]
    public void Pack_Null_ShouldUnpackToNull_ForNullableTargets()
    {
        var any = ProtobufPacker.Pack(null);
        ProtobufPacker.Unpack<string?>(any).ShouldBeNull();
        ProtobufPacker.Unpack<int?>(any).ShouldBeNull();
    }

    [Fact]
    public void Pack_Unpack_ProtobufMessage_ShouldRoundtrip()
    {
        var msg = new StringValue { Value = "x" };
        var any = ProtobufPacker.Pack(msg);
        var back = ProtobufPacker.Unpack<StringValue>(any);
        back.Value.ShouldBe("x");
    }

    [Fact]
    public void Pack_UnsupportedComplexType_ShouldThrow()
    {
        var ex = Should.Throw<NotSupportedException>(() => ProtobufPacker.Pack(new { a = 1 }));
        ex.Message.ShouldContain("not supported");
    }
}


