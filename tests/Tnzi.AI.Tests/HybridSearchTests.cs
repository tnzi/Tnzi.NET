using Microsoft.Extensions.Logging.Abstractions;

namespace Tnzi.AI.Tests;

/// <summary>
/// HybridSearchService + InMemoryKeywordSearchProvider 单元测试
/// </summary>
public class HybridSearchTests
{
    #region RRF 融合测试

    [Fact]
    public void RrfFusion_MergesScoresCorrectly()
    {
        // 准备 HybridSearchService（通过反射或直接调用 internal 方法测试 RRF 逻辑）
        var hybridOptions = new AIRagOptions
        {
            HybridSearch = new HybridSearchOptions
            {
                VectorWeight = 0.7,
                KeywordWeight = 0.3,
                FusionConstantK = 60
            }
        };

        var service = CreateHybridSearchService(hybridOptions);

        var chunkIdBoth = Guid.NewGuid();
        var chunkIdVectorOnly = Guid.NewGuid();
        var chunkIdKeywordOnly = Guid.NewGuid();

        var vectorResults = new List<VectorSearchResult>
        {
            new() { Id = chunkIdBoth, Content = "Both", DocumentId = Guid.NewGuid(), KnowledgeBaseId = Guid.NewGuid(), Score = 0.95 },
            new() { Id = chunkIdVectorOnly, Content = "Vector only", DocumentId = Guid.NewGuid(), KnowledgeBaseId = Guid.NewGuid(), Score = 0.85 }
        };

        var keywordResults = new List<KeywordSearchResult>
        {
            new() { ChunkId = chunkIdBoth, Content = "Both", DocumentId = vectorResults[0].DocumentId, KnowledgeBaseId = vectorResults[0].KnowledgeBaseId, Score = 5.0 },
            new() { ChunkId = chunkIdKeywordOnly, Content = "Keyword only", DocumentId = Guid.NewGuid(), KnowledgeBaseId = Guid.NewGuid(), Score = 3.0 }
        };

        var fused = service.ReciprocalRankFusion(vectorResults, keywordResults, topK: 10);

        // chunkIdBoth 同时出现在两路结果中，RRF 分数应该最高
        fused.Count.ShouldBe(3);
        fused[0].Id.ShouldBe(chunkIdBoth);

        // 验证分数计算（RRF: alpha/(k+rank) + beta/(k+rank)）
        // chunkIdBoth: 0.7/(60+1) + 0.3/(60+1) = 1.0/61 ≈ 0.01639
        var expectedBothScore = 0.7 / 61.0 + 0.3 / 61.0;
        fused[0].Score.ShouldBe(expectedBothScore, tolerance: 0.0001);
    }

    [Fact]
    public void RrfFusion_DeduplicatesByChunkId()
    {
        var hybridOptions = new AIRagOptions
        {
            HybridSearch = new HybridSearchOptions
            {
                VectorWeight = 0.6,
                KeywordWeight = 0.4,
                FusionConstantK = 60
            }
        };

        var service = CreateHybridSearchService(hybridOptions);

        var sharedChunkId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        var kbId = Guid.NewGuid();

        var vectorResults = new List<VectorSearchResult>
        {
            new() { Id = sharedChunkId, Content = "Shared chunk", DocumentId = docId, KnowledgeBaseId = kbId, Score = 0.9 }
        };

        var keywordResults = new List<KeywordSearchResult>
        {
            new() { ChunkId = sharedChunkId, Content = "Shared chunk", DocumentId = docId, KnowledgeBaseId = kbId, Score = 4.0 }
        };

        var fused = service.ReciprocalRankFusion(vectorResults, keywordResults, topK: 10);

        // 相同 ChunkId 只出现一次（去重）
        fused.Count.ShouldBe(1);
        fused[0].Id.ShouldBe(sharedChunkId);
    }

    [Fact]
    public void RrfFusion_EmptyInputs_ReturnsEmpty()
    {
        var service = CreateHybridSearchService(new AIRagOptions());

        var fused = service.ReciprocalRankFusion([], [], topK: 10);

        fused.ShouldBeEmpty();
    }

    [Fact]
    public void RrfFusion_TopKLimitsResults()
    {
        var service = CreateHybridSearchService(new AIRagOptions());

        var vectorResults = Enumerable.Range(0, 10)
            .Select(i => new VectorSearchResult
            {
                Id = Guid.NewGuid(),
                Content = $"V{i}",
                DocumentId = Guid.NewGuid(),
                KnowledgeBaseId = Guid.NewGuid(),
                Score = 0.9 - i * 0.05
            })
            .ToList();

        var fused = service.ReciprocalRankFusion(vectorResults, [], topK: 3);

        fused.Count.ShouldBe(3);
    }

    #endregion

    #region InMemoryKeywordSearch 测试

    [Fact]
    public void InMemoryKeywordSearch_Tokenize_WorksCorrectly()
    {
        var tokens = InMemoryKeywordSearchProvider.Tokenize("Hello, World! This is a test.");

        tokens.ShouldContain("hello");
        tokens.ShouldContain("world");
        tokens.ShouldContain("this");
        tokens.ShouldContain("test");

        // 单字符词 "a" 应该被过滤
        tokens.ShouldNotContain("a");
        // "is" 有 2 个字符，应该保留
        tokens.ShouldContain("is");
    }

    [Fact]
    public void InMemoryKeywordSearch_Tokenize_EmptyText_ReturnsEmpty()
    {
        InMemoryKeywordSearchProvider.Tokenize("").ShouldBeEmpty();
        InMemoryKeywordSearchProvider.Tokenize("   ").ShouldBeEmpty();
        InMemoryKeywordSearchProvider.Tokenize(null!).ShouldBeEmpty();
    }

    [Fact]
    public async Task InMemoryKeywordSearch_EmptyQuery_ReturnsEmpty()
    {
        var chunkRepoMock = new Mock<IRepository<DocumentChunk, Guid>>();
        var loggerMock = new Mock<ILogger<InMemoryKeywordSearchProvider>>();

        var provider = new InMemoryKeywordSearchProvider(chunkRepoMock.Object, loggerMock.Object);

        var results = await provider.SearchAsync("", topK: 5);
        results.ShouldBeEmpty();

        var results2 = await provider.SearchAsync("   ", topK: 5);
        results2.ShouldBeEmpty();
    }

    [Fact]
    public void InMemoryKeywordSearch_Tokenize_SpecialCharacters()
    {
        var tokens = InMemoryKeywordSearchProvider.Tokenize("C# is great; Python (v3.12) is also [amazing]");

        // 验证标点被正确分割
        tokens.ShouldContain("c#");
        tokens.ShouldContain("great");
        tokens.ShouldContain("python");
        tokens.ShouldContain("v3");
        tokens.ShouldContain("12");
        tokens.ShouldContain("also");
        tokens.ShouldContain("amazing");
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 创建 HybridSearchService 用于测试 RRF 融合逻辑
    /// </summary>
    private static HybridSearchService CreateHybridSearchService(AIRagOptions options)
    {
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(ILoggerFactory)))
            .Returns(NullLoggerFactory.Instance);

        var vectorStoreMock = new Mock<IVectorStore>();
        var keywordProviderMock = new Mock<IKeywordSearchProvider>();
        var embeddingServiceMock = new Mock<IEmbeddingService>();
        var rerankerMock = new Mock<IReranker>();
        var docRepoMock = new Mock<IRepository<KnowledgeDocument, Guid>>();
        var ragOptions = Microsoft.Extensions.Options.Options.Create(options);

        return new HybridSearchService(
            serviceProviderMock.Object,
            vectorStoreMock.Object,
            keywordProviderMock.Object,
            embeddingServiceMock.Object,
            rerankerMock.Object,
            docRepoMock.Object,
            ragOptions);
    }

    #endregion
}
