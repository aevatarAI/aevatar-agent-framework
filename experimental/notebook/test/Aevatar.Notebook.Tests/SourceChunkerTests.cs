using Aevatar.Notebook.Sources;
using Shouldly;

namespace Aevatar.Notebook.Tests;

public sealed class SourceChunkerTests
{
    [Fact]
    public void Chunk_ShouldBeDeterministic_AndBounded()
    {
        var chunker = new SourceChunker();

        var text =
            """
            alpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu
            line2: alpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu
            line3: alpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu
            """;

        var options = new SourceChunkingOptions
        {
            MaxChunkChars = 60,
            OverlapChars = 10,
            MaxChunks = 10,
            BoundaryBacktrackChars = 20,
            MinChunkChars = 10
        };

        var chunks1 = chunker.Chunk(text, options);
        var chunks2 = chunker.Chunk(text, options);

        chunks1.Count.ShouldBeGreaterThan(0);
        chunks1.Count.ShouldBeLessThanOrEqualTo(options.MaxChunks);

        chunks2.Count.ShouldBe(chunks1.Count);
        for (var i = 0; i < chunks1.Count; i++)
        {
            chunks2[i].ShouldBe(chunks1[i]);
        }
    }

    [Fact]
    public void Chunk_ShouldReturnOffsetsWithinNormalizedTextRange()
    {
        var chunker = new SourceChunker();

        var raw = "  a\r\nb\r\nc  ";
        var normalized = chunker.Normalize(raw);

        var options = new SourceChunkingOptions
        {
            MaxChunkChars = 2,
            OverlapChars = 0,
            MaxChunks = 10,
            BoundaryBacktrackChars = 1,
            MinChunkChars = 1
        };

        var chunks = chunker.Chunk(raw, options);
        chunks.Count.ShouldBeGreaterThan(0);

        foreach (var c in chunks)
        {
            c.OffsetStart.ShouldBeGreaterThanOrEqualTo(0);
            c.OffsetEnd.ShouldBeGreaterThan(c.OffsetStart);
            c.OffsetEnd.ShouldBeLessThanOrEqualTo(normalized.Length);
            c.Content.ShouldNotBeNull();
            c.Content.Trim().Length.ShouldBeGreaterThan(0);
        }
    }
}


