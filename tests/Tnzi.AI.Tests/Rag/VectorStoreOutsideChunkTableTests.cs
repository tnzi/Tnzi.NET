using Pgvector;

namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// 不读 RAG 分块表的向量存储（<see cref="IVectorStore.UsesRagChunkTable"/> 为 false）与分块表保持一致：
/// 摄取逐块写入、重新摄取替换、重建索引回写、删除时两边都删；读分块表的存储（PgVectorStore）一样都不多做。
/// </summary>
/// <remarks>
/// ★ 此前摄取只经 EF 写分块表、从不调用 <see cref="IVectorStore.UpsertAsync"/>：<c>VectorStoreProvider=InMemory</c> 与自定义向量库下
/// 文档显示「已完成 / N 块」，检索恒为空；删除文档 / 知识库只删向量存储（本来就空），分块表的行永不删除。
/// 集成测试此前替生产代码补上了这一步（拦截插入、手工同步进内存向量库），所以一直是绿的。
/// </remarks>
public class VectorStoreOutsideChunkTableTests
{
    private readonly Guid _kbId = Guid.NewGuid();
    private readonly Guid _docId = Guid.NewGuid();
    private readonly Mock<IRepository<DocumentChunk, Guid>> _chunkRepo = new();
    private readonly Mock<IRepository<KnowledgeBase, Guid>> _kbRepo = new();
    private readonly Mock<IRepository<KnowledgeDocument, Guid>> _docRepo = new();
    private readonly Mock<IEmbeddingService> _embedding = new();
    private readonly List<string> _chunkCalls = [];
    private readonly List<DocumentChunk> _inserted = [];
    private List<string> _chunkTexts = ["first chunk", "second chunk", "third chunk"];

    public VectorStoreOutsideChunkTableTests()
    {
        var kb = new KnowledgeBase { Id = _kbId, Name = "kb" };
        _kbRepo.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(new List<KnowledgeBase> { kb }.BuildMock());
        _kbRepo.Setup(r => r.GetAsync(_kbId, It.IsAny<CancellationToken>())).ReturnsAsync(kb);
        _docRepo.Setup(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _embedding
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string> texts, EmbeddingOptions? _, CancellationToken _) =>
                Result<List<float[]>>.Success(texts.Select((_, i) => new[] { 1f, i }).ToList()));
        _embedding
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success(new[] { 1f, 0f }));
        _chunkRepo
            .Setup(r => r.DeleteAsync(It.IsAny<Expression<Func<DocumentChunk, bool>>>(), It.IsAny<CancellationToken>()))
            .Callback(() => _chunkCalls.Add("delete"))
            .Returns(Task.CompletedTask);
        _chunkRepo
            .Setup(r => r.InsertManyAsync(It.IsAny<IEnumerable<DocumentChunk>>(), It.IsAny<CancellationToken>()))
            .Callback((IEnumerable<DocumentChunk> chunks, CancellationToken _) =>
            {
                _chunkCalls.Add("insert");
                foreach (var chunk in chunks)
                {
                    chunk.Id = Guid.NewGuid();
                    _inserted.Add(chunk);
                }
            })
            .Returns(Task.CompletedTask);
    }

    // =====================================================================
    // 摄取
    // =====================================================================

    [Fact]
    public async Task Ingest_WritesEveryChunkToTheStore_UnderTheChunkTableId()
    {
        var store = new InMemoryVectorStore(NullLogger<InMemoryVectorStore>.Instance);

        (await CreateIngestion(store).IngestAsync(_kbId, _docId, Content(), "doc.txt")).Succeeded.ShouldBeTrue();

        var stored = await store.SearchAsync([1f, 0f], topK: 10, _kbId);
        stored.Select(r => r.Id).ShouldBe(_inserted.Select(c => c.Id), ignoreOrder: true);
        stored.Select(r => r.ChunkIndex).ShouldBe([0, 1, 2], ignoreOrder: true);
    }

    [Fact]
    public async Task Reingest_ReplacesTheDocumentsEntries_InsteadOfStackingThem()
    {
        var store = new InMemoryVectorStore(NullLogger<InMemoryVectorStore>.Instance);
        var ingestion = CreateIngestion(store);

        (await ingestion.IngestAsync(_kbId, _docId, Content(), "doc.txt")).Succeeded.ShouldBeTrue();
        _chunkTexts = ["only chunk"];
        (await ingestion.IngestAsync(_kbId, _docId, Content(), "doc.txt")).Succeeded.ShouldBeTrue();

        (await store.SearchAsync([1f, 0f], topK: 10, _kbId)).ShouldHaveSingleItem().Content.ShouldBe("only chunk");
    }

    [Fact]
    public async Task Ingest_IntoTheChunkTableStore_DoesNotWriteItTwice()
    {
        var store = new Mock<IVectorStore>();
        store.Setup(v => v.UsesRagChunkTable).Returns(true);

        (await CreateIngestion(store.Object).IngestAsync(_kbId, _docId, Content(), "doc.txt")).Succeeded.ShouldBeTrue();

        store.Verify(v => v.UpsertAsync(It.IsAny<Guid>(), It.IsAny<float[]>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(v => v.DeleteByDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _chunkCalls.ShouldBe(["delete", "insert"]);
    }

    [Fact]
    public async Task Ingest_IntoAStoreThatCannotUpsert_FailsAndTakesBackTheChunkRows()
    {
        // 一个没有实现 UpsertAsync 的外部向量库：失败要说出来，不能留下分块表行让关键词检索命中一份失败文档
        var store = new Mock<IVectorStore>();
        store.Setup(v => v.UpsertAsync(It.IsAny<Guid>(), It.IsAny<float[]>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotSupportedException("This vector store does not support direct upsert."));

        var result = await CreateIngestion(store.Object).IngestAsync(_kbId, _docId, Content(), "doc.txt");

        result.Succeeded.ShouldBeFalse();
        _chunkCalls.ShouldBe(["delete", "insert", "delete"]);
        store.Verify(v => v.DeleteByDocumentAsync(_docId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // =====================================================================
    // 删除与重建索引
    // =====================================================================

    [Fact]
    public async Task DeleteKnowledgeBase_WithAStoreOutsideTheChunkTable_AlsoDeletesTheChunkRows()
    {
        var store = new Mock<IVectorStore>();
        UseEmptyKnowledgeBaseContents();

        (await CreateKnowledgeBaseService(store.Object).DeleteAsync(_kbId)).Succeeded.ShouldBeTrue();

        store.Verify(v => v.DeleteByKnowledgeBaseAsync(_kbId, It.IsAny<CancellationToken>()), Times.Once);
        _chunkCalls.ShouldBe(["delete"]);
    }

    [Fact]
    public async Task DeleteKnowledgeBase_WithTheChunkTableStore_LeavesTheRowsToIt()
    {
        var store = new Mock<IVectorStore>();
        store.Setup(v => v.UsesRagChunkTable).Returns(true);
        UseEmptyKnowledgeBaseContents();

        (await CreateKnowledgeBaseService(store.Object).DeleteAsync(_kbId)).Succeeded.ShouldBeTrue();

        store.Verify(v => v.DeleteByKnowledgeBaseAsync(_kbId, It.IsAny<CancellationToken>()), Times.Once);
        _chunkCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reindex_WithAStoreOutsideTheChunkTable_WritesTheNewVectorsToTheStore()
    {
        var store = new InMemoryVectorStore(NullLogger<InMemoryVectorStore>.Instance);
        var chunk = new DocumentChunk
        {
            Id = Guid.NewGuid(), KnowledgeBaseId = _kbId, DocumentId = _docId, ChunkIndex = 0, Content = "stale",
            Embedding = new Vector(new[] { 0f, 1f })
        };
        await store.UpsertAsync(chunk.Id, [0f, 1f], _docId, _kbId, chunk.Content, 0);
        _chunkRepo.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(new List<DocumentChunk> { chunk }.BuildMock());

        (await CreateKnowledgeBaseService(store).ReindexAsync(_kbId)).Succeeded.ShouldBeTrue();

        // 新向量是 [1, 0]：与 [1, 0] 的查询完全同向
        (await store.SearchAsync([1f, 0f], topK: 1, _kbId)).ShouldHaveSingleItem().Score.ShouldBe(1.0, tolerance: 1e-6);
    }

    private void UseEmptyKnowledgeBaseContents()
    {
        _docRepo.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(new List<KnowledgeDocument>().BuildMock());
    }

    private MemoryStream Content() => new([1, 2, 3]);

    private DocumentIngestionService CreateIngestion(IVectorStore store)
    {
        var extractor = new Mock<IFileExtractorService>();
        extractor.Setup(e => e.Supports(It.IsAny<string>())).Returns(true);
        extractor.Setup(e => e.ExtractTextAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("document text");
        var chunking = new Mock<IChunkingStrategy>();
        chunking.Setup(c => c.Chunk(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>())).Returns(() => _chunkTexts.ToList());

        return new DocumentIngestionService(
            [extractor.Object],
            chunking.Object,
            _embedding.Object,
            _chunkRepo.Object,
            store,
            _kbRepo.Object,
            _docRepo.Object,
            Mock.Of<IRepository<KnowledgeGraphNode, Guid>>(),
            Mock.Of<IRepository<KnowledgeGraphEdge, Guid>>(),
            Mock.Of<IGraphExtractor>(),
            Options(),
            LoggingServiceProvider());
    }

    private KnowledgeBaseService CreateKnowledgeBaseService(IVectorStore store)
    {
        var graphNodes = new Mock<IRepository<KnowledgeGraphNode, Guid>>();
        graphNodes.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(new List<KnowledgeGraphNode>().BuildMock());
        var graphEdges = new Mock<IRepository<KnowledgeGraphEdge, Guid>>();
        graphEdges.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(new List<KnowledgeGraphEdge>().BuildMock());

        return new KnowledgeBaseService(
            _kbRepo.Object,
            _docRepo.Object,
            _chunkRepo.Object,
            graphNodes.Object,
            graphEdges.Object,
            Mock.Of<IDocumentIngestionService>(),
            store,
            _embedding.Object,
            Mock.Of<IReranker>(),
            Options(),
            LoggingServiceProvider());
    }

    private static StaticOptionsMonitor<AIRagOptions> Options() => new(new AIRagOptions());

    private static IServiceProvider LoggingServiceProvider()
    {
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(sp => sp.GetService(typeof(ILoggerFactory))).Returns(NullLoggerFactory.Instance);
        return serviceProvider.Object;
    }
}
