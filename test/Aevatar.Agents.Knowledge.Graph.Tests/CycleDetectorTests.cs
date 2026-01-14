using Aevatar.Agents.Knowledge.Graph.Services;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Knowledge.Graph.Tests;

/// <summary>
/// Unit tests for CycleDetector DFS-based cycle detection per FR-015b.
/// </summary>
public class CycleDetectorTests
{
    #region WouldCreateCycle Tests

    [Fact]
    public void WouldCreateCycle_SelfLoop_ReturnsTrue()
    {
        var edges = new Dictionary<string, IReadOnlyList<string>>();

        var result = CycleDetector.WouldCreateCycle("A", "A", edges);

        result.ShouldBeTrue();
    }

    [Fact]
    public void WouldCreateCycle_EmptyGraph_ReturnsFalse()
    {
        var edges = new Dictionary<string, IReadOnlyList<string>>();

        var result = CycleDetector.WouldCreateCycle("A", "B", edges);

        result.ShouldBeFalse();
    }

    [Fact]
    public void WouldCreateCycle_NoExistingPath_ReturnsFalse()
    {
        // Graph: A -> B, C (isolated)
        var edges = new Dictionary<string, IReadOnlyList<string>>
        {
            ["A"] = new List<string> { "B" }
        };

        // Adding C -> A should not create cycle
        var result = CycleDetector.WouldCreateCycle("C", "A", edges);

        result.ShouldBeFalse();
    }

    [Fact]
    public void WouldCreateCycle_DirectBackEdge_ReturnsTrue()
    {
        // Graph: A -> B
        // Adding B -> A would create cycle: A -> B -> A
        var edges = new Dictionary<string, IReadOnlyList<string>>
        {
            ["A"] = new List<string> { "B" }
        };

        var result = CycleDetector.WouldCreateCycle("B", "A", edges);

        result.ShouldBeTrue();
    }

    [Fact]
    public void WouldCreateCycle_IndirectBackEdge_ReturnsTrue()
    {
        // Graph: A -> B -> C
        // Adding C -> A would create cycle: A -> B -> C -> A
        var edges = new Dictionary<string, IReadOnlyList<string>>
        {
            ["A"] = new List<string> { "B" },
            ["B"] = new List<string> { "C" }
        };

        var result = CycleDetector.WouldCreateCycle("C", "A", edges);

        result.ShouldBeTrue();
    }

    [Fact]
    public void WouldCreateCycle_LongChain_DetectsCycle()
    {
        // Graph: A -> B -> C -> D -> E
        // Adding E -> A would create cycle
        var edges = new Dictionary<string, IReadOnlyList<string>>
        {
            ["A"] = new List<string> { "B" },
            ["B"] = new List<string> { "C" },
            ["C"] = new List<string> { "D" },
            ["D"] = new List<string> { "E" }
        };

        var result = CycleDetector.WouldCreateCycle("E", "A", edges);

        result.ShouldBeTrue();
    }

    [Fact]
    public void WouldCreateCycle_DiamondGraph_NoCycle()
    {
        // Diamond: A -> B, A -> C, B -> D, C -> D
        // This is not a cycle, just a diamond pattern
        var edges = new Dictionary<string, IReadOnlyList<string>>
        {
            ["A"] = new List<string> { "B", "C" },
            ["B"] = new List<string> { "D" },
            ["C"] = new List<string> { "D" }
        };

        // Adding E -> D should not create cycle
        var result = CycleDetector.WouldCreateCycle("E", "D", edges);

        result.ShouldBeFalse();
    }

    #endregion

    #region FindCycleCreatingEdge Tests

    [Fact]
    public void FindCycleCreatingEdge_NoEdges_ReturnsNull()
    {
        var edges = new Dictionary<string, IReadOnlyList<string>>();
        var newEdges = new List<(string Source, string Target)>();

        var result = CycleDetector.FindCycleCreatingEdge(newEdges, edges);

        result.ShouldBeNull();
    }

    [Fact]
    public void FindCycleCreatingEdge_ValidEdges_ReturnsNull()
    {
        var edges = new Dictionary<string, IReadOnlyList<string>>
        {
            ["A"] = new List<string> { "B" }
        };
        var newEdges = new List<(string Source, string Target)>
        {
            ("C", "A"),
            ("D", "C")
        };

        var result = CycleDetector.FindCycleCreatingEdge(newEdges, edges);

        result.ShouldBeNull();
    }

    [Fact]
    public void FindCycleCreatingEdge_FirstEdgeCausesCycle_ReturnsFirst()
    {
        var edges = new Dictionary<string, IReadOnlyList<string>>
        {
            ["A"] = new List<string> { "B" }
        };
        var newEdges = new List<(string Source, string Target)>
        {
            ("B", "A"), // This would create cycle
            ("C", "D")
        };

        var result = CycleDetector.FindCycleCreatingEdge(newEdges, edges);

        result.ShouldNotBeNull();
        result.Value.Source.ShouldBe("B");
        result.Value.Target.ShouldBe("A");
    }

    [Fact]
    public void FindCycleCreatingEdge_LaterEdgeCausesCycle_ReturnsCorrect()
    {
        var edges = new Dictionary<string, IReadOnlyList<string>>();
        var newEdges = new List<(string Source, string Target)>
        {
            ("A", "B"), // Valid
            ("B", "C"), // Valid
            ("C", "A")  // Creates cycle
        };

        var result = CycleDetector.FindCycleCreatingEdge(newEdges, edges);

        result.ShouldNotBeNull();
        result.Value.Source.ShouldBe("C");
        result.Value.Target.ShouldBe("A");
    }

    [Fact]
    public void FindCycleCreatingEdge_SelfLoop_ReturnsSelfLoop()
    {
        var edges = new Dictionary<string, IReadOnlyList<string>>();
        var newEdges = new List<(string Source, string Target)>
        {
            ("A", "A") // Self loop
        };

        var result = CycleDetector.FindCycleCreatingEdge(newEdges, edges);

        result.ShouldNotBeNull();
        result.Value.Source.ShouldBe("A");
        result.Value.Target.ShouldBe("A");
    }

    #endregion

    #region TopologicalSort Tests

    [Fact]
    public void TopologicalSort_EmptyGraph_ReturnsEmpty()
    {
        var nodeIds = new List<string>();
        var edges = new Dictionary<string, IReadOnlyList<string>>();

        var result = CycleDetector.TopologicalSort(nodeIds, edges);

        result.ShouldNotBeNull();
        result.ShouldBeEmpty();
    }

    [Fact]
    public void TopologicalSort_SingleNode_ReturnsSingleNode()
    {
        var nodeIds = new List<string> { "A" };
        var edges = new Dictionary<string, IReadOnlyList<string>>();

        var result = CycleDetector.TopologicalSort(nodeIds, edges);

        result.ShouldNotBeNull();
        result.Count.ShouldBe(1);
        result[0].ShouldBe("A");
    }

    [Fact]
    public void TopologicalSort_LinearChain_ReturnsDependenciesFirst()
    {
        // A -> B -> C (A depends on B, B depends on C)
        var nodeIds = new List<string> { "A", "B", "C" };
        var edges = new Dictionary<string, IReadOnlyList<string>>
        {
            ["A"] = new List<string> { "B" },
            ["B"] = new List<string> { "C" }
        };

        var sorted = CycleDetector.TopologicalSort(nodeIds, edges);

        sorted.ShouldNotBeNull();
        sorted.Count.ShouldBe(3);
        var result = sorted.ToList();
        // C should come before B, B should come before A
        result.IndexOf("C").ShouldBeLessThan(result.IndexOf("B"));
        result.IndexOf("B").ShouldBeLessThan(result.IndexOf("A"));
    }

    [Fact]
    public void TopologicalSort_DiamondGraph_ValidOrdering()
    {
        // A -> B, A -> C, B -> D, C -> D
        var nodeIds = new List<string> { "A", "B", "C", "D" };
        var edges = new Dictionary<string, IReadOnlyList<string>>
        {
            ["A"] = new List<string> { "B", "C" },
            ["B"] = new List<string> { "D" },
            ["C"] = new List<string> { "D" }
        };

        var sorted = CycleDetector.TopologicalSort(nodeIds, edges);

        sorted.ShouldNotBeNull();
        sorted.Count.ShouldBe(4);
        var result = sorted.ToList();
        // D should come before B and C, B and C should come before A
        result.IndexOf("D").ShouldBeLessThan(result.IndexOf("B"));
        result.IndexOf("D").ShouldBeLessThan(result.IndexOf("C"));
        result.IndexOf("B").ShouldBeLessThan(result.IndexOf("A"));
        result.IndexOf("C").ShouldBeLessThan(result.IndexOf("A"));
    }

    [Fact]
    public void TopologicalSort_GraphWithCycle_ReturnsNull()
    {
        // A -> B -> C -> A (cycle)
        var nodeIds = new List<string> { "A", "B", "C" };
        var edges = new Dictionary<string, IReadOnlyList<string>>
        {
            ["A"] = new List<string> { "B" },
            ["B"] = new List<string> { "C" },
            ["C"] = new List<string> { "A" }
        };

        var result = CycleDetector.TopologicalSort(nodeIds, edges);

        result.ShouldBeNull();
    }

    [Fact]
    public void TopologicalSort_DisconnectedComponents_ReturnsAll()
    {
        // A -> B, C -> D (two disconnected components)
        var nodeIds = new List<string> { "A", "B", "C", "D" };
        var edges = new Dictionary<string, IReadOnlyList<string>>
        {
            ["A"] = new List<string> { "B" },
            ["C"] = new List<string> { "D" }
        };

        var sorted = CycleDetector.TopologicalSort(nodeIds, edges);

        sorted.ShouldNotBeNull();
        sorted.Count.ShouldBe(4);
        var result = sorted.ToList();
        result.IndexOf("B").ShouldBeLessThan(result.IndexOf("A"));
        result.IndexOf("D").ShouldBeLessThan(result.IndexOf("C"));
    }

    #endregion
}
