using Aevatar.PaperReview.Models;
using Aevatar.PaperReview.Services;
using Shouldly;
using Xunit;

namespace Aevatar.PaperReview.Tests;

// ============================================================
//  PHASE MAPPER TESTS
//  验证 Cognitive DSL 阶段到 ReviewPhase 的映射正确性
// ============================================================

public class PhaseMapperTests
{
    // ─────────────────────────────────────────────────────────
    //  StepId 关键字映射测试
    // ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("check_atomic_task", ReviewPhase.Assessing)]
    [InlineData("main.check_atomic", ReviewPhase.Assessing)]
    [InlineData("CHECK_ATOMIC.vote", ReviewPhase.Assessing)]
    public void Map_StepId_CheckAtomic_ReturnsAssessing(string stepId, ReviewPhase expected)
    {
        PhaseMapper.Map("", "", stepId).ShouldBe(expected);
    }

    [Theory]
    [InlineData("decompose_task", ReviewPhase.Decomposing)]
    [InlineData("main.decompose", ReviewPhase.Decomposing)]
    [InlineData("DECOMPOSE.parallel", ReviewPhase.Decomposing)]
    public void Map_StepId_Decompose_ReturnsDecomposing(string stepId, ReviewPhase expected)
    {
        PhaseMapper.Map("", "", stepId).ShouldBe(expected);
    }

    [Theory]
    [InlineData("compose_results", ReviewPhase.Composing)]
    [InlineData("main.compose", ReviewPhase.Composing)]
    [InlineData("COMPOSE.final", ReviewPhase.Composing)]
    public void Map_StepId_Compose_ReturnsComposing(string stepId, ReviewPhase expected)
    {
        PhaseMapper.Map("", "", stepId).ShouldBe(expected);
    }

    [Theory]
    [InlineData("solve_subtask", ReviewPhase.Solving)]
    [InlineData("main.solve", ReviewPhase.Solving)]
    [InlineData("SOLVE.worker", ReviewPhase.Solving)]
    public void Map_StepId_Solve_ReturnsSolving(string stepId, ReviewPhase expected)
    {
        PhaseMapper.Map("", "", stepId).ShouldBe(expected);
    }

    [Theory]
    [InlineData("execute_task", ReviewPhase.Executing)]
    [InlineData("main.execute", ReviewPhase.Executing)]
    [InlineData("EXECUTE.parallel", ReviewPhase.Executing)]
    public void Map_StepId_Execute_ReturnsExecuting(string stepId, ReviewPhase expected)
    {
        PhaseMapper.Map("", "", stepId).ShouldBe(expected);
    }

    // ─────────────────────────────────────────────────────────
    //  Phase 前缀映射测试
    // ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("ANALYZE task complexity", ReviewPhase.Assessing)]
    [InlineData("ASSESS task atomicity", ReviewPhase.Assessing)]
    public void Map_Phase_Analyze_ReturnsAssessing(string phase, ReviewPhase expected)
    {
        PhaseMapper.Map(phase, "", "unknown").ShouldBe(expected);
    }

    [Theory]
    [InlineData("START processing", ReviewPhase.Starting)]
    [InlineData("LOAD configuration", ReviewPhase.Starting)]
    [InlineData("INIT workflow", ReviewPhase.Starting)]
    public void Map_Phase_Start_ReturnsStarting(string phase, ReviewPhase expected)
    {
        PhaseMapper.Map(phase, "", "unknown").ShouldBe(expected);
    }

    [Theory]
    [InlineData("VOTE round 1", ReviewPhase.Voting)]
    [InlineData("VOTING in progress", ReviewPhase.Voting)]
    public void Map_Phase_Vote_ReturnsVoting(string phase, ReviewPhase expected)
    {
        PhaseMapper.Map(phase, "", "unknown").ShouldBe(expected);
    }

    [Theory]
    [InlineData("COMPLETE", ReviewPhase.Completed)]
    [InlineData("RESULT ready", ReviewPhase.Completed)]
    public void Map_Phase_Complete_ReturnsCompleted(string phase, ReviewPhase expected)
    {
        PhaseMapper.Map(phase, "", "unknown").ShouldBe(expected);
    }

    [Theory]
    [InlineData("FAIL: timeout", ReviewPhase.Failed)]
    [InlineData("ERROR occurred", ReviewPhase.Failed)]
    public void Map_Phase_Fail_ReturnsFailed(string phase, ReviewPhase expected)
    {
        PhaseMapper.Map(phase, "", "unknown").ShouldBe(expected);
    }

    // ─────────────────────────────────────────────────────────
    //  StepType 映射测试
    // ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("vote", ReviewPhase.Voting)]
    [InlineData("Vote", ReviewPhase.Voting)]
    [InlineData("VOTE", ReviewPhase.Voting)]
    public void Map_StepType_Vote_ReturnsVoting(string stepType, ReviewPhase expected)
    {
        PhaseMapper.Map("", stepType, "unknown").ShouldBe(expected);
    }

    [Theory]
    [InlineData("fan_out", ReviewPhase.Executing)]
    [InlineData("parallel", ReviewPhase.Executing)]
    public void Map_StepType_Parallel_ReturnsExecuting(string stepType, ReviewPhase expected)
    {
        PhaseMapper.Map("", stepType, "unknown").ShouldBe(expected);
    }

    [Theory]
    [InlineData("llm_call", ReviewPhase.Solving)]
    [InlineData("LLM_CALL", ReviewPhase.Solving)]
    public void Map_StepType_LlmCall_ReturnsSolving(string stepType, ReviewPhase expected)
    {
        PhaseMapper.Map("", stepType, "unknown").ShouldBe(expected);
    }

    // ─────────────────────────────────────────────────────────
    //  优先级测试：StepId > Phase > StepType
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void Map_StepIdPriority_TakesPrecedenceOverPhaseAndStepType()
    {
        // StepId 包含 "decompose" 应该返回 Decomposing
        // 即使 Phase 是 "COMPLETE"，StepType 是 "vote"
        var result = PhaseMapper.Map("COMPLETE", "vote", "main.decompose");
        result.ShouldBe(ReviewPhase.Decomposing);
    }

    [Fact]
    public void Map_PhasePriority_TakesPrecedenceOverStepType()
    {
        // Phase 包含 "VOTE" 应该返回 Voting
        // 即使 StepType 是 "llm_call"
        var result = PhaseMapper.Map("VOTING round", "llm_call", "unknown");
        result.ShouldBe(ReviewPhase.Voting);
    }

    // ─────────────────────────────────────────────────────────
    //  默认值测试
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void Map_UnknownInputs_ReturnsVoting()
    {
        var result = PhaseMapper.Map("unknown_phase", "unknown_type", "unknown_step");
        result.ShouldBe(ReviewPhase.Voting);
    }

    [Fact]
    public void Map_EmptyInputs_ReturnsVoting()
    {
        var result = PhaseMapper.Map("", "", "");
        result.ShouldBe(ReviewPhase.Voting);
    }
}
