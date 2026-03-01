namespace Tnzi.AI.Tests;

/// <summary>
/// Search post-processor pipeline 单元测试
/// </summary>
public class SearchPostProcessorTests
{
    #region DeduplicationPostProcessor

    [Fact]
    public async Task Deduplication_RemovesNearDuplicates()
    {
        var logger = new Mock<ILogger<DeduplicationPostProcessor>>();
        var processor = new DeduplicationPostProcessor(logger.Object, 0.85);

        var results = new List<VectorSearchResult>
        {
            new() { Id = Guid.NewGuid(), Content = "The quick brown fox jumps over the lazy dog", Score = 0.9 },
            new() { Id = Guid.NewGuid(), Content = "The quick brown fox jumps over the lazy dog today", Score = 0.8 }, // near-duplicate
            new() { Id = Guid.NewGuid(), Content = "Machine learning is a subset of artificial intelligence", Score = 0.7 }
        };

        var processed = await processor.ProcessAsync(results, "test query");

        processed.Count.ShouldBe(2);
        processed[0].Score.ShouldBe(0.9);
        processed[1].Score.ShouldBe(0.7);
    }

    [Fact]
    public async Task Deduplication_KeepsDistinctResults()
    {
        var logger = new Mock<ILogger<DeduplicationPostProcessor>>();
        var processor = new DeduplicationPostProcessor(logger.Object, 0.85);

        var results = new List<VectorSearchResult>
        {
            new() { Id = Guid.NewGuid(), Content = "Apples are a delicious fruit", Score = 0.9 },
            new() { Id = Guid.NewGuid(), Content = "Machine learning uses neural networks", Score = 0.8 },
            new() { Id = Guid.NewGuid(), Content = "PostgreSQL is a relational database", Score = 0.7 }
        };

        var processed = await processor.ProcessAsync(results, "test query");

        processed.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Deduplication_SingleResult_ReturnsUnchanged()
    {
        var logger = new Mock<ILogger<DeduplicationPostProcessor>>();
        var processor = new DeduplicationPostProcessor(logger.Object);

        var results = new List<VectorSearchResult>
        {
            new() { Id = Guid.NewGuid(), Content = "Single result", Score = 0.9 }
        };

        var processed = await processor.ProcessAsync(results, "test");

        processed.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Deduplication_EmptyResults_ReturnsEmpty()
    {
        var logger = new Mock<ILogger<DeduplicationPostProcessor>>();
        var processor = new DeduplicationPostProcessor(logger.Object);

        var processed = await processor.ProcessAsync([], "test");

        processed.ShouldBeEmpty();
    }

    [Fact]
    public void Deduplication_Order_Is10()
    {
        var logger = new Mock<ILogger<DeduplicationPostProcessor>>();
        var processor = new DeduplicationPostProcessor(logger.Object);

        processor.Order.ShouldBe(10);
    }

    #endregion

    #region ScoreNormalizationPostProcessor

    [Fact]
    public async Task ScoreNormalization_NormalizesToZeroOneRange()
    {
        var logger = new Mock<ILogger<ScoreNormalizationPostProcessor>>();
        var processor = new ScoreNormalizationPostProcessor(logger.Object);

        var results = new List<VectorSearchResult>
        {
            new() { Id = Guid.NewGuid(), Content = "Best", Score = 100.0 },
            new() { Id = Guid.NewGuid(), Content = "Middle", Score = 50.0 },
            new() { Id = Guid.NewGuid(), Content = "Worst", Score = 0.0 }
        };

        var processed = await processor.ProcessAsync(results, "test");

        processed.Count.ShouldBe(3);
        processed[0].Score.ShouldBe(1.0);
        processed[1].Score.ShouldBe(0.5);
        processed[2].Score.ShouldBe(0.0);
    }

    [Fact]
    public async Task ScoreNormalization_AllEqualScores_NormalizesToOne()
    {
        var logger = new Mock<ILogger<ScoreNormalizationPostProcessor>>();
        var processor = new ScoreNormalizationPostProcessor(logger.Object);

        var results = new List<VectorSearchResult>
        {
            new() { Id = Guid.NewGuid(), Content = "A", Score = 0.5 },
            new() { Id = Guid.NewGuid(), Content = "B", Score = 0.5 }
        };

        var processed = await processor.ProcessAsync(results, "test");

        processed.Count.ShouldBe(2);
        processed.All(r => r.Score == 1.0).ShouldBeTrue();
    }

    [Fact]
    public async Task ScoreNormalization_WithMinThreshold_FiltersLowScores()
    {
        var logger = new Mock<ILogger<ScoreNormalizationPostProcessor>>();
        var processor = new ScoreNormalizationPostProcessor(logger.Object, 0.5);

        var results = new List<VectorSearchResult>
        {
            new() { Id = Guid.NewGuid(), Content = "Best", Score = 1.0 },
            new() { Id = Guid.NewGuid(), Content = "Middle", Score = 0.5 },
            new() { Id = Guid.NewGuid(), Content = "Worst", Score = 0.0 }
        };

        var processed = await processor.ProcessAsync(results, "test");

        // After normalization: Best=1.0, Middle=0.5, Worst=0.0
        // Threshold 0.5 keeps Best and Middle
        processed.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ScoreNormalization_EmptyResults_ReturnsEmpty()
    {
        var logger = new Mock<ILogger<ScoreNormalizationPostProcessor>>();
        var processor = new ScoreNormalizationPostProcessor(logger.Object);

        var processed = await processor.ProcessAsync([], "test");

        processed.ShouldBeEmpty();
    }

    [Fact]
    public void ScoreNormalization_Order_Is20()
    {
        var logger = new Mock<ILogger<ScoreNormalizationPostProcessor>>();
        var processor = new ScoreNormalizationPostProcessor(logger.Object);

        processor.Order.ShouldBe(20);
    }

    #endregion
}
