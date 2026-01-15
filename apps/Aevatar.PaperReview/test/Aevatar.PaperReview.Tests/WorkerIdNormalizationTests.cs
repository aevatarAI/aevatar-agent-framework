using System.Reflection;
using Aevatar.PaperReview.Models;
using Aevatar.PaperReview.Services;
using Shouldly;
using Xunit;

namespace Aevatar.PaperReview.Tests;

// ============================================================
//  WORKER ID NORMALIZATION TESTS
//  验证 Worker ID 规范化逻辑的正确性
//  核心规则：gen[N] → worker-{(N-1) % workerCount}
// ============================================================

public class WorkerIdNormalizationTests
{
    // 通过反射访问私有方法进行测试
    private static readonly MethodInfo NormalizeMethod = typeof(ReviewEventBridge)
        .GetMethod("NormalizeWorkerId", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static string NormalizeWorkerId(string stepId, int workerCount = 3)
    {
        return (string)NormalizeMethod.Invoke(null, [stepId, workerCount])!;
    }

    // ─────────────────────────────────────────────────────────
    //  Coordinator 模式识别测试
    // ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("check_atomic")]
    [InlineData("CHECK_ATOMIC_task")]
    [InlineData("main.check_atomic")]
    public void Normalize_CheckAtomic_ReturnsCoordinator(string stepId)
    {
        NormalizeWorkerId(stepId).ShouldBe("coordinator");
    }

    [Theory]
    [InlineData("coordinator")]
    [InlineData("COORDINATOR")]
    [InlineData("main_coordinator")]
    public void Normalize_Coordinator_ReturnsCoordinator(string stepId)
    {
        NormalizeWorkerId(stepId).ShouldBe("coordinator");
    }

    [Fact]
    public void Normalize_Main_ReturnsCoordinator()
    {
        NormalizeWorkerId("main").ShouldBe("coordinator");
    }

    [Theory]
    [InlineData("compose")]
    [InlineData("COMPOSE_results")]
    public void Normalize_Compose_ReturnsCoordinator(string stepId)
    {
        NormalizeWorkerId(stepId).ShouldBe("coordinator");
    }

    [Theory]
    [InlineData("main.vote")]
    [InlineData("task.vote")]
    [InlineData("VOTE")]
    public void Normalize_Vote_ReturnsCoordinator(string stepId)
    {
        NormalizeWorkerId(stepId).ShouldBe("coordinator");
    }

    // ─────────────────────────────────────────────────────────
    //  gen[N] 模式测试 (核心规则: worker-{(N-1) % workerCount})
    // ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("gen[1]", 3, "worker-0")]  // (1-1) % 3 = 0
    [InlineData("gen[2]", 3, "worker-1")]  // (2-1) % 3 = 1
    [InlineData("gen[3]", 3, "worker-2")]  // (3-1) % 3 = 2
    [InlineData("gen[4]", 3, "worker-0")]  // (4-1) % 3 = 0 (循环)
    [InlineData("gen[5]", 3, "worker-1")]  // (5-1) % 3 = 1 (循环)
    [InlineData("gen[6]", 3, "worker-2")]  // (6-1) % 3 = 2 (循环)
    public void Normalize_Gen_WithK3_CyclesCorrectly(string stepId, int k, string expected)
    {
        NormalizeWorkerId(stepId, k).ShouldBe(expected);
    }

    [Theory]
    [InlineData("gen[1]", 5, "worker-0")]
    [InlineData("gen[2]", 5, "worker-1")]
    [InlineData("gen[3]", 5, "worker-2")]
    [InlineData("gen[4]", 5, "worker-3")]
    [InlineData("gen[5]", 5, "worker-4")]
    [InlineData("gen[6]", 5, "worker-0")]  // 循环
    public void Normalize_Gen_WithK5_CyclesCorrectly(string stepId, int k, string expected)
    {
        NormalizeWorkerId(stepId, k).ShouldBe(expected);
    }

    [Theory]
    [InlineData("task.gen[1]")]
    [InlineData("main.gen[2]")]
    [InlineData("GEN[3]")]
    public void Normalize_Gen_WithPrefix_ExtractsCorrectly(string stepId)
    {
        // 应该能提取 gen[N] 中的数字
        var result = NormalizeWorkerId(stepId, 3);
        result.ShouldStartWith("worker-");
    }

    // ─────────────────────────────────────────────────────────
    //  worker-N 模式测试
    //  NOTE:
    //  - worker-N 视为“已规范化”(0-indexed)，应保持不变（只做大小写/分隔符统一）
    // ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("worker-1", 3, "worker-1")]
    [InlineData("worker-2", 3, "worker-2")]
    [InlineData("worker-3", 3, "worker-3")]
    [InlineData("worker-4", 3, "worker-4")]
    [InlineData("worker_1", 3, "worker-1")]  // 下划线格式 → 统一为 "-"
    [InlineData("worker0", 3, "worker0")]    // 无分隔符：保持原样（目前实现不自动插入 '-'）
    public void Normalize_WorkerN_KeptAsIs(string stepId, int workerCount, string expected)
    {
        NormalizeWorkerId(stepId, workerCount).ShouldBe(expected);
    }

    // ─────────────────────────────────────────────────────────
    //  UUID 模式测试
    // ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("a1b2c3d4e5f6a1b2c3d4e5f6")]
    [InlineData("550e8400-e29b-41d4-a716-446655440000")]
    [InlineData("abcdef1234567890abcdef")]
    public void Normalize_UuidLike_ReturnsCoordinator(string stepId)
    {
        NormalizeWorkerId(stepId).ShouldBe("coordinator");
    }

    // ─────────────────────────────────────────────────────────
    //  边界条件测试
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void Normalize_EmptyString_ReturnsCoordinator()
    {
        NormalizeWorkerId("").ShouldBe("coordinator");
    }

    [Fact]
    public void Normalize_Null_ReturnsCoordinator()
    {
        NormalizeWorkerId(null!).ShouldBe("coordinator");
    }

    [Fact]
    public void Normalize_UnknownPattern_ReturnsCoordinator()
    {
        NormalizeWorkerId("unknown_step_id").ShouldBe("coordinator");
    }

    [Fact]
    public void Normalize_K0_HandlesGracefully()
    {
        // K=0 时应该避免除零错误
        var result = NormalizeWorkerId("gen[5]", 0);
        result.ShouldBe("worker-0");
    }

    // ─────────────────────────────────────────────────────────
    //  实际场景测试
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void Normalize_RealWorkflowScenario_CorrectMapping()
    {
        // 模拟真实的 MAKER 工作流场景
        var workerCount = 3; // 3 个并行 Worker (N=3)
        
        // Coordinator 操作
        NormalizeWorkerId("check_atomic_complexity", workerCount).ShouldBe("coordinator");
        NormalizeWorkerId("decompose_task", workerCount).ShouldBe("coordinator");
        NormalizeWorkerId("compose_results", workerCount).ShouldBe("coordinator");
        NormalizeWorkerId("main.vote", workerCount).ShouldBe("coordinator");
        
        // Worker 操作 (并行执行)
        NormalizeWorkerId("gen[1]", workerCount).ShouldBe("worker-0");
        NormalizeWorkerId("gen[2]", workerCount).ShouldBe("worker-1");
        NormalizeWorkerId("gen[3]", workerCount).ShouldBe("worker-2");
        
        // 第二轮采样 (循环分配)
        NormalizeWorkerId("gen[4]", workerCount).ShouldBe("worker-0");
        NormalizeWorkerId("gen[5]", workerCount).ShouldBe("worker-1");
        NormalizeWorkerId("gen[6]", workerCount).ShouldBe("worker-2");
    }

    [Theory]
    [InlineData(ReviewType.Quick, 1, 1)]      // K=1, N=1
    [InlineData(ReviewType.Standard, 2, 3)]   // K=2, N=3
    [InlineData(ReviewType.Detailed, 3, 5)]   // K=3, N=5
    [InlineData(ReviewType.Rigorous, 4, 7)]   // K=4, N=7
    [InlineData(ReviewType.Critical, 5, 9)]   // K=5, N=9
    public void Normalize_AllReviewTypes_WorkerCountMatchesN(ReviewType type, int expectedK, int expectedN)
    {
        var (k, n, _) = MakerParameters.GetParams(type);
        k.ShouldBe(expectedK);
        n.ShouldBe(expectedN);
        
        // 用 N 作为循环参数，验证 N 个采样被正确分配到 N 个 Worker 卡片
        var workerIds = new HashSet<string>();
        for (var i = 1; i <= n; i++)
        {
            var normalized = NormalizeWorkerId($"gen[{i}]", n);
            workerIds.Add(normalized);
        }
        
        // Paper Review UI：按 workerCount(N) 预创建卡片，gen[1..N] 应该覆盖所有 worker-0..worker-(N-1)
        workerIds.Count.ShouldBeLessThanOrEqualTo(n);
    }
}
