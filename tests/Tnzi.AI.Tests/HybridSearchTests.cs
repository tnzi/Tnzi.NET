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

    #region SearchAsync 端到端管线测试（嵌入对齐 + 双路 + RRF + 重排 + 映射）

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmpty()
    {
        var (service, _) = CreateHybridSearchServiceWithMocks(new AIRagOptions());

        (await service.SearchAsync("")).ShouldBeEmpty();
        (await service.SearchAsync("   ")).ShouldBeEmpty();
    }

    [Fact]
    public async Task SearchAsync_UsesRagDefaultEmbeddingProviderAndModel()
    {
        // 锁定 #1 修复：查询向量必须用 RAG 全局默认 embedding 配置，
        // 而非回退到 AI 顶层 DefaultProvider（否则向量空间不一致）。
        var options = new AIRagOptions
        {
            DefaultEmbeddingProvider = "rag-embed-provider",
            DefaultEmbeddingModel = "rag-embed-model"
        };
        var (service, mocks) = CreateHybridSearchServiceWithMocks(options);

        EmbeddingOptions? capturedOptions = null;
        mocks.Embedding
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, EmbeddingOptions?, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(Result<float[]>.Success(new[] { 0.1f, 0.2f }));

        mocks.VectorStore
            .Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>());
        mocks.Keyword
            .Setup(k => k.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KeywordSearchResult>());

        await service.SearchAsync("how does hybrid search work");

        capturedOptions.ShouldNotBeNull();
        capturedOptions!.Provider.ShouldBe("rag-embed-provider");
        capturedOptions.Model.ShouldBe("rag-embed-model");
    }

    [Fact]
    public async Task SearchAsync_EmbeddingFails_ReturnsEmpty()
    {
        var (service, mocks) = CreateHybridSearchServiceWithMocks(new AIRagOptions());

        mocks.Embedding
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Failure("embedding API error"));

        (await service.SearchAsync("query")).ShouldBeEmpty();

        // 嵌入失败应短路：双路检索不应执行
        mocks.VectorStore.Verify(
            v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        mocks.Keyword.Verify(
            k => k.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchAsync_FusesBothPaths_RerankThenMapsToTextSearchResults()
    {
        var options = new AIRagOptions
        {
            DefaultEmbeddingProvider = "openai",
            DefaultEmbeddingModel = "text-embedding-3-small",
            HybridSearch = new HybridSearchOptions
            {
                VectorWeight = 0.6,
                KeywordWeight = 0.4,
                FusionConstantK = 60
            }
        };
        var (service, mocks) = CreateHybridSearchServiceWithMocks(options);

        var queryVector = new[] { 0.1f, 0.2f, 0.3f };
        mocks.Embedding
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success(queryVector));

        var docVector = Guid.NewGuid();
        var docKeyword = Guid.NewGuid();
        var kbId = Guid.NewGuid();
        var chunkVectorOnly = Guid.NewGuid();
        var chunkKeywordOnly = Guid.NewGuid();

        // 向量路命中 chunkVectorOnly；关键词路命中 chunkKeywordOnly（无重叠 → 融合后 2 条）
        mocks.VectorStore
            .Setup(v => v.SearchAsync(queryVector, It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>
            {
                new() { Id = chunkVectorOnly, Content = "Vector hit", DocumentId = docVector, KnowledgeBaseId = kbId, ChunkIndex = 2, Score = 0.95 }
            });
        mocks.Keyword
            .Setup(k => k.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KeywordSearchResult>
            {
                new() { ChunkId = chunkKeywordOnly, Content = "Keyword hit", DocumentId = docKeyword, KnowledgeBaseId = kbId, Score = 4.0 }
            });

        // Reranker 透传（验证融合结果交给 reranker 且其输出被映射）
        List<VectorSearchResult>? rerankInput = null;
        mocks.Reranker
            .Setup(r => r.RerankAsync(It.IsAny<string>(), It.IsAny<List<VectorSearchResult>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<string, List<VectorSearchResult>, int, CancellationToken>((_, fused, _, _) => rerankInput = fused)
            .ReturnsAsync((string _, List<VectorSearchResult> fused, int topK, CancellationToken _) => fused.Take(topK).ToList());

        var docs = new List<KnowledgeDocument>
        {
            new() { Id = docVector, FileName = "vector-doc.pdf", KnowledgeBaseId = kbId },
            new() { Id = docKeyword, FileName = "keyword-doc.md", KnowledgeBaseId = kbId }
        };
        mocks.DocRepo.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(docs.BuildMock());

        var results = (await service.SearchAsync("query text", maxResults: 5)).ToList();

        // 融合后 2 条都进入 reranker
        rerankInput.ShouldNotBeNull();
        rerankInput!.Count.ShouldBe(2);

        // 映射结果：2 条，含正确 SourceName + hybrid searchType metadata
        results.Count.ShouldBe(2);
        results.ShouldContain(r => r.Text == "Vector hit" && r.SourceName == "vector-doc.pdf");
        results.ShouldContain(r => r.Text == "Keyword hit" && r.SourceName == "keyword-doc.md");
        foreach (var r in results)
        {
            r.Metadata.ShouldNotBeNull();
            r.Metadata!["searchType"].ShouldBe("hybrid");
            r.Metadata["knowledgeBaseId"].ShouldBe(kbId);
        }

        // 双路各检索一次（fetchCount = maxResults * 2）
        mocks.VectorStore.Verify(v => v.SearchAsync(queryVector, 10, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
        mocks.Keyword.Verify(k => k.SearchAsync("query text", 10, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_ExceptionThrown_ReturnsEmptyGracefully()
    {
        var (service, mocks) = CreateHybridSearchServiceWithMocks(new AIRagOptions());

        mocks.Embedding
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        (await service.SearchAsync("query")).ShouldBeEmpty();
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
        var ragOptions = new StaticOptionsMonitor<AIRagOptions>(options);

        return new HybridSearchService(
            serviceProviderMock.Object,
            vectorStoreMock.Object,
            keywordProviderMock.Object,
            embeddingServiceMock.Object,
            rerankerMock.Object,
            docRepoMock.Object,
            ragOptions);
    }

    /// <summary>
    /// HybridSearchService 的可配置 mock 集合（用于端到端 SearchAsync 测试）
    /// </summary>
    private sealed class HybridMocks
    {
        public required Mock<IVectorStore> VectorStore { get; init; }
        public required Mock<IKeywordSearchProvider> Keyword { get; init; }
        public required Mock<IEmbeddingService> Embedding { get; init; }
        public required Mock<IReranker> Reranker { get; init; }
        public required Mock<IRepository<KnowledgeDocument, Guid>> DocRepo { get; init; }
    }

    /// <summary>
    /// 创建 HybridSearchService 并返回其底层 mock，便于端到端配置/断言
    /// </summary>
    private static (HybridSearchService Service, HybridMocks Mocks) CreateHybridSearchServiceWithMocks(AIRagOptions options)
    {
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(ILoggerFactory)))
            .Returns(NullLoggerFactory.Instance);

        var mocks = new HybridMocks
        {
            VectorStore = new Mock<IVectorStore>(),
            Keyword = new Mock<IKeywordSearchProvider>(),
            Embedding = new Mock<IEmbeddingService>(),
            Reranker = new Mock<IReranker>(),
            DocRepo = new Mock<IRepository<KnowledgeDocument, Guid>>()
        };

        var service = new HybridSearchService(
            serviceProviderMock.Object,
            mocks.VectorStore.Object,
            mocks.Keyword.Object,
            mocks.Embedding.Object,
            mocks.Reranker.Object,
            mocks.DocRepo.Object,
            new StaticOptionsMonitor<AIRagOptions>(options));

        return (service, mocks);
    }

    #endregion
}
