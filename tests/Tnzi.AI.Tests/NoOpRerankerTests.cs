
namespace Tnzi.AI.Tests;

/// <summary>
/// NoOpReranker 单元测试 - 验证透传重排序器原样返回输入
/// </summary>
public class NoOpRerankerTests
{
    [Fact]
    public async Task Rerank_ReturnsInputUnchanged()
    {
        var reranker = new NoOpReranker();

        var results = new List<VectorSearchResult>
        {
            new() { Id = Guid.NewGuid(), Content = "Result A", Score = 0.9 },
            new() { Id = Guid.NewGuid(), Content = "Result B", Score = 0.7 },
            new() { Id = Guid.NewGuid(), Content = "Result C", Score = 0.5 }
        };

        var reranked = await reranker.RerankAsync("test query", results, topK: 3);

        reranked.Count.ShouldBe(3);
        reranked[0].Content.ShouldBe("Result A");
        reranked[1].Content.ShouldBe("Result B");
        reranked[2].Content.ShouldBe("Result C");
    }

    [Fact]
    public async Task Rerank_TopKLessThanResults_TruncatesToTopK()
    {
        var reranker = new NoOpReranker();

        var results = new List<VectorSearchResult>
        {
            new() { Id = Guid.NewGuid(), Content = "First", Score = 0.9 },
            new() { Id = Guid.NewGuid(), Content = "Second", Score = 0.7 },
            new() { Id = Guid.NewGuid(), Content = "Third", Score = 0.5 }
        };

        var reranked = await reranker.RerankAsync("query", results, topK: 2);

        reranked.Count.ShouldBe(2);
        reranked[0].Content.ShouldBe("First");
        reranked[1].Content.ShouldBe("Second");
    }

    [Fact]
    public async Task Rerank_EmptyResults_ReturnsEmpty()
    {
        var reranker = new NoOpReranker();

        var reranked = await reranker.RerankAsync("query", [], topK: 5);

        reranked.ShouldBeEmpty();
    }

    [Fact]
    public async Task Rerank_TopKGreaterThanResults_ReturnsAll()
    {
        var reranker = new NoOpReranker();

        var results = new List<VectorSearchResult>
        {
            new() { Id = Guid.NewGuid(), Content = "Only one", Score = 0.8 }
        };

        var reranked = await reranker.RerankAsync("query", results, topK: 10);

        reranked.Count.ShouldBe(1);
    }
}
