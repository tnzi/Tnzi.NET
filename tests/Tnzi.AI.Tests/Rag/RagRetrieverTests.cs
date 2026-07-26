using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.AI.Rag.Dtos;

namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// RagRetriever 单元测试 - 重点验证 query 向量与目标知识库嵌入配置（provider/model）的对齐：
/// KB 范围检索按 per-KB 配置分组生成 query 向量；search-all 使用 RAG 全局默认配置。
/// </summary>
public class RagRetrieverTests
{
    private readonly Mock<IEmbeddingService> _embeddingServiceMock = new();
    private readonly Mock<IVectorStore> _vectorStoreMock = new();
    private readonly Mock<IReranker> _rerankerMock = new();
    private readonly Mock<IRepository<KnowledgeDocument, Guid>> _docRepoMock = new();
    private readonly Mock<IRepository<KnowledgeBase, Guid>> _kbRepoMock = new();

    public RagRetrieverTests()
    {
        // Reranker 直通
        _rerankerMock
            .Setup(r => r.RerankAsync(It.IsAny<string>(), It.IsAny<List<VectorSearchResult>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, List<VectorSearchResult> results, int topK, CancellationToken _) =>
                results.Take(topK).ToList());
    }

    private RagRetriever CreateRetriever()
    {
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(ILoggerFactory)))
            .Returns(NullLoggerFactory.Instance);

        var ragOptions = new StaticOptionsMonitor<AIRagOptions>(new AIRagOptions
        {
            DefaultEmbeddingProvider = "openai",
            DefaultEmbeddingModel = "text-embedding-3-small"
        });

        return new RagRetriever(
            serviceProviderMock.Object,
            _embeddingServiceMock.Object,
            _vectorStoreMock.Object,
            _rerankerMock.Object,
            _docRepoMock.Object,
            _kbRepoMock.Object,
            Enumerable.Empty<ISearchPostProcessor>(),
            ragOptions);
    }

    [Fact]
    public async Task RetrieveAsync_NoKbScope_UsesDefaultEmbeddingOptions()
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

        var retriever = CreateRetriever();

        await retriever.RetrieveAsync("query");

        // 跨库检索必须显式使用 RAG 全局默认 embedding provider/model
        capturedOptions.ShouldNotBeNull();
        capturedOptions!.Provider.ShouldBe("openai");
        capturedOptions.Model.ShouldBe("text-embedding-3-small");
    }

    [Fact]
    public async Task RetrieveAsync_KbScoped_UsesPerKbEmbeddingOptions()
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

        var retriever = CreateRetriever();

        await retriever.RetrieveAsync("query", new RagRetrievalOptions { KnowledgeBaseIds = [kbId] });

        // query 向量与该 KB 摄取时的 provider/model 对齐
        capturedOptions.ShouldNotBeNull();
        capturedOptions!.Provider.ShouldBe("custom-provider");
        capturedOptions.Model.ShouldBe("custom-model");
        _vectorStoreMock.Verify(v => v.SearchAsync(queryVector, It.IsAny<int>(), kbId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RetrieveAsync_MultiKbDifferentProviders_GeneratesEmbeddingPerGroup()
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

        var retriever = CreateRetriever();

        await retriever.RetrieveAsync("query", new RagRetrievalOptions { KnowledgeBaseIds = [kbA, kbB] });

        // provider/model 不一致 → 按配置分组逐组生成 query 向量，各 KB 用自己空间的向量检索
        _embeddingServiceMock.Verify(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _vectorStoreMock.Verify(v => v.SearchAsync(vectorA, It.IsAny<int>(), kbA, It.IsAny<CancellationToken>()), Times.Once);
        _vectorStoreMock.Verify(v => v.SearchAsync(vectorB, It.IsAny<int>(), kbB, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RetrieveAsync_MultiKbSameProvider_GeneratesSingleEmbedding()
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

        var retriever = CreateRetriever();

        await retriever.RetrieveAsync("query", new RagRetrievalOptions { KnowledgeBaseIds = [kbA, kbB] });

        // 同一嵌入空间只生成一次 query 向量
        _embeddingServiceMock.Verify(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RetrieveAsync_ReturnsResults_FromKbScopedSearch()
    {
        var kbId = Guid.NewGuid();
        var queryVector = new float[] { 0.1f };

        var kbs = new List<KnowledgeBase>
        {
            new() { Id = kbId, Name = "KB", EmbeddingProvider = "default", EmbeddingModel = null }
        };
        _kbRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(kbs.BuildMock());

        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success(queryVector));

        _vectorStoreMock
            .Setup(v => v.SearchAsync(queryVector, It.IsAny<int>(), kbId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>
            {
                new() { Id = Guid.NewGuid(), Content = "Relevant chunk", KnowledgeBaseId = kbId, DocumentId = Guid.NewGuid(), Score = 0.9 }
            });

        var retriever = CreateRetriever();

        var results = await retriever.RetrieveAsync("query", new RagRetrievalOptions { KnowledgeBaseIds = [kbId] });

        results.Count.ShouldBe(1);
        results[0].Content.ShouldBe("Relevant chunk");
        results[0].KnowledgeBaseId.ShouldBe(kbId);
    }

    [Fact]
    public async Task RetrieveAsync_EmbeddingFailure_ReturnsEmptyGracefully()
    {
        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Failure("backend offline"));

        var retriever = CreateRetriever();

        var results = await retriever.RetrieveAsync("query");

        results.ShouldBeEmpty();
    }
}
