using Pgvector;
using Tnzi.AI.Rag.Dtos;
using Tnzi.AI.Rag.Metadata;

namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// IncrementalIndexService 单元测试 — 验证增量索引的段落级差异检测和 re-chunking 逻辑
/// </summary>
public class IncrementalIndexServiceTests
{
    private readonly Mock<IRepository<KnowledgeDocument, Guid>> _docRepoMock = new();
    private readonly Mock<IRepository<DocumentChunk, Guid>> _chunkRepoMock = new();
    private readonly Mock<IRepository<KnowledgeBase, Guid>> _kbRepoMock = new();
    private readonly Mock<IChunkingStrategy> _chunkingStrategyMock = new();
    private readonly Mock<IEmbeddingService> _embeddingServiceMock = new();

    private readonly Guid _docId = Guid.NewGuid();
    private readonly Guid _kbId = Guid.NewGuid();

    private IncrementalIndexService CreateService()
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

        return new IncrementalIndexService(
            _docRepoMock.Object,
            _chunkRepoMock.Object,
            _kbRepoMock.Object,
            _chunkingStrategyMock.Object,
            _embeddingServiceMock.Object,
            options,
            serviceProviderMock.Object);
    }

    private KnowledgeDocument CreateDocument(string? contentHash = "oldhash", int version = 1)
    {
        return new KnowledgeDocument
        {
            Id = _docId,
            KnowledgeBaseId = _kbId,
            FileName = "test.txt",
            ContentHash = contentHash,
            Version = version,
            Status = DocumentStatus.Completed,
            ChunkCount = 3
        };
    }

    private void SetupKnowledgeBase(int chunkSize = 512, int chunkOverlap = 64)
    {
        var kb = new KnowledgeBase
        {
            Id = _kbId,
            Name = "Test KB",
            ChunkSize = chunkSize,
            ChunkOverlap = chunkOverlap,
            EmbeddingProvider = "default",
            EmbeddingModel = null
        };
        _kbRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(new List<KnowledgeBase> { kb }.BuildMock());
    }

    private List<DocumentChunk> CreateExistingChunks(params string[] contents)
    {
        return contents.Select((c, i) => new DocumentChunk
        {
            Id = Guid.NewGuid(),
            KnowledgeBaseId = _kbId,
            DocumentId = _docId,
            Content = c,
            Embedding = new Vector(new float[] { 0.1f * i }),
            ChunkIndex = i
        }).ToList();
    }

    private void SetupEmbeddings(int count)
    {
        var embeddings = Enumerable.Range(0, count).Select(i => new float[] { 0.1f * i }).ToList();
        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<float[]>>.Success(embeddings));
    }

    [Fact]
    public async Task ReindexAsync_DocumentNotFound_ReturnsFailure()
    {
        _docRepoMock.Setup(r => r.GetAsync(_docId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((KnowledgeDocument?)null);

        var service = CreateService();
        var result = await service.ReindexAsync(_docId, "new content");

        result.Succeeded.ShouldBeFalse();
        var errorMessage = result.ErrorMessage;
        errorMessage.ShouldNotBeNull();
        errorMessage.ShouldContain("Document not found");
    }

    [Fact]
    public async Task ReindexAsync_KnowledgeBaseNotFound_ReturnsFailure()
    {
        _docRepoMock.Setup(r => r.GetAsync(_docId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDocument());
        _kbRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(new List<KnowledgeBase>().BuildMock());

        var service = CreateService();
        var result = await service.ReindexAsync(_docId, "new content");

        result.Succeeded.ShouldBeFalse();
        var errorMessage = result.ErrorMessage;
        errorMessage.ShouldNotBeNull();
        errorMessage.ShouldContain("Knowledge base not found");
    }

    [Fact]
    public async Task ReindexAsync_ContentUnchanged_ReturnsSuccessWithNoChanges()
    {
        // 使用与新内容相同的哈希
        var content = "Paragraph one\n\nParagraph two";
        var contentHash = ComputeHash(content);
        var doc = CreateDocument(contentHash: contentHash);

        _docRepoMock.Setup(r => r.GetAsync(_docId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);
        SetupKnowledgeBase();

        // 模拟已有 2 个块
        var existingChunks = CreateExistingChunks("Chunk 1", "Chunk 2");
        _chunkRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>()))
            .Returns(existingChunks.BuildMock());

        var service = CreateService();
        var result = await service.ReindexAsync(_docId, content);

        result.Succeeded.ShouldBeTrue();
        result.AddedChunks.ShouldBe(0);
        result.RemovedChunks.ShouldBe(0);
        result.UnchangedChunks.ShouldBe(2);
        result.FullReindexUsed.ShouldBeFalse();
    }

    [Fact]
    public async Task ReindexAsync_SmallChange_UsesIncrementalReindex()
    {
        // 原内容：3 段落中改 1 段 → 33% 变更 < 50% 阈值 → 增量
        var doc = CreateDocument();
        _docRepoMock.Setup(r => r.GetAsync(_docId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);
        SetupKnowledgeBase();

        // 旧块内容 = 3 段落（模拟存储的块就是段落）
        var existingChunks = CreateExistingChunks("Paragraph one", "Paragraph two", "Paragraph three");
        _chunkRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>()))
            .Returns(existingChunks.BuildMock());

        // 新内容：修改第二段
        var newContent = "Paragraph one\n\nParagraph two MODIFIED\n\nParagraph three";

        // 设置切块策略：变更部分重新切块
        _chunkingStrategyMock
            .Setup(c => c.Chunk(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(new List<string> { "Paragraph two MODIFIED" });

        SetupEmbeddings(1);

        var service = CreateService();
        var result = await service.ReindexAsync(_docId, newContent);

        result.Succeeded.ShouldBeTrue();
        result.FullReindexUsed.ShouldBeFalse();
        result.AddedChunks.ShouldBe(1);
        result.RemovedChunks.ShouldBe(1); // "Paragraph two" 被删除
        result.UnchangedChunks.ShouldBe(2); // "Paragraph one" 和 "Paragraph three" 保留
    }

    [Fact]
    public async Task ReindexAsync_LargeChange_FallsBackToFullReindex()
    {
        // 原内容 3 段落全部修改 → 100% 变更 > 50% 阈值 → 全量
        var doc = CreateDocument();
        _docRepoMock.Setup(r => r.GetAsync(_docId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);
        SetupKnowledgeBase();

        var existingChunks = CreateExistingChunks("Old para 1", "Old para 2", "Old para 3");
        _chunkRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>()))
            .Returns(existingChunks.BuildMock());

        var newContent = "New para A\n\nNew para B\n\nNew para C";

        _chunkingStrategyMock
            .Setup(c => c.Chunk(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(new List<string> { "New para A", "New para B", "New para C" });

        SetupEmbeddings(3);

        var service = CreateService();
        var result = await service.ReindexAsync(_docId, newContent);

        result.Succeeded.ShouldBeTrue();
        result.FullReindexUsed.ShouldBeTrue();
        result.AddedChunks.ShouldBe(3);
        result.RemovedChunks.ShouldBe(3);
        result.UnchangedChunks.ShouldBe(0);
    }

    [Fact]
    public async Task ReindexAsync_AddParagraphs_IncrementallyAddsChunks()
    {
        // 原 2 段落 + 新增 1 段落 → 1/3 变更 < 50% → 增量
        var doc = CreateDocument();
        _docRepoMock.Setup(r => r.GetAsync(_docId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);
        SetupKnowledgeBase();

        var existingChunks = CreateExistingChunks("Paragraph A", "Paragraph B");
        _chunkRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>()))
            .Returns(existingChunks.BuildMock());

        var newContent = "Paragraph A\n\nParagraph B\n\nParagraph C added";

        _chunkingStrategyMock
            .Setup(c => c.Chunk(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(new List<string> { "Paragraph C added" });

        SetupEmbeddings(1);

        var service = CreateService();
        var result = await service.ReindexAsync(_docId, newContent);

        result.Succeeded.ShouldBeTrue();
        result.FullReindexUsed.ShouldBeFalse();
        result.AddedChunks.ShouldBe(1);
        result.RemovedChunks.ShouldBe(0);
        result.UnchangedChunks.ShouldBe(2);
    }

    [Fact]
    public async Task ReindexAsync_RemoveParagraphs_DeletesChunks()
    {
        // 原 3 段落，删除 1 段 → 1/3 变更 < 50% → 增量
        var doc = CreateDocument();
        _docRepoMock.Setup(r => r.GetAsync(_docId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);
        SetupKnowledgeBase();

        var existingChunks = CreateExistingChunks("Keep A", "Remove B", "Keep C");
        _chunkRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>()))
            .Returns(existingChunks.BuildMock());

        // 新内容只有 2 段，去掉 "Remove B"
        var newContent = "Keep A\n\nKeep C";

        var service = CreateService();
        var result = await service.ReindexAsync(_docId, newContent);

        result.Succeeded.ShouldBeTrue();
        result.FullReindexUsed.ShouldBeFalse();
        result.RemovedChunks.ShouldBe(1);
        result.UnchangedChunks.ShouldBe(2);

        // 验证删除操作
        _chunkRepoMock.Verify(r => r.DeleteManyAsync(
            It.Is<List<DocumentChunk>>(list => list.Count == 1 && list[0].Content == "Remove B"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReindexAsync_UpdatesDocumentMetadata()
    {
        var doc = CreateDocument(version: 2);
        _docRepoMock.Setup(r => r.GetAsync(_docId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);
        SetupKnowledgeBase();

        var existingChunks = CreateExistingChunks("Old content");
        _chunkRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>()))
            .Returns(existingChunks.BuildMock());

        var newContent = "Completely new content here\n\nAnother new paragraph";

        _chunkingStrategyMock
            .Setup(c => c.Chunk(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(new List<string> { "Completely new content here", "Another new paragraph" });

        SetupEmbeddings(2);

        var service = CreateService();
        var result = await service.ReindexAsync(_docId, newContent);

        result.Succeeded.ShouldBeTrue();
        result.NewContentHash.ShouldNotBeNullOrEmpty();

        // 验证文档元数据更新（版本递增）
        _docRepoMock.Verify(r => r.UpdateAsync(
            _docId,
            It.IsAny<Action<KnowledgeDocument>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReindexAsync_EmbeddingFailure_ReturnsFailure()
    {
        var doc = CreateDocument();
        _docRepoMock.Setup(r => r.GetAsync(_docId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);
        SetupKnowledgeBase();

        // 全部段落变更 → 全量重建
        var existingChunks = CreateExistingChunks("Old");
        _chunkRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>()))
            .Returns(existingChunks.BuildMock());

        var newContent = "Entirely different content";

        _chunkingStrategyMock
            .Setup(c => c.Chunk(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(new List<string> { "Entirely different content" });

        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<float[]>>.Failure("API error"));

        var service = CreateService();
        var result = await service.ReindexAsync(_docId, newContent);

        result.Succeeded.ShouldBeFalse();
        var errorMessage = result.ErrorMessage;
        errorMessage.ShouldNotBeNull();
        errorMessage.ShouldContain("Embedding generation failed");
    }

    [Fact]
    public async Task ReindexAsync_ContentHashUpdated()
    {
        var doc = CreateDocument();
        _docRepoMock.Setup(r => r.GetAsync(_docId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);
        SetupKnowledgeBase();

        var existingChunks = CreateExistingChunks("Para A");
        _chunkRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>()))
            .Returns(existingChunks.BuildMock());

        var newContent = "Para A\n\nPara B";

        _chunkingStrategyMock
            .Setup(c => c.Chunk(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(new List<string> { "Para B" });

        SetupEmbeddings(1);

        var service = CreateService();
        var result = await service.ReindexAsync(_docId, newContent);

        result.Succeeded.ShouldBeTrue();
        result.NewContentHash.ShouldNotBe("oldhash");
        result.NewContentHash.Length.ShouldBe(64); // SHA256 hex length
    }

    /// <summary>
    /// 辅助方法：计算 SHA256 哈希（与 IncrementalIndexService 保持一致）
    /// </summary>
    private static string ComputeHash(string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var hashBytes = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLower(System.Globalization.CultureInfo.InvariantCulture);
    }
}
