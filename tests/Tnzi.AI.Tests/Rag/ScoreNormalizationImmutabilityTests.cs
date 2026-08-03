
namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// ScoreNormalizationPostProcessor 不可变性测试 - 归一化必须返回新实例，
/// 不得原地修改入参对象（与 WeightedDiminishingReranker 的 immutable pattern 一致）。
/// </summary>
public class ScoreNormalizationImmutabilityTests
{
    private static ScoreNormalizationPostProcessor CreateProcessor(double minScoreThreshold = 0.0)
        => new(NullLogger<ScoreNormalizationPostProcessor>.Instance, minScoreThreshold);

    [Fact]
    public async Task ProcessAsync_DoesNotMutateInputResults()
    {
        var processor = CreateProcessor();
        var results = new List<VectorSearchResult>
        {
            new() { Id = Guid.NewGuid(), Content = "Best", Score = 100.0 },
            new() { Id = Guid.NewGuid(), Content = "Middle", Score = 50.0 },
            new() { Id = Guid.NewGuid(), Content = "Worst", Score = 10.0 }
        };

        var processed = await processor.ProcessAsync(results, "query");

        // 入参对象的 Score 保持原值
        results[0].Score.ShouldBe(100.0);
        results[1].Score.ShouldBe(50.0);
        results[2].Score.ShouldBe(10.0);

        // 返回的是新实例
        processed.Count.ShouldBe(3);
        foreach (var item in processed)
        {
            results.ShouldNotContain(item);
        }
    }

    [Fact]
    public async Task ProcessAsync_NewInstances_PreserveAllNonScoreFields()
    {
        var processor = CreateProcessor();
        var source = new VectorSearchResult
        {
            Id = Guid.NewGuid(),
            Content = "Content",
            DocumentId = Guid.NewGuid(),
            KnowledgeBaseId = Guid.NewGuid(),
            ChunkIndex = 7,
            Metadata = """{"source":"a.txt"}""",
            ParentChunkId = Guid.NewGuid(),
            ContentHash = "hash-1",
            Score = 0.8
        };
        var other = new VectorSearchResult { Id = Guid.NewGuid(), Content = "Other", Score = 0.2 };

        var processed = await processor.ProcessAsync([source, other], "query");

        var normalized = processed.Single(r => r.Id == source.Id);
        normalized.Content.ShouldBe(source.Content);
        normalized.DocumentId.ShouldBe(source.DocumentId);
        normalized.KnowledgeBaseId.ShouldBe(source.KnowledgeBaseId);
        normalized.ChunkIndex.ShouldBe(source.ChunkIndex);
        normalized.Metadata.ShouldBe(source.Metadata);
        normalized.ParentChunkId.ShouldBe(source.ParentChunkId);
        normalized.ContentHash.ShouldBe(source.ContentHash);
        normalized.Score.ShouldBe(1.0); // max → 1.0
    }

    [Fact]
    public async Task ProcessAsync_WithThreshold_InputStillUntouched()
    {
        var processor = CreateProcessor(minScoreThreshold: 0.5);
        var results = new List<VectorSearchResult>
        {
            new() { Id = Guid.NewGuid(), Content = "High", Score = 1.0 },
            new() { Id = Guid.NewGuid(), Content = "Low", Score = 0.0 }
        };

        var processed = await processor.ProcessAsync(results, "query");

        processed.Count.ShouldBe(1);
        processed[0].Content.ShouldBe("High");

        // 即便发生阈值过滤，入参对象也未被修改
        results[0].Score.ShouldBe(1.0);
        results[1].Score.ShouldBe(0.0);
    }
}
