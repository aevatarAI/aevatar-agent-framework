using Aevatar.Agents.Cognitive.Execution;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Cognitive.Template;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Cognitive.Tests;

public class ExecutorTests
{
    [Fact]
    public void TransformExecutor_ShouldAggregateCountsAndDistinctBPool()
    {
        // --------------------------------------------------------
        //  Arrange: verdict list (Dictionary-based, like JSON parsed output)
        // --------------------------------------------------------
        var verdicts = new List<object>
        {
            new Dictionary<string, object>
            {
                ["accept"] = true,
                ["strong_refutation"] = false,
                ["proposed_b"] = new List<object>
                {
                    new Dictionary<string, object> { ["statement"] = "B1", ["motivation"] = "m1" },
                    new Dictionary<string, object> { ["statement"] = "B2", ["motivation"] = "m2" }
                }
            },
            new Dictionary<string, object>
            {
                ["accept"] = false,
                ["strong_refutation"] = true,
                ["proposed_b"] = new List<object>
                {
                    new Dictionary<string, object> { ["statement"] = "B2", ["motivation"] = "dup" },
                    new Dictionary<string, object> { ["statement"] = "B3", ["motivation"] = "m3" }
                }
            },
            // include_failures style item (should be ignored by count/select_many)
            new Dictionary<string, object>
            {
                ["success"] = false,
                ["error"] = "llm-timeout>180s",
                ["worker_id"] = "worker-x"
            }
        };

        var variables = new Dictionary<string, object>
        {
            ["worker_verdicts"] = verdicts
        };

        var ops = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["op"] = "count_where",
                ["from"] = "worker_verdicts",
                ["where"] = new Dictionary<string, object?> { ["field"] = "accept", ["equals"] = true },
                ["store"] = "accept_count"
            },
            new()
            {
                ["op"] = "count_where",
                ["from"] = "worker_verdicts",
                ["where"] = new Dictionary<string, object?> { ["field"] = "strong_refutation", ["equals"] = true },
                ["store"] = "strong_refutation_count"
            },
            new()
            {
                ["op"] = "select_many",
                ["from"] = "worker_verdicts",
                ["field"] = "proposed_b",
                ["store"] = "b_raw"
            },
            new()
            {
                ["op"] = "distinct",
                ["from"] = "b_raw",
                ["key"] = "statement",
                ["store"] = "b_pool"
            }
        };

        var step = new StepDefinition
        {
            Id = "aggregate",
            Type = "transform",
            Parameters = new Dictionary<string, object?> { ["ops"] = ops }
        };

        var executor = new TransformExecutor(new TemplateEngine(), NullLogger.Instance);

        // --------------------------------------------------------
        //  Act
        // --------------------------------------------------------
        var result = executor.Execute(step, variables);

        // --------------------------------------------------------
        //  Assert
        // --------------------------------------------------------
        result.Success.ShouldBeTrue(result.Error);

        variables["accept_count"].ShouldBe(1);
        variables["strong_refutation_count"].ShouldBe(1);

        var bPool = (List<object>)variables["b_pool"];
        bPool.Count.ShouldBe(3);
        bPool.Select(x => ((Dictionary<string, object>)x)["statement"].ToString()).ShouldBe(new[] { "B1", "B2", "B3" });
    }

    [Fact]
    public void RetrieveFactsExecutor_ShouldReturnTopK_ByLexicalSimilarity()
    {
        var template = new TemplateEngine();
        var executor = new RetrieveFactsExecutor(template, NullLogger.Instance);

        var theorems = new List<object>
        {
            new Dictionary<string, object> { ["id"] = "T1", ["statement"] = "Optimize city traffic flow with adaptive signals" },
            new Dictionary<string, object> { ["id"] = "T2", ["statement"] = "Proof about prime numbers" },
            new Dictionary<string, object> { ["id"] = "T3", ["statement"] = "City traffic can be improved by coordination" }
        };

        var variables = new Dictionary<string, object>
        {
            ["candidate"] = new Dictionary<string, object> { ["statement"] = "city traffic optimization" },
            ["state"] = new Dictionary<string, object> { ["theorems"] = theorems }
        };

        var step = new StepDefinition
        {
            Id = "retrieve",
            Type = "retrieve_facts",
            Store = "relevant",
            Parameters = new Dictionary<string, object?>
            {
                ["query"] = "{{ candidate.statement }}",
                ["source"] = "state.theorems",
                ["text_field"] = "statement",
                ["id_field"] = "id",
                ["top_k"] = 2,
                ["mode"] = "lexical"
            }
        };

        var result = executor.Execute(step, variables);
        result.Success.ShouldBeTrue(result.Error);

        var relevant = (List<object>)variables["relevant"];
        relevant.Count.ShouldBe(2);

        var ids = relevant
            .Select(x => ((Dictionary<string, object>)x)["id"].ToString())
            .ToList();

        // T1 and T3 should be preferred over the unrelated T2.
        ids.ShouldContain("T1");
        ids.ShouldContain("T3");
        ids.ShouldNotContain("T2");
    }

    [Fact]
    public void RetrieveFactsExecutor_ShouldFallbackToBigrams_ForCjk()
    {
        var template = new TemplateEngine();
        var executor = new RetrieveFactsExecutor(template, NullLogger.Instance);

        var corpus = new List<object>
        {
            new Dictionary<string, object> { ["id"] = "T1", ["statement"] = "城市交通优化可以通过信号灯自适应" },
            new Dictionary<string, object> { ["id"] = "T2", ["statement"] = "量子纠缠与测量" }
        };

        var variables = new Dictionary<string, object>
        {
            ["q"] = "城市交通",
            ["state"] = new Dictionary<string, object> { ["theorems"] = corpus }
        };

        var step = new StepDefinition
        {
            Id = "retrieve",
            Type = "retrieve_facts",
            Store = "relevant",
            Parameters = new Dictionary<string, object?>
            {
                ["query"] = "{{ q }}",
                ["source"] = "state.theorems",
                ["text_field"] = "statement",
                ["id_field"] = "id",
                ["top_k"] = 1,
                ["mode"] = "lexical"
            }
        };

        var result = executor.Execute(step, variables);
        result.Success.ShouldBeTrue(result.Error);

        var relevant = (List<object>)variables["relevant"];
        relevant.Count.ShouldBe(1);

        var top = (Dictionary<string, object>)relevant[0];
        top["id"].ToString().ShouldBe("T1");
        ((double)top["score"]).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void TransformExecutor_ShouldMakeWorkersDeterministically()
    {
        var variables = new Dictionary<string, object>
        {
            ["k"] = 3
        };

        var ops = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["op"] = "make_workers",
                ["n"] = "{{ (k * 2) - 1 }}",
                ["id_prefix"] = "worker-",
                ["store"] = "provers"
            }
        };

        var step = new StepDefinition
        {
            Id = "build_provers",
            Type = "transform",
            Parameters = new Dictionary<string, object?> { ["ops"] = ops }
        };

        var executor = new TransformExecutor(new TemplateEngine(), NullLogger.Instance);
        var result = executor.Execute(step, variables);
        result.Success.ShouldBeTrue(result.Error);

        var provers = (List<object>)variables["provers"];
        provers.Count.ShouldBe(5);

        var first = (Dictionary<string, object>)provers[0];
        first["id"].ToString().ShouldBe("worker-0");
        first["role"].ToString().ShouldNotBeNullOrWhiteSpace();
        first["angle"].ToString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TransformExecutor_ShouldPatchState_WithSetPathIncAndAppend()
    {
        var state = new Dictionary<string, object>
        {
            ["iteration"] = 0L,
            ["seen_hypotheses"] = new List<object>(),
            ["done"] = false,
            ["status"] = "running"
        };

        var variables = new Dictionary<string, object>
        {
            ["state"] = state,
            ["max_depth"] = 1,
            ["candidate"] = new Dictionary<string, object> { ["statement"] = "  Foo   Bar  " },
            ["next_hypothesis"] = new Dictionary<string, object> { ["statement"] = "B", ["motivation"] = "m" }
        };

        var ops = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["op"] = "normalize",
                ["value"] = "{{ candidate.statement }}",
                ["store"] = "cand_norm"
            },
            new()
            {
                ["op"] = "inc_path",
                ["target"] = "state",
                ["path"] = "iteration",
                ["by"] = 1
            },
            new()
            {
                ["op"] = "append_unique_path",
                ["target"] = "state",
                ["path"] = "seen_hypotheses",
                ["value"] = "{{ cand_norm }}"
            },
            new()
            {
                ["op"] = "set_path",
                ["target"] = "state",
                ["path"] = "current_hypothesis",
                ["value"] = new Dictionary<string, object?>
                {
                    ["id"] = "H{{ state.iteration }}",
                    ["statement"] = "{{ next_hypothesis.statement }}",
                    ["motivation"] = "{{ next_hypothesis.motivation }}"
                }
            },
            new()
            {
                ["op"] = "eval",
                ["expr"] = "state.iteration >= max_depth",
                ["store"] = "reach_limit"
            },
            new()
            {
                ["op"] = "set_path",
                ["target"] = "state",
                ["path"] = "done",
                ["value"] = "{{ reach_limit }}"
            },
            new()
            {
                ["op"] = "set_path",
                ["target"] = "state",
                ["path"] = "status",
                ["value"] = "running"
            },
            new()
            {
                ["op"] = "set_path_if",
                ["if"] = "{{ reach_limit }}",
                ["target"] = "state",
                ["path"] = "status",
                ["value"] = "limit"
            }
        };

        var step = new StepDefinition
        {
            Id = "patch_state",
            Type = "transform",
            Parameters = new Dictionary<string, object?> { ["ops"] = ops }
        };

        var executor = new TransformExecutor(new TemplateEngine(), NullLogger.Instance);
        var result = executor.Execute(step, variables);
        result.Success.ShouldBeTrue(result.Error);

        ((long)state["iteration"]).ShouldBe(1L);
        ((bool)state["done"]).ShouldBeTrue();
        state["status"].ToString().ShouldBe("limit");

        var seen = (List<object>)state["seen_hypotheses"];
        seen.Count.ShouldBe(1);
        seen[0].ToString().ShouldBe("foo bar");

        var cur = (Dictionary<string, object>)state["current_hypothesis"];
        cur["id"].ToString().ShouldBe("H1");
        cur["statement"].ToString().ShouldBe("B");
    }
}

