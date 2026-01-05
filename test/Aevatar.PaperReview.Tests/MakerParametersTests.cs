using Aevatar.PaperReview.Models;
using Shouldly;
using Xunit;

namespace Aevatar.PaperReview.Tests;

// ============================================================
//  MAKER PARAMETERS TESTS
//  验证 MAKER 共识参数计算的正确性
//  基于论文公式: K = 领先票数, N = 2K - 1 = 采样数
// ============================================================

public class MakerParametersTests
{
    // ─────────────────────────────────────────────────────────
    //  K 和 N 值计算测试
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void GetParams_Quick_ReturnsK1N1()
    {
        var (k, n, desc) = MakerParameters.GetParams(ReviewType.Quick);
        
        k.ShouldBe(1);
        n.ShouldBe(1);
        desc.ShouldContain("Single shot");
    }

    [Fact]
    public void GetParams_Standard_ReturnsK2N3()
    {
        var (k, n, desc) = MakerParameters.GetParams(ReviewType.Standard);
        
        k.ShouldBe(2);
        n.ShouldBe(3);
        desc.ShouldContain("3 workers");
        desc.ShouldContain("2-vote lead");
    }

    [Fact]
    public void GetParams_Detailed_ReturnsK3N5()
    {
        var (k, n, desc) = MakerParameters.GetParams(ReviewType.Detailed);
        
        k.ShouldBe(3);
        n.ShouldBe(5);
        desc.ShouldContain("5 workers");
        desc.ShouldContain("3-vote lead");
    }

    [Fact]
    public void GetParams_Rigorous_ReturnsK4N7()
    {
        var (k, n, desc) = MakerParameters.GetParams(ReviewType.Rigorous);
        
        k.ShouldBe(4);
        n.ShouldBe(7);
        desc.ShouldContain("7 workers");
        desc.ShouldContain("4-vote lead");
    }

    [Fact]
    public void GetParams_Critical_ReturnsK5N9()
    {
        var (k, n, desc) = MakerParameters.GetParams(ReviewType.Critical);
        
        k.ShouldBe(5);
        n.ShouldBe(9);
        desc.ShouldContain("9 workers");
        desc.ShouldContain("5-vote lead");
    }

    // ─────────────────────────────────────────────────────────
    //  数学公式验证: N = 2K - 1
    // ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ReviewType.Quick)]
    [InlineData(ReviewType.Standard)]
    [InlineData(ReviewType.Detailed)]
    [InlineData(ReviewType.Rigorous)]
    [InlineData(ReviewType.Critical)]
    public void GetParams_AllTypes_SatisfyFormulaRelationship(ReviewType type)
    {
        var (k, n, _) = MakerParameters.GetParams(type);
        
        // 验证 N = 2K - 1 (MAKER 论文公式)
        n.ShouldBe(2 * k - 1);
    }

    // ─────────────────────────────────────────────────────────
    //  边界条件测试
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void GetParams_AllTypes_HavePositiveValues()
    {
        foreach (ReviewType type in Enum.GetValues<ReviewType>())
        {
            var (k, n, desc) = MakerParameters.GetParams(type);
            
            k.ShouldBeGreaterThan(0);
            n.ShouldBeGreaterThan(0);
            n.ShouldBeGreaterThanOrEqualTo(k);
            desc.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void GetParams_Types_AreOrderedByComplexity()
    {
        var quick = MakerParameters.GetParams(ReviewType.Quick);
        var standard = MakerParameters.GetParams(ReviewType.Standard);
        var detailed = MakerParameters.GetParams(ReviewType.Detailed);
        var rigorous = MakerParameters.GetParams(ReviewType.Rigorous);
        var critical = MakerParameters.GetParams(ReviewType.Critical);
        
        // K 值应该递增
        quick.K.ShouldBeLessThan(standard.K);
        standard.K.ShouldBeLessThan(detailed.K);
        detailed.K.ShouldBeLessThan(rigorous.K);
        rigorous.K.ShouldBeLessThan(critical.K);
        
        // N 值应该递增
        quick.N.ShouldBeLessThan(standard.N);
        standard.N.ShouldBeLessThan(detailed.N);
        detailed.N.ShouldBeLessThan(rigorous.N);
        rigorous.N.ShouldBeLessThan(critical.N);
    }

    // ─────────────────────────────────────────────────────────
    //  默认值测试
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void GetParams_UnknownEnumValue_ReturnsStandardParams()
    {
        // 使用强制转换创建未定义的枚举值
        var unknownType = (ReviewType)999;
        var (k, n, desc) = MakerParameters.GetParams(unknownType);
        
        // 应该返回默认值（Standard）
        k.ShouldBe(2);
        n.ShouldBe(3);
        desc.ShouldContain("Default");
    }
}

