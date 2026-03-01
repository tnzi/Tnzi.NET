namespace Tnzi.AI.Tests;

/// <summary>
/// FixedSizeChunkingStrategy 单元测试
/// </summary>
public class FixedSizeChunkingStrategyTests
{
    private readonly FixedSizeChunkingStrategy _strategy = new();

    [Fact]
    public void Chunk_ShortText_ReturnsSingleChunk()
    {
        var text = "Hello, world!";
        var result = _strategy.Chunk(text, chunkSize: 100, chunkOverlap: 0);

        result.Count.ShouldBe(1);
        result[0].ShouldBe(text);
    }

    [Fact]
    public void Chunk_LongText_SplitsIntoMultipleChunks()
    {
        // 每句末尾有句号，方便在句子边界断开
        var text = string.Join(" ", Enumerable.Range(1, 50).Select(i => $"Sentence number {i}."));
        var result = _strategy.Chunk(text, chunkSize: 100, chunkOverlap: 0);

        result.Count.ShouldBeGreaterThan(1);
        // 每个 chunk 不超过 chunkSize
        foreach (var chunk in result)
        {
            chunk.Length.ShouldBeLessThanOrEqualTo(100);
        }
    }

    [Fact]
    public void Chunk_WithOverlap_ChunksHaveOverlap()
    {
        // 构建可预测的长文本
        var text = string.Join(" ", Enumerable.Range(1, 50).Select(i => $"Word{i}."));
        var result = _strategy.Chunk(text, chunkSize: 60, chunkOverlap: 20);

        result.Count.ShouldBeGreaterThan(1);

        // 验证相邻 chunk 有重叠内容
        for (var i = 0; i < result.Count - 1; i++)
        {
            var currentEnd = result[i][^10..]; // 当前 chunk 尾部
            var nextStart = result[i + 1][..Math.Min(30, result[i + 1].Length)]; // 下一个 chunk 头部
            // 由于 overlap，下一个 chunk 的开头应该包含当前 chunk 尾部的部分内容
            // 至少有一些共同的单词
            var currentWords = result[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var nextWords = result[i + 1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var overlap = currentWords.Intersect(nextWords).Any();
            overlap.ShouldBeTrue($"Chunks {i} and {i + 1} should have overlapping content");
        }
    }

    [Fact]
    public void Chunk_EmptyText_ReturnsEmpty()
    {
        _strategy.Chunk("", chunkSize: 100, chunkOverlap: 0).ShouldBeEmpty();
        _strategy.Chunk("   ", chunkSize: 100, chunkOverlap: 0).ShouldBeEmpty();
    }

    [Fact]
    public void Chunk_NullText_ReturnsEmpty()
    {
        _strategy.Chunk(null!, chunkSize: 100, chunkOverlap: 0).ShouldBeEmpty();
    }

    [Fact]
    public void Chunk_ExactChunkSize_ReturnsSingleChunk()
    {
        var text = new string('A', 100);
        var result = _strategy.Chunk(text, chunkSize: 100, chunkOverlap: 0);

        result.Count.ShouldBe(1);
        result[0].ShouldBe(text);
    }

    [Fact]
    public void Chunk_PrefersSentenceBoundary()
    {
        // 句号需要在 chunkSize 的最后 20% 范围内（即位置 40-50 对于 chunkSize=50）
        // "AAAA...sentence ends here." 长度约 45，句号在最后 20% 范围内
        var text = "This is padding text to fill space sentence. More text that goes beyond the chunk size limit here.";
        var result = _strategy.Chunk(text, chunkSize: 50, chunkOverlap: 0);

        result.Count.ShouldBeGreaterThan(1);
        // 第一个 chunk 应该在句号处断开（句号在最后 20% 范围内）
        result[0].ShouldEndWith(".");
    }
}

/// <summary>
/// MarkdownHeaderChunkingStrategy 单元测试
/// </summary>
public class MarkdownHeaderChunkingStrategyTests
{
    private readonly MarkdownHeaderChunkingStrategy _strategy = new();

    [Fact]
    public void Chunk_SplitsByHeaders()
    {
        // Markdown 标题必须在行首（无缩进），否则正则不匹配
        // chunkSize 必须小于总长度（否则短路返回整段），但大于单个 section 长度
        var text = "# Section 1\nContent of section one.\n\n## Section 2\nContent of section two.\n\n### Section 3\nContent of section three.";

        var result = _strategy.Chunk(text, chunkSize: 80, chunkOverlap: 0);

        result.Count.ShouldBeGreaterThanOrEqualTo(3);
        result.ShouldContain(c => c.Contains("Section 1"));
        result.ShouldContain(c => c.Contains("Section 2"));
        result.ShouldContain(c => c.Contains("Section 3"));
    }

    [Fact]
    public void Chunk_NoHeaders_FallsBackToFixedSize()
    {
        // 纯文本无标题，长度超过 chunkSize
        var text = string.Join(". ", Enumerable.Range(1, 50).Select(i => $"Sentence number {i}")) + ".";
        var result = _strategy.Chunk(text, chunkSize: 100, chunkOverlap: 0);

        // 应该退化为固定大小分块（通过 SplitByHeaders 得到一个空 header 段落，然后 fallback 分块）
        result.Count.ShouldBeGreaterThan(1);
    }

    [Fact]
    public void Chunk_LongSection_SubChunks()
    {
        // 标题下有大量内容
        var longContent = string.Join(". ", Enumerable.Range(1, 100).Select(i => $"Detail number {i}")) + ".";
        var text = $"# Main Section\n{longContent}";

        var result = _strategy.Chunk(text, chunkSize: 200, chunkOverlap: 0);

        result.Count.ShouldBeGreaterThan(1);
        // 所有子块都应该包含标题前缀
        foreach (var chunk in result)
        {
            chunk.ShouldContain("# Main Section");
        }
    }

    [Fact]
    public void Chunk_ShortText_ReturnsSingleChunk()
    {
        var text = "# Title\nShort content.";
        var result = _strategy.Chunk(text, chunkSize: 500, chunkOverlap: 0);

        result.Count.ShouldBe(1);
    }

    [Fact]
    public void Chunk_EmptyText_ReturnsEmpty()
    {
        _strategy.Chunk("", chunkSize: 100, chunkOverlap: 0).ShouldBeEmpty();
        _strategy.Chunk("   ", chunkSize: 100, chunkOverlap: 0).ShouldBeEmpty();
    }

    [Fact]
    public void Chunk_HeaderLongerThanChunkSize_DoesNotCrash()
    {
        // 标题比 chunkSize 长时不崩溃（测试负数防御）
        var longHeader = "# " + new string('A', 200);
        var text = $"{longHeader}\nSome content.";

        // 不应抛出异常
        var result = _strategy.Chunk(text, chunkSize: 50, chunkOverlap: 0);
        result.ShouldNotBeNull();
        result.Count.ShouldBeGreaterThan(0);
    }
}
