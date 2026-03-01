namespace Tnzi.AI.Tests;

/// <summary>
/// HierarchicalChunkingStrategy 单元测试 — 验证父子块关系创建、标题分割、固定大小子块拆分
/// </summary>
public class HierarchicalChunkingTests
{
    private readonly HierarchicalChunkingStrategy _strategy = new();

    [Fact]
    public void Chunk_EmptyText_ReturnsEmpty()
    {
        _strategy.Chunk("", chunkSize: 100, chunkOverlap: 0).ShouldBeEmpty();
        _strategy.Chunk("   ", chunkSize: 100, chunkOverlap: 0).ShouldBeEmpty();
        _strategy.Chunk(null!, chunkSize: 100, chunkOverlap: 0).ShouldBeEmpty();
    }

    [Fact]
    public void Chunk_ShortText_ReturnsSingleChunk()
    {
        var text = "Hello, world!";
        var result = _strategy.Chunk(text, chunkSize: 100, chunkOverlap: 0);

        result.Count.ShouldBe(1);
        result[0].ShouldBe(text);
    }

    [Fact]
    public void ChunkWithHierarchy_EmptyText_ReturnsEmptyResult()
    {
        var result = _strategy.ChunkWithHierarchy("", chunkSize: 100, chunkOverlap: 0);

        result.Parents.ShouldBeEmpty();
        result.Children.ShouldBeEmpty();
    }

    [Fact]
    public void ChunkWithHierarchy_HeaderBasedParentSplitting_CreatesMultipleParents()
    {
        var text = """
            # Section 1
            Content of section one with some detail.

            ## Section 2
            Content of section two with more detail.

            ### Section 3
            Content of section three with extra detail.
            """;

        // chunkSize 设置足够大，确保不会有子块
        var result = _strategy.ChunkWithHierarchy(text, chunkSize: 500, chunkOverlap: 0);

        result.Parents.Count.ShouldBeGreaterThanOrEqualTo(3);
        result.Parents.ShouldContain(p => p.Content.Contains("Section 1"));
        result.Parents.ShouldContain(p => p.Content.Contains("Section 2"));
        result.Parents.ShouldContain(p => p.Content.Contains("Section 3"));
        // 因为每个 section 小于 chunkSize，不应有子块
        result.Children.ShouldBeEmpty();
    }

    [Fact]
    public void ChunkWithHierarchy_LongSection_CreatesChildChunks()
    {
        // 第二个 section 很长，需要拆分为子块
        var longContent = string.Join(". ", Enumerable.Range(1, 80).Select(i => $"Detail number {i}")) + ".";
        var text = $"# Short Section\nShort content.\n\n# Long Section\n{longContent}";

        var result = _strategy.ChunkWithHierarchy(text, chunkSize: 200, chunkOverlap: 0);

        // 应有 2 个父块
        result.Parents.Count.ShouldBe(2);

        // 第一个父块应该没有子块（内容短）
        var shortSectionChildren = result.GetChildren(0);
        shortSectionChildren.Count.ShouldBe(0);

        // 第二个父块应该有多个子块（内容长）
        var longSectionChildren = result.GetChildren(1);
        longSectionChildren.Count.ShouldBeGreaterThan(1);

        // 所有子块的 ParentIndex 应该指向第二个父块
        foreach (var child in longSectionChildren)
        {
            child.ParentIndex.ShouldBe(1);
        }
    }

    [Fact]
    public void ChunkWithHierarchy_ChildChunksRespectMaxSize()
    {
        var longContent = string.Join(". ", Enumerable.Range(1, 100).Select(i => $"Sentence number {i}")) + ".";
        var text = $"# Section\n{longContent}";

        var chunkSize = 150;
        var result = _strategy.ChunkWithHierarchy(text, chunkSize, chunkOverlap: 0);

        // 所有子块不应超过 chunkSize
        foreach (var child in result.Children)
        {
            child.Content.Length.ShouldBeLessThanOrEqualTo(chunkSize);
        }
    }

    [Fact]
    public void Chunk_FlatOutput_ContainsAllChildChunks()
    {
        var longContent = string.Join(". ", Enumerable.Range(1, 50).Select(i => $"Detail {i}")) + ".";
        var text = $"# Section A\nShort.\n\n# Section B\n{longContent}";

        var flatChunks = _strategy.Chunk(text, chunkSize: 100, chunkOverlap: 0);

        // 应该有多个块（至少包含 section A 和 section B 的子块）
        flatChunks.Count.ShouldBeGreaterThan(1);

        // 所有块都不应为空
        foreach (var chunk in flatChunks)
        {
            chunk.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void ChunkWithHierarchy_NoHeaders_FallsBackToParagraphSplit()
    {
        // 无 Markdown 标题，使用双换行符分割
        var text = "First paragraph content here.\n\nSecond paragraph content here.\n\nThird paragraph content here.";

        // chunkSize 比总长度小但比单段大
        var result = _strategy.ChunkWithHierarchy(text, chunkSize: 500, chunkOverlap: 0);

        result.Parents.Count.ShouldBeGreaterThanOrEqualTo(3);
        result.Parents.ShouldContain(p => p.Content.Contains("First paragraph"));
        result.Parents.ShouldContain(p => p.Content.Contains("Second paragraph"));
        result.Parents.ShouldContain(p => p.Content.Contains("Third paragraph"));
    }

    [Fact]
    public void ChunkWithHierarchy_GetChildren_ReturnsCorrectChildren()
    {
        var text = """
            # Section 1
            Short content.

            # Section 2
            Also short content.
            """;

        // 使用大 chunkSize 确保没有子块
        var result = _strategy.ChunkWithHierarchy(text, chunkSize: 500, chunkOverlap: 0);

        // 两个父块都没有子块
        result.GetChildren(0).ShouldBeEmpty();
        result.GetChildren(1).ShouldBeEmpty();
        // 不存在的索引也返回空
        result.GetChildren(99).ShouldBeEmpty();
    }

    [Fact]
    public void ChunkWithHierarchy_ParentContentPreservesHeaders()
    {
        var text = "# Important Title\nSome content under the title.\n\n## Sub Title\nMore content here.";

        var result = _strategy.ChunkWithHierarchy(text, chunkSize: 500, chunkOverlap: 0);

        // 父块应保留标题
        result.Parents.ShouldContain(p => p.Content.Contains("# Important Title"));
        result.Parents.ShouldContain(p => p.Content.Contains("## Sub Title"));
    }

    [Fact]
    public void Chunk_WithOverlap_ChildChunksHaveOverlap()
    {
        var longContent = string.Join(". ", Enumerable.Range(1, 60).Select(i => $"Word{i}")) + ".";
        var text = $"# Section\n{longContent}";

        var result = _strategy.Chunk(text, chunkSize: 80, chunkOverlap: 20);

        result.Count.ShouldBeGreaterThan(1);

        // 验证相邻块之间有共同内容（重叠）
        for (var i = 0; i < result.Count - 1; i++)
        {
            var currentWords = result[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var nextWords = result[i + 1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var overlap = currentWords.Intersect(nextWords).Any();
            overlap.ShouldBeTrue($"Chunks {i} and {i + 1} should have overlapping content");
        }
    }

    [Fact]
    public void ChunkWithHierarchy_SingleSection_CreatesSingleParent()
    {
        var text = "# Only Section\nThis is the only section content.";

        var result = _strategy.ChunkWithHierarchy(text, chunkSize: 500, chunkOverlap: 0);

        result.Parents.Count.ShouldBe(1);
        result.Parents[0].Content.ShouldContain("Only Section");
        result.Children.ShouldBeEmpty();
    }
}
