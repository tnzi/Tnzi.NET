using Pgvector;
using Tnzi.AI.Rag.Dtos;

namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// KnowledgeBaseService.ReindexAsync 单元测试 — 验证完整重新向量化流程
/// </summary>
public class KnowledgeBaseServiceReindexTests
{
    private readonly Mock<IRepository<KnowledgeBase, Guid>> _kbRepoMock = new();
    private readonly Mock<IRepository<KnowledgeDocument, Guid>> _docRepoMock = new();
    private readonly Mock<IRepository<DocumentChunk, Guid>> _chunkRepoMock = new();
    private readonly Mock<IDocumentIngestionService> _ingestionServiceMock = new();
    private readonly Mock<IVectorStore> _vectorStoreMock = new();
    private readonly Mock<IEmbeddingService> _embeddingServiceMock = new();
    private readonly Mock<IReranker> _rerankerMock = new();

    private KnowledgeBaseService CreateService()
    {
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(ILoggerFactory)))
            .Returns(NullLoggerFactory.Instance);

        var options = Microsoft.Extensions.Options.Options.Create(new AIRagOptions
        {
            DefaultChunkSize = 512,
            DefaultChunkOverlap = 64,
            EmbeddingBatchSize = 100,
            DefaultEmbeddingProvider = "openai",
            DefaultEmbeddingModel = "text-embedding-3-small",
            MaxTopK = 50
        });

        return new KnowledgeBaseService(
            _kbRepoMock.Object,
            _docRepoMock.Object,
            _chunkRepoMock.Object,
            _ingestionServiceMock.Object,
            _vectorStoreMock.Object,
            _embeddingServiceMock.Object,
            _rerankerMock.Object,
            options,
            serviceProviderMock.Object,
            backgroundJobManager: null);
    }

    [Fact]
    public async Task ReindexAsync_KnowledgeBaseNotFound_Returns404()
    {
        var kbId = Guid.NewGuid();
        _kbRepoMock.Setup(r => r.GetAsync(kbId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((KnowledgeBase?)null);

        var service = CreateService();
        var result = await service.ReindexAsync(kbId);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
        result.Message.ShouldNotBeNull();
        result.Message!.ShouldContain("Knowledge base not found");
    }

    [Fact]
    public async Task ReindexAsync_NoChunks_ReturnsSuccessWithZeroCounts()
    {
        var kbId = Guid.NewGuid();
        var kb = new KnowledgeBase
        {
            Id = kbId,
            Name = "Empty KB",
            EmbeddingProvider = "default",
            EmbeddingModel = null
        };

        _kbRepoMock.Setup(r => r.GetAsync(kbId, It.IsAny<CancellationToken>())).ReturnsAsync(kb);
        _chunkRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>()))
            .Returns(new List<DocumentChunk>().BuildMock());

        var service = CreateService();
        var result = await service.ReindexAsync(kbId);

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.KnowledgeBaseId.ShouldBe(kbId);
        result.Data.ChunkCount.ShouldBe(0);
        result.Data.DocumentCount.ShouldBe(0);
        result.Data.DurationMs.ShouldBeGreaterThanOrEqualTo(0);

        _embeddingServiceMock.Verify(
            e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<EmbeddingOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReindexAsync_WithChunks_RegeneratesAllEmbeddingsAndPersists()
    {
        var kbId = Guid.NewGuid();
        var docId1 = Guid.NewGuid();
        var docId2 = Guid.NewGuid();

        var kb = new KnowledgeBase
        {
            Id = kbId,
            Name = "Test KB",
            EmbeddingProvider = "default",
            EmbeddingModel = null
        };

        var oldEmbedding = new Vector(new float[] { 0.0f, 0.0f, 0.0f });
        var chunks = new List<DocumentChunk>
        {
            new() { Id = Guid.NewGuid(), KnowledgeBaseId = kbId, DocumentId = docId1, ChunkIndex = 0, Content = "chunk-a", Embedding = oldEmbedding },
            new() { Id = Guid.NewGuid(), KnowledgeBaseId = kbId, DocumentId = docId1, ChunkIndex = 1, Content = "chunk-b", Embedding = oldEmbedding },
            new() { Id = Guid.NewGuid(), KnowledgeBaseId = kbId, DocumentId = docId2, ChunkIndex = 0, Content = "chunk-c", Embedding = oldEmbedding }
        };

        _kbRepoMock.Setup(r => r.GetAsync(kbId, It.IsAny<CancellationToken>())).ReturnsAsync(kb);
        _chunkRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(chunks.BuildMock());

        // Mock embedding service: return a deterministic vector per text
        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<EmbeddingOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string> texts, EmbeddingOptions _, CancellationToken _) =>
                Result<List<float[]>>.Success(texts.Select(t => new float[] { t.Length, 1.0f, 2.0f }).ToList()));

        List<DocumentChunk>? capturedUpdate = null;
        _chunkRepoMock
            .Setup(r => r.UpdateManyAsync(It.IsAny<IEnumerable<DocumentChunk>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<DocumentChunk>, CancellationToken>((batch, _) => capturedUpdate = batch.ToList())
            .Returns(Task.CompletedTask);

        var service = CreateService();
        var result = await service.ReindexAsync(kbId);

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.ChunkCount.ShouldBe(3);
        result.Data.DocumentCount.ShouldBe(2);
        result.Data.KnowledgeBaseId.ShouldBe(kbId);

        capturedUpdate.ShouldNotBeNull();
        capturedUpdate!.Count.ShouldBe(3);
        // Each chunk's embedding must be replaced (no longer the zero vector)
        foreach (var chunk in capturedUpdate)
        {
            chunk.Embedding.ShouldNotBe(oldEmbedding);
            chunk.Embedding.ToArray()[0].ShouldBe(chunk.Content.Length); // matches mock formula
        }

        _embeddingServiceMock.Verify(
            e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<EmbeddingOptions>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        _chunkRepoMock.Verify(
            r => r.UpdateManyAsync(It.IsAny<IEnumerable<DocumentChunk>>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ReindexAsync_EmbeddingFailure_ReturnsFailWithChunkId()
    {
        var kbId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();

        var kb = new KnowledgeBase
        {
            Id = kbId,
            Name = "Test KB",
            EmbeddingProvider = "default",
            EmbeddingModel = null
        };

        var chunks = new List<DocumentChunk>
        {
            new() { Id = chunkId, KnowledgeBaseId = kbId, DocumentId = docId, ChunkIndex = 0, Content = "x", Embedding = new Vector(new float[] { 0.0f }) }
        };

        _kbRepoMock.Setup(r => r.GetAsync(kbId, It.IsAny<CancellationToken>())).ReturnsAsync(kb);
        _chunkRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(chunks.BuildMock());

        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<EmbeddingOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<float[]>>.Failure("backend offline"));

        var service = CreateService();
        var result = await service.ReindexAsync(kbId);

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldNotBeNull();
        result.Message!.ShouldContain(chunkId.ToString());
        result.Message!.ShouldContain("backend offline");
    }
}
