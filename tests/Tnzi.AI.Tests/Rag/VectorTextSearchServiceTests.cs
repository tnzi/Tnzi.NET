using Microsoft.Extensions.Logging.Abstractions;

namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// VectorTextSearchService 单元测试 — 验证向量搜索管线各阶段
/// </summary>
public class VectorTextSearchServiceTests
{
    private readonly Mock<IEmbeddingService> _embeddingServiceMock = new();
    private readonly Mock<IVectorStore> _vectorStoreMock = new();
    private readonly Mock<IReranker> _rerankerMock = new();
    private readonly Mock<IRepository<KnowledgeDocument, Guid>> _docRepoMock = new();
    private readonly Mock<IRepository<KnowledgeBase, Guid>> _kbRepoMock = new();

    private VectorTextSearchService CreateService(
        IQueryRewriter? queryRewriter = null,
        IRelevanceGrader? relevanceGrader = null,
        IEnumerable<ISearchPostProcessor>? postProcessors = null,
        AIRagOptions? ragOptions = null)
    {
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(ILoggerFactory)))
            .Returns(NullLoggerFactory.Instance);

        return new VectorTextSearchService(
            serviceProviderMock.Object,
            _embeddingServiceMock.Object,
            _vectorStoreMock.Object,
            _rerankerMock.Object,
            _docRepoMock.Object,
            _kbRepoMock.Object,
            postProcessors ?? Enumerable.Empty<ISearchPostProcessor>(),
            new StaticOptionsMonitor<AIRagOptions>(ragOptions ?? new AIRagOptions
            {
                DefaultEmbeddingProvider = "openai",
                DefaultEmbeddingModel = "text-embedding-3-small"
            }),
            queryRewriter,
            relevanceGrader);
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmpty()
    {
        var service = CreateService();

        var results = await service.SearchAsync("");

        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task SearchAsync_WhitespaceQuery_ReturnsEmpty()
    {
        var service = CreateService();

        var results = await service.SearchAsync("   ");

        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task SearchAsync_EmbeddingFails_ReturnsEmpty()
    {
        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Failure("API error"));

        var service = CreateService();

        var results = await service.SearchAsync("test query");

        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task SearchAsync_NoResults_ReturnsEmpty()
    {
        var queryVector = new float[] { 0.1f, 0.2f, 0.3f };
        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success(queryVector));

        _vectorStoreMock
            .Setup(v => v.SearchAsync(queryVector, It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>());

        _rerankerMock
            .Setup(r => r.RerankAsync(It.IsAny<string>(), It.IsAny<List<VectorSearchResult>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>());

        var service = CreateService();

        var results = await service.SearchAsync("test query");

        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task SearchAsync_WithResults_MapsToTextSearchResults()
    {
        var docId = Guid.NewGuid();
        var kbId = Guid.NewGuid();
        var queryVector = new float[] { 0.1f, 0.2f, 0.3f };

        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success(queryVector));

        var vectorResults = new List<VectorSearchResult>
        {
            new() { Id = Guid.NewGuid(), Content = "Chunk content", DocumentId = docId, KnowledgeBaseId = kbId, ChunkIndex = 0, Score = 0.95 }
        };

        _vectorStoreMock
            .Setup(v => v.SearchAsync(queryVector, It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(vectorResults);

        _rerankerMock
            .Setup(r => r.RerankAsync(It.IsAny<string>(), It.IsAny<List<VectorSearchResult>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(vectorResults);

        // Mock 文档查询
        var docs = new List<KnowledgeDocument>
        {
            new() { Id = docId, FileName = "test.pdf", KnowledgeBaseId = kbId }
        };
        _docRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(docs.BuildMock());

        var service = CreateService();

        var results = (await service.SearchAsync("test query")).ToList();

        results.Count.ShouldBe(1);
        results[0].Text.ShouldBe("Chunk content");
        results[0].SourceName.ShouldBe("test.pdf");
        results[0].Score.ShouldBe(0.95);
        var metadata = results[0].Metadata;
        metadata.ShouldNotBeNull();
        metadata["documentId"].ShouldBe(docId);
        metadata["knowledgeBaseId"].ShouldBe(kbId);
    }

    [Fact]
    public async Task SearchAsync_CallsReranker()
    {
        var queryVector = new float[] { 0.1f, 0.2f };

        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success(queryVector));

        var vectorResults = new List<VectorSearchResult>
        {
            new() { Id = Guid.NewGuid(), Content = "Result A", Score = 0.9, DocumentId = Guid.NewGuid(), KnowledgeBaseId = Guid.NewGuid() },
            new() { Id = Guid.NewGuid(), Content = "Result B", Score = 0.8, DocumentId = Guid.NewGuid(), KnowledgeBaseId = Guid.NewGuid() }
        };

        _vectorStoreMock
            .Setup(v => v.SearchAsync(queryVector, It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(vectorResults);

        // Reranker 反转排序
        var reranked = new List<VectorSearchResult> { vectorResults[1], vectorResults[0] };
        _rerankerMock
            .Setup(r => r.RerankAsync("search query", vectorResults, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(reranked);

        var allDocIds = vectorResults.Select(r => r.DocumentId).Distinct().ToList();
        var docs = allDocIds.Select(id => new KnowledgeDocument { Id = id, FileName = $"doc-{id}.txt", KnowledgeBaseId = Guid.NewGuid() }).ToList();
        _docRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(docs.BuildMock());

        var service = CreateService();

        var results = (await service.SearchAsync("search query")).ToList();

        results.Count.ShouldBe(2);
        // 验证 reranker 被调用
        _rerankerMock.Verify(r => r.RerankAsync("search query", vectorResults, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_WithQueryRewriter_UsesRewrittenQuery()
    {
        var queryRewriterMock = new Mock<IQueryRewriter>();
        queryRewriterMock
            .Setup(q => q.RewriteAsync("user question", It.IsAny<CancellationToken>()))
            .ReturnsAsync("rewritten semantic query");

        var queryVector = new float[] { 0.1f };
        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingAsync("rewritten semantic query", It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success(queryVector));

        _vectorStoreMock
            .Setup(v => v.SearchAsync(queryVector, It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>());

        _rerankerMock
            .Setup(r => r.RerankAsync(It.IsAny<string>(), It.IsAny<List<VectorSearchResult>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>());

        var service = CreateService(queryRewriter: queryRewriterMock.Object);

        await service.SearchAsync("user question");

        // 验证嵌入使用改写后的查询
        _embeddingServiceMock.Verify(e => e.GenerateEmbeddingAsync("rewritten semantic query", It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_QueryRewriterReturnsEmpty_FallsBackToOriginal()
    {
        var queryRewriterMock = new Mock<IQueryRewriter>();
        queryRewriterMock
            .Setup(q => q.RewriteAsync("original query", It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        var queryVector = new float[] { 0.1f };
        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingAsync("original query", It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success(queryVector));

        _vectorStoreMock
            .Setup(v => v.SearchAsync(queryVector, It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>());

        _rerankerMock
            .Setup(r => r.RerankAsync(It.IsAny<string>(), It.IsAny<List<VectorSearchResult>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>());

        var service = CreateService(queryRewriter: queryRewriterMock.Object);

        await service.SearchAsync("original query");

        // 改写为空时应回退到原始查询
        _embeddingServiceMock.Verify(e => e.GenerateEmbeddingAsync("original query", It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_WithRelevanceGrader_FiltersIrrelevant()
    {
        var docId = Guid.NewGuid();
        var kbId = Guid.NewGuid();
        var queryVector = new float[] { 0.1f };

        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success(queryVector));

        var vectorResults = new List<VectorSearchResult>
        {
            new() { Id = Guid.NewGuid(), Content = "Relevant", DocumentId = docId, KnowledgeBaseId = kbId, Score = 0.9 },
            new() { Id = Guid.NewGuid(), Content = "Irrelevant", DocumentId = docId, KnowledgeBaseId = kbId, Score = 0.3 }
        };

        _vectorStoreMock
            .Setup(v => v.SearchAsync(queryVector, It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(vectorResults);

        _rerankerMock
            .Setup(r => r.RerankAsync(It.IsAny<string>(), It.IsAny<List<VectorSearchResult>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(vectorResults);

        var docs = new List<KnowledgeDocument> { new() { Id = docId, FileName = "doc.pdf", KnowledgeBaseId = kbId } };
        _docRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(docs.BuildMock());

        var relevanceGraderMock = new Mock<IRelevanceGrader>();
        relevanceGraderMock
            .Setup(g => g.GradeAsync(It.IsAny<string>(), It.IsAny<List<TextSearchResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, List<TextSearchResult> results, CancellationToken _) =>
                results.Select(r => new GradedResult
                {
                    Result = r,
                    IsRelevant = r.Text == "Relevant",
                    RelevanceScore = r.Text == "Relevant" ? 0.9 : 0.1
                }).ToList());

        var service = CreateService(relevanceGrader: relevanceGraderMock.Object);

        var results = (await service.SearchAsync("query")).ToList();

        // 只返回相关结果
        results.Count.ShouldBe(1);
        results[0].Text.ShouldBe("Relevant");
    }

    [Fact]
    public async Task SearchAsync_PostProcessorsRunInOrder()
    {
        var queryVector = new float[] { 0.1f };

        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success(queryVector));

        var vectorResults = new List<VectorSearchResult>
        {
            new() { Id = Guid.NewGuid(), Content = "Result", DocumentId = Guid.NewGuid(), KnowledgeBaseId = Guid.NewGuid(), Score = 0.9 }
        };

        _vectorStoreMock
            .Setup(v => v.SearchAsync(queryVector, It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(vectorResults);

        _rerankerMock
            .Setup(r => r.RerankAsync(It.IsAny<string>(), It.IsAny<List<VectorSearchResult>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(vectorResults);

        var docs = vectorResults.Select(r => new KnowledgeDocument { Id = r.DocumentId, FileName = "doc.txt", KnowledgeBaseId = r.KnowledgeBaseId }).ToList();
        _docRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(docs.BuildMock());

        // 追踪后处理器执行顺序
        var executionOrder = new List<int>();

        var processor1 = new Mock<ISearchPostProcessor>();
        processor1.Setup(p => p.Order).Returns(20);
        processor1.Setup(p => p.ProcessAsync(It.IsAny<List<VectorSearchResult>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => executionOrder.Add(20))
            .ReturnsAsync((List<VectorSearchResult> r, string _, CancellationToken _) => r);

        var processor2 = new Mock<ISearchPostProcessor>();
        processor2.Setup(p => p.Order).Returns(10);
        processor2.Setup(p => p.ProcessAsync(It.IsAny<List<VectorSearchResult>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => executionOrder.Add(10))
            .ReturnsAsync((List<VectorSearchResult> r, string _, CancellationToken _) => r);

        var service = CreateService(postProcessors: new[] { processor1.Object, processor2.Object });

        await service.SearchAsync("test");

        // Order=10 的处理器应先执行
        executionOrder.Count.ShouldBe(2);
        executionOrder[0].ShouldBe(10);
        executionOrder[1].ShouldBe(20);
    }

    [Fact]
    public async Task SearchAsync_ExceptionThrown_ReturnsEmptyGracefully()
    {
        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unexpected error"));

        var service = CreateService();

        // 不应抛出异常
        var results = await service.SearchAsync("test query");

        results.ShouldBeEmpty();
    }

    #region Per-KB embedding provider/model alignment (query 向量须与摄取空间一致)

    private void SetupPassthroughReranker()
    {
        _rerankerMock
            .Setup(r => r.RerankAsync(It.IsAny<string>(), It.IsAny<List<VectorSearchResult>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, List<VectorSearchResult> results, int topK, CancellationToken _) =>
                results.Take(topK).ToList());
    }

    [Fact]
    public async Task SearchAsync_NoKbScope_UsesDefaultEmbeddingProviderAndModel()
    {
        var queryVector = new float[] { 0.1f };
        EmbeddingOptions? capturedOptions = null;

        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, EmbeddingOptions?, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(Result<float[]>.Success(queryVector));

        _vectorStoreMock
            .Setup(v => v.SearchAsync(queryVector, It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>());
        SetupPassthroughReranker();

        var service = CreateService();

        await service.SearchAsync("query");

        // 跨库搜索必须使用 RAG 全局默认 embedding 配置，而非回退到 AI 顶层 DefaultProvider
        capturedOptions.ShouldNotBeNull();
        capturedOptions!.Provider.ShouldBe("openai");
        capturedOptions.Model.ShouldBe("text-embedding-3-small");
    }

    [Fact]
    public async Task SearchAsync_KbScoped_UsesPerKbEmbeddingProviderAndModel()
    {
        var kbId = Guid.NewGuid();
        var queryVector = new float[] { 0.1f };
        EmbeddingOptions? capturedOptions = null;

        var kbs = new List<KnowledgeBase>
        {
            new() { Id = kbId, Name = "KB", EmbeddingProvider = "custom-provider", EmbeddingModel = "custom-model" }
        };
        _kbRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(kbs.BuildMock());

        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, EmbeddingOptions?, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(Result<float[]>.Success(queryVector));

        _vectorStoreMock
            .Setup(v => v.SearchAsync(queryVector, It.IsAny<int>(), kbId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>());
        SetupPassthroughReranker();

        var service = CreateService();

        await service.SearchAsync("query", new TextSearchFilter { KnowledgeBaseIds = [kbId] });

        // query 向量必须与该 KB 摄取时的 provider/model 对齐
        capturedOptions.ShouldNotBeNull();
        capturedOptions!.Provider.ShouldBe("custom-provider");
        capturedOptions.Model.ShouldBe("custom-model");
        _vectorStoreMock.Verify(v => v.SearchAsync(queryVector, It.IsAny<int>(), kbId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_MultiKbWithDifferentProviders_GeneratesEmbeddingPerGroup()
    {
        var kbA = Guid.NewGuid();
        var kbB = Guid.NewGuid();
        var vectorA = new float[] { 0.1f };
        var vectorB = new float[] { 0.9f };

        var kbs = new List<KnowledgeBase>
        {
            new() { Id = kbA, Name = "A", EmbeddingProvider = "provider-a", EmbeddingModel = "model-a" },
            new() { Id = kbB, Name = "B", EmbeddingProvider = "provider-b", EmbeddingModel = "model-b" }
        };
        _kbRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(kbs.BuildMock());

        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.Is<EmbeddingOptions?>(o => o != null && o.Provider == "provider-a"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success(vectorA));
        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.Is<EmbeddingOptions?>(o => o != null && o.Provider == "provider-b"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success(vectorB));

        _vectorStoreMock
            .Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>());
        SetupPassthroughReranker();

        var service = CreateService();

        await service.SearchAsync("query", new TextSearchFilter { KnowledgeBaseIds = [kbA, kbB] });

        // 每个嵌入空间各生成一次 query 向量，且各 KB 用自己空间的向量检索
        _embeddingServiceMock.Verify(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _vectorStoreMock.Verify(v => v.SearchAsync(vectorA, It.IsAny<int>(), kbA, It.IsAny<CancellationToken>()), Times.Once);
        _vectorStoreMock.Verify(v => v.SearchAsync(vectorB, It.IsAny<int>(), kbB, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_MultiKbWithSameProvider_GeneratesSingleEmbedding()
    {
        var kbA = Guid.NewGuid();
        var kbB = Guid.NewGuid();
        var queryVector = new float[] { 0.5f };

        var kbs = new List<KnowledgeBase>
        {
            new() { Id = kbA, Name = "A", EmbeddingProvider = "shared", EmbeddingModel = "model" },
            new() { Id = kbB, Name = "B", EmbeddingProvider = "shared", EmbeddingModel = "model" }
        };
        _kbRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(kbs.BuildMock());

        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success(queryVector));

        _vectorStoreMock
            .Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>());
        SetupPassthroughReranker();

        var service = CreateService();

        await service.SearchAsync("query", new TextSearchFilter { KnowledgeBaseIds = [kbA, kbB] });

        // 同一嵌入空间只生成一次 query 向量
        _embeddingServiceMock.Verify(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        _vectorStoreMock.Verify(v => v.SearchAsync(queryVector, It.IsAny<int>(), kbA, It.IsAny<CancellationToken>()), Times.Once);
        _vectorStoreMock.Verify(v => v.SearchAsync(queryVector, It.IsAny<int>(), kbB, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_KbWithDefaultSentinelProvider_FallsBackToRagDefaults()
    {
        var kbId = Guid.NewGuid();
        var queryVector = new float[] { 0.1f };
        EmbeddingOptions? capturedOptions = null;

        var kbs = new List<KnowledgeBase>
        {
            new() { Id = kbId, Name = "KB", EmbeddingProvider = "default", EmbeddingModel = null }
        };
        _kbRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(kbs.BuildMock());

        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, EmbeddingOptions?, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(Result<float[]>.Success(queryVector));

        _vectorStoreMock
            .Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>());
        SetupPassthroughReranker();

        var service = CreateService();

        await service.SearchAsync("query", new TextSearchFilter { KnowledgeBaseIds = [kbId] });

        // "default" 哨兵值回退到 RAG 全局默认（与摄取路径一致）
        capturedOptions.ShouldNotBeNull();
        capturedOptions!.Provider.ShouldBe("openai");
        capturedOptions.Model.ShouldBe("text-embedding-3-small");
    }

    #endregion
}
