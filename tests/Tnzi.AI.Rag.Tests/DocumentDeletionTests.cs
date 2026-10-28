using Tnzi.AI.Options;
using Tnzi.AI.Rag.Chunking;
using Tnzi.AI.Rag.Graph;
using Tnzi.AI.Rag.Options;
using Tnzi.AI.Rag.Tests.TestInfrastructure;
using Tnzi.Data;
using Tnzi.Domain.Entities;
using Tnzi.Results;

namespace Tnzi.AI.Rag.Tests;

/// <summary>
/// 删除文档把它在检索面上留下的一切带走：分块、图谱、计数，且与仍在进行的摄取并发时不留孤儿。
/// </summary>
/// <remarks>
/// <para>
/// ★ 此前三处缺口：①图谱节点 / 边只挂在知识库上，删除文档对图谱无效，被删文档的实体描述继续经图谱片段进入回答，
/// 重复摄取同一文档还每次叠加一份同名节点；②删除一份仍在 Processing 的文档，摄取随后写入的整套分块成为无主孤儿，
/// 照样被检索命中、却不出现在任何管理列表里；③删除一份 Failed / Processing 文档也把 <c>DocumentCount</c> 减 1。
/// </para>
/// <para>
/// 真实 SQLite + 真实仓储（条件删除 / 条件更新是修复的原语，替身仓储里不存在）；分块表带 <c>vector</c> 列映射不到 SQLite，
/// 经替身仓储与向量库断言。图谱抽取用真实的 <see cref="LlmGraphExtractor"/>，只替换模型调用。
/// </para>
/// </remarks>
public class DocumentDeletionTests : IntegratedTestBase<RagJobTestDbContext>
{
    private const string GraphJson = """
        {
          "entities": [
            { "name": "Acme Corp", "type": "Organization", "description": "Supplier, contract value 2M" },
            { "name": "Alice", "type": "Person", "description": "Account manager" }
          ],
          "relationships": [
            { "source": "Alice", "target": "Acme Corp", "relation": "works_for", "description": "Alice works for Acme", "weight": 0.9 }
          ]
        }
        """;

    private readonly Mock<IVectorStore> _vectorStore = new();
    private readonly Mock<IRepository<DocumentChunk, Guid>> _chunkRepository = new();
    private readonly Mock<IAiUtility> _aiUtility = new();
    private readonly Mock<IEmbeddingService> _embedding = new();
    private readonly List<string> _chunkCalls = [];

    public DocumentDeletionTests()
    {
        // 这些用例描述的是检索分块表本身的向量存储（PgVectorStore）；分块表之外的存储见文件末尾那组用例
        _vectorStore.Setup(v => v.UsesRagChunkTable).Returns(true);
        _aiUtility
            .Setup(u => u.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GraphJson);
        _embedding
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string> texts, EmbeddingOptions? _, CancellationToken _) =>
                Result<List<float[]>>.Success(texts.Select(_ => new[] { 0.1f }).ToList()));
        _chunkRepository
            .Setup(r => r.DeleteAsync(It.IsAny<System.Linq.Expressions.Expression<Func<DocumentChunk, bool>>>(), It.IsAny<CancellationToken>()))
            .Callback(() => _chunkCalls.Add("delete"))
            .Returns(Task.CompletedTask);
        _chunkRepository
            .Setup(r => r.InsertManyAsync(It.IsAny<IEnumerable<DocumentChunk>>(), It.IsAny<CancellationToken>()))
            .Callback(() => _chunkCalls.Add("insert"))
            .Returns(Task.CompletedTask);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        var entityManager = new Mock<IEntityManager>();
        entityManager.Setup(m => m.GetAllDbContextTypes()).Returns([typeof(RagJobTestDbContext)]);
        services.AddSingleton(_ => entityManager.Object);
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();
        services.AddScoped<IPostCommitActionQueue, PostCommitActionQueue>();

        services.AddScoped<IRepository<KnowledgeBase, Guid>, EFCoreRepository<RagJobTestDbContext, KnowledgeBase, Guid>>();
        services.AddScoped<IRepository<KnowledgeDocument, Guid>, EFCoreRepository<RagJobTestDbContext, KnowledgeDocument, Guid>>();
        services.AddScoped<IRepository<KnowledgeGraphNode, Guid>, EFCoreRepository<RagJobTestDbContext, KnowledgeGraphNode, Guid>>();
        services.AddScoped<IRepository<KnowledgeGraphEdge, Guid>, EFCoreRepository<RagJobTestDbContext, KnowledgeGraphEdge, Guid>>();
    }

    private IUnitOfWorkManager Manager => ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

    private IRepository<T, Guid> Repo<T>() where T : class, IEntity<Guid> => ServiceProvider.GetRequiredService<IRepository<T, Guid>>();

    private static StaticOptionsMonitor<AIRagOptions> Options() => new(new AIRagOptions
    {
        DefaultChunkSize = 512,
        DefaultChunkOverlap = 0,
        EmbeddingBatchSize = 100,
        GraphRag = new GraphRagOptions { Enabled = true }
    });

    private KnowledgeBaseService CreateKnowledgeBaseService(IDocumentIngestionService? ingestion = null)
        => new(
            Repo<KnowledgeBase>(),
            Repo<KnowledgeDocument>(),
            _chunkRepository.Object,
            Repo<KnowledgeGraphNode>(),
            Repo<KnowledgeGraphEdge>(),
            ingestion ?? Mock.Of<IDocumentIngestionService>(),
            _vectorStore.Object,
            _embedding.Object,
            Mock.Of<IReranker>(),
            Options(),
            ServiceProvider);

    private DocumentIngestionService CreateIngestionService()
    {
        var extractor = new Mock<IFileExtractorService>();
        extractor.Setup(e => e.Supports(It.IsAny<string>())).Returns(true);
        extractor.Setup(e => e.ExtractTextAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Alice works for Acme Corp. The contract value is 2M.");
        var chunking = new Mock<IChunkingStrategy>();
        chunking.Setup(c => c.Chunk(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>())).Returns(["chunk a", "chunk b"]);

        var graphExtractor = new LlmGraphExtractor(
            _aiUtility.Object, Repo<KnowledgeGraphNode>(), Repo<KnowledgeGraphEdge>(), NullLogger<LlmGraphExtractor>.Instance);

        return new DocumentIngestionService(
            [extractor.Object],
            chunking.Object,
            _embedding.Object,
            _chunkRepository.Object,
            _vectorStore.Object,
            Repo<KnowledgeBase>(),
            Repo<KnowledgeDocument>(),
            Repo<KnowledgeGraphNode>(),
            Repo<KnowledgeGraphEdge>(),
            graphExtractor,
            Options(),
            ServiceProvider);
    }

    private async Task<Guid> SeedKbAsync(int documentCount = 0, int chunkCount = 0)
    {
        var kb = new KnowledgeBase { Name = "KB", EmbeddingProvider = "default", DocumentCount = documentCount, ChunkCount = chunkCount };
        DbContext.Set<KnowledgeBase>().Add(kb);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return kb.Id;
    }

    private async Task<Guid> SeedDocAsync(Guid kbId, DocumentStatus status, int chunkCount = 0)
    {
        var doc = new KnowledgeDocument { KnowledgeBaseId = kbId, FileName = $"{status}.txt", Status = status, ChunkCount = chunkCount };
        DbContext.Set<KnowledgeDocument>().Add(doc);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return doc.Id;
    }

    private async Task<KnowledgeBase> ReadKbAsync(Guid kbId)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<KnowledgeBase>().AsNoTracking().SingleAsync(k => k.Id == kbId);
    }

    private Task<bool> DocExistsAsync(Guid docId)
        => DbContext.Set<KnowledgeDocument>().AsNoTracking().AnyAsync(d => d.Id == docId);

    private Task<int> GraphNodeCountAsync(Guid docId)
        => DbContext.Set<KnowledgeGraphNode>().AsNoTracking().CountAsync(n => n.DocumentId == docId);

    private Task<int> GraphEdgeCountAsync(Guid docId)
        => DbContext.Set<KnowledgeGraphEdge>().AsNoTracking().CountAsync(e => e.DocumentId == docId);

    private static MemoryStream Content() => new([1, 2, 3]);

    // =====================================================================
    // 计数：只扣被计过数的文档
    // =====================================================================

    [Fact]
    public async Task DeleteCompletedDocument_DecrementsTheKnowledgeBaseCounters()
    {
        var kbId = await SeedKbAsync(documentCount: 1, chunkCount: 5);
        var docId = await SeedDocAsync(kbId, DocumentStatus.Completed, chunkCount: 5);

        var result = await CreateKnowledgeBaseService().DeleteDocumentAsync(kbId, docId);

        result.Succeeded.ShouldBeTrue();
        var kb = await ReadKbAsync(kbId);
        kb.DocumentCount.ShouldBe(0);
        kb.ChunkCount.ShouldBe(0);
        (await DocExistsAsync(docId)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(DocumentStatus.Failed)]
    [InlineData(DocumentStatus.Processing)]
    public async Task DeleteDocumentThatWasNeverCounted_LeavesTheKnowledgeBaseCountersAlone(DocumentStatus status)
    {
        // 计数只在 Processing → Completed 时递增：另一篇已完成文档的 1/5 不该被这次删除扣掉
        var kbId = await SeedKbAsync(documentCount: 1, chunkCount: 5);
        var docId = await SeedDocAsync(kbId, status);

        var result = await CreateKnowledgeBaseService().DeleteDocumentAsync(kbId, docId);

        result.Succeeded.ShouldBeTrue();
        var kb = await ReadKbAsync(kbId);
        kb.DocumentCount.ShouldBe(1);
        kb.ChunkCount.ShouldBe(5);
        (await DocExistsAsync(docId)).ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteDocumentOfAnotherKnowledgeBase_IsNotFound()
    {
        var kbId = await SeedKbAsync();
        var otherKbId = await SeedKbAsync();
        var docId = await SeedDocAsync(otherKbId, DocumentStatus.Completed);

        var result = await CreateKnowledgeBaseService().DeleteDocumentAsync(kbId, docId);

        result.Code.ShouldBe(404);
        (await DocExistsAsync(docId)).ShouldBeTrue();
        _vectorStore.Verify(v => v.DeleteByDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // =====================================================================
    // 图谱：按来源文档归属，删除带走、重复摄取不叠加
    // =====================================================================

    [Fact]
    public async Task IngestingTheSameDocumentTwice_ReplacesItsGraph_InsteadOfStackingIt()
    {
        var kbId = await SeedKbAsync();
        var docId = await SeedDocAsync(kbId, DocumentStatus.Processing);
        var ingestion = CreateIngestionService();

        (await ingestion.IngestAsync(kbId, docId, Content(), "a.txt")).Succeeded.ShouldBeTrue();
        // Hangfire 重试 / 重新摄取同一文档
        (await ingestion.IngestAsync(kbId, docId, Content(), "a.txt")).Succeeded.ShouldBeTrue();

        (await GraphNodeCountAsync(docId)).ShouldBe(2);
        (await GraphEdgeCountAsync(docId)).ShouldBe(1);
    }

    [Fact]
    public async Task DeleteDocument_RemovesTheGraphExtractedFromIt_AndKeepsOtherDocumentsGraph()
    {
        var kbId = await SeedKbAsync();
        var deletedDoc = await SeedDocAsync(kbId, DocumentStatus.Processing);
        var keptDoc = await SeedDocAsync(kbId, DocumentStatus.Processing);
        var ingestion = CreateIngestionService();
        (await ingestion.IngestAsync(kbId, deletedDoc, Content(), "contract.txt")).Succeeded.ShouldBeTrue();
        (await ingestion.IngestAsync(kbId, keptDoc, Content(), "other.txt")).Succeeded.ShouldBeTrue();
        (await GraphNodeCountAsync(deletedDoc)).ShouldBe(2);

        var result = await CreateKnowledgeBaseService().DeleteDocumentAsync(kbId, deletedDoc);

        result.Succeeded.ShouldBeTrue();
        (await GraphNodeCountAsync(deletedDoc)).ShouldBe(0);
        (await GraphEdgeCountAsync(deletedDoc)).ShouldBe(0);
        (await GraphNodeCountAsync(keptDoc)).ShouldBe(2);
        (await GraphEdgeCountAsync(keptDoc)).ShouldBe(1);
    }

    [Fact]
    public async Task AfterTheOnlySourceIsDeleted_GraphSearch_NoLongerReturnsItsEntities()
    {
        var kbId = await SeedKbAsync();
        var docId = await SeedDocAsync(kbId, DocumentStatus.Processing);
        (await CreateIngestionService().IngestAsync(kbId, docId, Content(), "contract.txt")).Succeeded.ShouldBeTrue();
        var graphSearch = new GraphSearchService(Repo<KnowledgeGraphNode>(), Repo<KnowledgeGraphEdge>(), NullLogger<GraphSearchService>.Instance);
        (await graphSearch.SearchAsync("Acme Corp", kbId)).ShouldNotBeEmpty();

        await CreateKnowledgeBaseService().DeleteDocumentAsync(kbId, docId);

        (await graphSearch.SearchAsync("Acme Corp", kbId)).ShouldBeEmpty();
    }

    // =====================================================================
    // 与仍在进行的摄取并发：任何交错都不留孤儿
    // =====================================================================

    [Fact]
    public async Task DeleteDocument_RemovesTheRowBeforeItsChunksAndGraph()
    {
        // 顺序是约定的一半：清理发生时文档行已经不在，之后才写下的分块会被摄取自己看见并清掉
        var kbId = await SeedKbAsync();
        var docId = await SeedDocAsync(kbId, DocumentStatus.Processing);
        var rowPresentAtChunkCleanup = new List<bool>();
        _vectorStore
            .Setup(v => v.DeleteByDocumentAsync(docId, It.IsAny<CancellationToken>()))
            .Callback(() => rowPresentAtChunkCleanup.Add(DocExistsAsync(docId).GetAwaiter().GetResult()))
            .Returns(Task.CompletedTask);

        await CreateKnowledgeBaseService().DeleteDocumentAsync(kbId, docId);

        rowPresentAtChunkCleanup.ShouldBe([false]);
    }

    [Fact]
    public async Task DocumentDeletedWhileBeingIngested_LeavesNoChunksOrGraphBehind()
    {
        // 管理员在摄取的嵌入阶段（大文档可达数分钟）删除了文档：删除方清理时还什么都没写，
        // 之后写下的分块与图谱只能由摄取在末尾发现「文档已删」并自己清掉
        var kbId = await SeedKbAsync();
        var docId = await SeedDocAsync(kbId, DocumentStatus.Processing);
        var kbService = CreateKnowledgeBaseService();
        _embedding
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(async (List<string> texts, EmbeddingOptions? _, CancellationToken _) =>
            {
                (await kbService.DeleteDocumentAsync(kbId, docId)).Succeeded.ShouldBeTrue();
                return Result<List<float[]>>.Success(texts.Select(_ => new[] { 0.1f }).ToList());
            });

        var result = await CreateIngestionService().IngestAsync(kbId, docId, Content(), "secret.txt");

        result.Succeeded.ShouldBeFalse();
        result.DocumentDeleted.ShouldBeTrue();
        // 插入之后还有一次按文档的删除：写下的分块被收回
        _chunkCalls.ShouldBe(["delete", "insert", "delete"]);
        (await GraphNodeCountAsync(docId)).ShouldBe(0);
        (await GraphEdgeCountAsync(docId)).ShouldBe(0);
        var kb = await ReadKbAsync(kbId);
        kb.DocumentCount.ShouldBe(0);
        kb.ChunkCount.ShouldBe(0);
    }

    [Fact]
    public async Task DeleteDocument_InsideATransaction_CleansChunksAndGraphOnlyAfterCommit()
    {
        // 事务里先清分块：文档行的删除对摄取尚不可见，清了也白清；事务回滚时更不能已经把分块删掉
        var kbId = await SeedKbAsync();
        var docId = await SeedDocAsync(kbId, DocumentStatus.Completed);
        Manager.EnableTransaction();

        (await CreateKnowledgeBaseService().DeleteDocumentAsync(kbId, docId)).Succeeded.ShouldBeTrue();

        _vectorStore.Verify(v => v.DeleteByDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);

        await Manager.CommitTransactionAsync();

        _vectorStore.Verify(v => v.DeleteByDocumentAsync(docId, It.IsAny<CancellationToken>()), Times.Once);
        (await DocExistsAsync(docId)).ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteDocument_RolledBack_KeepsTheDocumentAndItsChunks()
    {
        var kbId = await SeedKbAsync(documentCount: 1, chunkCount: 5);
        var docId = await SeedDocAsync(kbId, DocumentStatus.Completed, chunkCount: 5);
        Manager.EnableTransaction();

        (await CreateKnowledgeBaseService().DeleteDocumentAsync(kbId, docId)).Succeeded.ShouldBeTrue();
        await Manager.RollbackTransactionAsync();

        _vectorStore.Verify(v => v.DeleteByDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        (await DocExistsAsync(docId)).ShouldBeTrue();
        var kb = await ReadKbAsync(kbId);
        kb.DocumentCount.ShouldBe(1);
        kb.ChunkCount.ShouldBe(5);
    }

    // =====================================================================
    // 分块表之外的向量存储：分块表的行要另删
    // =====================================================================

    [Fact]
    public async Task DeleteDocument_WithAStoreOutsideTheChunkTable_AlsoDeletesTheChunkRows()
    {
        // 向量存储只删它自己那边；分块表的行此前永不删除，分块表上的关键词检索继续返回已删文档的内容
        _vectorStore.Setup(v => v.UsesRagChunkTable).Returns(false);
        var kbId = await SeedKbAsync(documentCount: 1, chunkCount: 2);
        var docId = await SeedDocAsync(kbId, DocumentStatus.Completed, chunkCount: 2);

        (await CreateKnowledgeBaseService().DeleteDocumentAsync(kbId, docId)).Succeeded.ShouldBeTrue();

        _vectorStore.Verify(v => v.DeleteByDocumentAsync(docId, It.IsAny<CancellationToken>()), Times.Once);
        _chunkCalls.ShouldBe(["delete"]);
    }

    [Fact]
    public async Task DocumentDeletedWhileBeingIngested_WithAStoreOutsideTheChunkTable_LeavesNothingInTheStore()
    {
        _vectorStore.Setup(v => v.UsesRagChunkTable).Returns(false);
        var kbId = await SeedKbAsync();
        var docId = await SeedDocAsync(kbId, DocumentStatus.Processing);
        var kbService = CreateKnowledgeBaseService();
        _embedding
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(async (List<string> texts, EmbeddingOptions? _, CancellationToken _) =>
            {
                (await kbService.DeleteDocumentAsync(kbId, docId)).Succeeded.ShouldBeTrue();
                return Result<List<float[]>>.Success(texts.Select(_ => new[] { 0.1f }).ToList());
            });

        var result = await CreateIngestionService().IngestAsync(kbId, docId, Content(), "secret.txt");

        result.DocumentDeleted.ShouldBeTrue();
        _vectorStore.Verify(v => v.UpsertAsync(It.IsAny<Guid>(), It.IsAny<float[]>(), docId, kbId,
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        // 删除方一次、摄取替换旧块一次、摄取发现文档已删收尾一次：最后一次在写入之后
        _vectorStore.Verify(v => v.DeleteByDocumentAsync(docId, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }
}
