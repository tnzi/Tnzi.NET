using Microsoft.Extensions.Logging.Abstractions;
using Pgvector;

namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// DocumentIngestionService 单元测试 — 验证文档摄取管道各阶段
/// </summary>
public class DocumentIngestionServiceTests
{
    private readonly Mock<IFileExtractorService> _extractorMock = new();
    private readonly Mock<IChunkingStrategy> _chunkingStrategyMock = new();
    private readonly Mock<IEmbeddingService> _embeddingServiceMock = new();
    private readonly Mock<IRepository<DocumentChunk, Guid>> _chunkRepoMock = new();
    private readonly Mock<IRepository<KnowledgeBase, Guid>> _kbRepoMock = new();

    private DocumentIngestionService CreateService(IAsyncChunkingStrategy? asyncStrategy = null)
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
            DefaultEmbeddingModel = "text-embedding-3-small"
        });

        return new DocumentIngestionService(
            new[] { _extractorMock.Object },
            _chunkingStrategyMock.Object,
            _embeddingServiceMock.Object,
            _chunkRepoMock.Object,
            _kbRepoMock.Object,
            options,
            serviceProviderMock.Object,
            asyncStrategy);
    }

    private void SetupKnowledgeBase(Guid kbId, int chunkSize = 512, int chunkOverlap = 64)
    {
        var kb = new KnowledgeBase
        {
            Id = kbId,
            Name = "Test KB",
            ChunkSize = chunkSize,
            ChunkOverlap = chunkOverlap,
            EmbeddingProvider = "default",
            EmbeddingModel = null
        };
        var kbs = new List<KnowledgeBase> { kb };
        _kbRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(kbs.BuildMock());
    }

    [Fact]
    public async Task IngestAsync_UnsupportedFileType_ReturnsFailure()
    {
        _extractorMock.Setup(e => e.Supports("unknown.xyz")).Returns(false);
        SetupKnowledgeBase(Guid.NewGuid());

        var service = CreateService();
        var result = await service.IngestAsync(Guid.NewGuid(), Guid.NewGuid(), Stream.Null, "unknown.xyz");

        result.Succeeded.ShouldBeFalse();
        var errorMessage = result.ErrorMessage;
        errorMessage.ShouldNotBeNull();
        errorMessage.ShouldContain("Unsupported file type");
    }

    [Fact]
    public async Task IngestAsync_KnowledgeBaseNotFound_ReturnsFailure()
    {
        _extractorMock.Setup(e => e.Supports("test.txt")).Returns(true);

        var emptyKbs = new List<KnowledgeBase>();
        _kbRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(emptyKbs.BuildMock());

        var service = CreateService();
        var result = await service.IngestAsync(Guid.NewGuid(), Guid.NewGuid(), Stream.Null, "test.txt");

        result.Succeeded.ShouldBeFalse();
        var errorMessage = result.ErrorMessage;
        errorMessage.ShouldNotBeNull();
        errorMessage.ShouldContain("Knowledge base not found");
    }

    [Fact]
    public async Task IngestAsync_EmptyExtraction_ReturnsFailure()
    {
        var kbId = Guid.NewGuid();
        _extractorMock.Setup(e => e.Supports("test.txt")).Returns(true);
        _extractorMock
            .Setup(e => e.ExtractTextAsync(It.IsAny<Stream>(), "test.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);
        SetupKnowledgeBase(kbId);

        var service = CreateService();
        var result = await service.IngestAsync(kbId, Guid.NewGuid(), Stream.Null, "test.txt");

        result.Succeeded.ShouldBeFalse();
        var errorMessage = result.ErrorMessage;
        errorMessage.ShouldNotBeNull();
        errorMessage.ShouldContain("No text content");
    }

    [Fact]
    public async Task IngestAsync_ChunkingProducesNoChunks_ReturnsFailure()
    {
        var kbId = Guid.NewGuid();
        _extractorMock.Setup(e => e.Supports("test.txt")).Returns(true);
        _extractorMock
            .Setup(e => e.ExtractTextAsync(It.IsAny<Stream>(), "test.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Some text content");
        SetupKnowledgeBase(kbId);

        _chunkingStrategyMock
            .Setup(c => c.Chunk(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(new List<string>());

        var service = CreateService();
        var result = await service.IngestAsync(kbId, Guid.NewGuid(), Stream.Null, "test.txt");

        result.Succeeded.ShouldBeFalse();
        var errorMessage = result.ErrorMessage;
        errorMessage.ShouldNotBeNull();
        errorMessage.ShouldContain("no chunks");
    }

    [Fact]
    public async Task IngestAsync_EmbeddingFails_ReturnsFailure()
    {
        var kbId = Guid.NewGuid();
        _extractorMock.Setup(e => e.Supports("test.txt")).Returns(true);
        _extractorMock
            .Setup(e => e.ExtractTextAsync(It.IsAny<Stream>(), "test.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Some text content for chunking.");
        SetupKnowledgeBase(kbId);

        _chunkingStrategyMock
            .Setup(c => c.Chunk(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(new List<string> { "Chunk 1", "Chunk 2" });

        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<float[]>>.Failure("Embedding API error"));

        var service = CreateService();
        var result = await service.IngestAsync(kbId, Guid.NewGuid(), Stream.Null, "test.txt");

        result.Succeeded.ShouldBeFalse();
        var errorMessage = result.ErrorMessage;
        errorMessage.ShouldNotBeNull();
        errorMessage.ShouldContain("Embedding generation failed");
    }

    [Fact]
    public async Task IngestAsync_SuccessfulPipeline_InsertsChunksAndReturnsSuccess()
    {
        var kbId = Guid.NewGuid();
        var docId = Guid.NewGuid();

        _extractorMock.Setup(e => e.Supports("test.txt")).Returns(true);
        _extractorMock
            .Setup(e => e.ExtractTextAsync(It.IsAny<Stream>(), "test.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Document text content for processing.");
        SetupKnowledgeBase(kbId);

        var chunks = new List<string> { "Chunk one", "Chunk two", "Chunk three" };
        _chunkingStrategyMock
            .Setup(c => c.Chunk(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(chunks);

        var embeddings = new List<float[]>
        {
            new float[] { 0.1f, 0.2f },
            new float[] { 0.3f, 0.4f },
            new float[] { 0.5f, 0.6f }
        };
        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<float[]>>.Success(embeddings));

        var service = CreateService();
        var result = await service.IngestAsync(kbId, docId, Stream.Null, "test.txt");

        result.Succeeded.ShouldBeTrue();
        result.ChunkCount.ShouldBe(3);

        // 验证批量插入
        _chunkRepoMock.Verify(r => r.InsertManyAsync(
            It.Is<List<DocumentChunk>>(list =>
                list.Count == 3 &&
                list[0].KnowledgeBaseId == kbId &&
                list[0].DocumentId == docId &&
                list[0].Content == "Chunk one" &&
                list[0].ChunkIndex == 0 &&
                list[1].ChunkIndex == 1 &&
                list[2].ChunkIndex == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IngestAsync_UsesKnowledgeBaseChunkSettings()
    {
        var kbId = Guid.NewGuid();

        _extractorMock.Setup(e => e.Supports("test.txt")).Returns(true);
        _extractorMock
            .Setup(e => e.ExtractTextAsync(It.IsAny<Stream>(), "test.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Document text for chunking.");
        SetupKnowledgeBase(kbId, chunkSize: 256, chunkOverlap: 32);

        _chunkingStrategyMock
            .Setup(c => c.Chunk(It.IsAny<string>(), 256, 32))
            .Returns(new List<string> { "Chunk" });

        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<float[]>>.Success(new List<float[]> { new float[] { 0.1f } }));

        var service = CreateService();
        await service.IngestAsync(kbId, Guid.NewGuid(), Stream.Null, "test.txt");

        // 验证使用知识库的 ChunkSize 和 ChunkOverlap
        _chunkingStrategyMock.Verify(c => c.Chunk(It.IsAny<string>(), 256, 32), Times.Once);
    }

    [Fact]
    public async Task IngestAsync_PreferAsyncChunkingStrategy()
    {
        var kbId = Guid.NewGuid();

        _extractorMock.Setup(e => e.Supports("test.txt")).Returns(true);
        _extractorMock
            .Setup(e => e.ExtractTextAsync(It.IsAny<Stream>(), "test.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Text for async chunking.");
        SetupKnowledgeBase(kbId);

        var asyncStrategyMock = new Mock<IAsyncChunkingStrategy>();
        asyncStrategyMock
            .Setup(a => a.ChunkAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "Async chunk" });

        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<float[]>>.Success(new List<float[]> { new float[] { 0.1f } }));

        var service = CreateService(asyncStrategy: asyncStrategyMock.Object);
        var result = await service.IngestAsync(kbId, Guid.NewGuid(), Stream.Null, "test.txt");

        result.Succeeded.ShouldBeTrue();
        // 异步策略被调用，同步策略不被调用
        asyncStrategyMock.Verify(a => a.ChunkAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        _chunkingStrategyMock.Verify(c => c.Chunk(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task IngestAsync_InternalException_ReturnsFailure()
    {
        var kbId = Guid.NewGuid();

        _extractorMock.Setup(e => e.Supports("test.txt")).Returns(true);
        _extractorMock
            .Setup(e => e.ExtractTextAsync(It.IsAny<Stream>(), "test.txt", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("File read error"));
        SetupKnowledgeBase(kbId);

        var service = CreateService();
        var result = await service.IngestAsync(kbId, Guid.NewGuid(), Stream.Null, "test.txt");

        result.Succeeded.ShouldBeFalse();
        var errorMessage = result.ErrorMessage;
        errorMessage.ShouldNotBeNull();
        errorMessage.ShouldContain("internal error");
    }

    [Fact]
    public async Task IngestAsync_MultipleBatches_ProcessesAllChunks()
    {
        var kbId = Guid.NewGuid();

        _extractorMock.Setup(e => e.Supports("test.txt")).Returns(true);
        _extractorMock
            .Setup(e => e.ExtractTextAsync(It.IsAny<Stream>(), "test.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Large document text content.");

        // 知识库配置
        SetupKnowledgeBase(kbId);

        // 生成 5 个 chunks，batch size 在 options 中设为 100，所以 5 个 chunks 属于同一批次
        var chunks = Enumerable.Range(0, 5).Select(i => $"Chunk {i}").ToList();
        _chunkingStrategyMock
            .Setup(c => c.Chunk(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(chunks);

        var embeddings = Enumerable.Range(0, 5).Select(i => new float[] { i * 0.1f }).ToList();
        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<float[]>>.Success(embeddings));

        var service = CreateService();
        var result = await service.IngestAsync(kbId, Guid.NewGuid(), Stream.Null, "test.txt");

        result.Succeeded.ShouldBeTrue();
        result.ChunkCount.ShouldBe(5);
    }
}
