using Tnzi.AI.Rag.Tests.TestInfrastructure;

namespace Tnzi.AI.Rag.Tests;

/// <summary>
/// <see cref="DocumentIngestionBackgroundJob"/> 一致性 + 幂等性测试。
/// <para>
/// 复现并守护已修复的生产 bug：成功摄取后文档 <c>Status</c>/<c>ChunkCount</c> 回写与知识库
/// <c>DocumentCount</c>/<c>ChunkCount</c> 计数器必须严格一致，且 Hangfire 重试不得双重计数。
/// </para>
/// <para>
/// 使用 SQLite 内存库 + 真实 <see cref="EFCoreRepository{TDbContext, TEntity, TKey}"/>，
/// 以执行真实的 <c>ExecuteUpdateAsync</c>（这是修复的核心原语）；摄取服务被 mock。
/// </para>
/// </summary>
public class DocumentIngestionBackgroundJobTests : IntegratedTestBase<RagJobTestDbContext>
{
    private readonly IRepository<KnowledgeDocument, Guid> _docRepository;
    private readonly IRepository<KnowledgeBase, Guid> _kbRepository;

    public DocumentIngestionBackgroundJobTests()
    {
        _docRepository = new EFCoreRepository<RagJobTestDbContext, KnowledgeDocument, Guid>(DbContext, null, ServiceProvider);
        _kbRepository = new EFCoreRepository<RagJobTestDbContext, KnowledgeBase, Guid>(DbContext, null, ServiceProvider);
    }

    [Fact]
    public async Task Success_PersistsCompletedStatusAndChunkCount()
    {
        var (kbId, docId) = await SeedKbAndProcessingDocAsync();
        var job = CreateJob(IngestionReturning(IngestResult.Success(7)));

        await job.ExecuteAsync(JobArgs(kbId, docId));

        var doc = await ReloadDocAsync(docId);
        doc.Status.ShouldBe(DocumentStatus.Completed);
        doc.ChunkCount.ShouldBe(7);
        doc.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public async Task Success_IncrementsKnowledgeBaseCountersConsistentlyWithDocument()
    {
        var (kbId, docId) = await SeedKbAndProcessingDocAsync();
        var job = CreateJob(IngestionReturning(IngestResult.Success(5)));

        await job.ExecuteAsync(JobArgs(kbId, docId));

        // KB 计数器必须与文档状态一致：文档完成 → DocumentCount=1, ChunkCount=5
        var kb = await ReloadKbAsync(kbId);
        kb.DocumentCount.ShouldBe(1);
        kb.ChunkCount.ShouldBe(5);

        var doc = await ReloadDocAsync(docId);
        doc.Status.ShouldBe(DocumentStatus.Completed);
        doc.ChunkCount.ShouldBe(kb.ChunkCount);
    }

    [Fact]
    public async Task Retry_OfAlreadyCompletedDocument_DoesNotDoubleCountCounters()
    {
        var (kbId, docId) = await SeedKbAndProcessingDocAsync();
        var job = CreateJob(IngestionReturning(IngestResult.Success(4)));

        // 首次成功
        await job.ExecuteAsync(JobArgs(kbId, docId));
        // Hangfire 重试同一文档（摄取再次成功）
        await job.ExecuteAsync(JobArgs(kbId, docId));

        var kb = await ReloadKbAsync(kbId);
        // 计数器只递增一次，重试不双重计数
        kb.DocumentCount.ShouldBe(1);
        kb.ChunkCount.ShouldBe(4);

        var doc = await ReloadDocAsync(docId);
        doc.Status.ShouldBe(DocumentStatus.Completed);
        doc.ChunkCount.ShouldBe(4);
    }

    [Fact]
    public async Task IngestionReportsFailure_PersistsFailedStatusAndLeavesCountersUntouched()
    {
        var (kbId, docId) = await SeedKbAndProcessingDocAsync();
        var job = CreateJob(IngestionReturning(IngestResult.Failure("bad file")));

        await job.ExecuteAsync(JobArgs(kbId, docId));

        var doc = await ReloadDocAsync(docId);
        doc.Status.ShouldBe(DocumentStatus.Failed);
        doc.ErrorMessage.ShouldBe("bad file");

        var kb = await ReloadKbAsync(kbId);
        kb.DocumentCount.ShouldBe(0);
        kb.ChunkCount.ShouldBe(0);
    }

    [Fact]
    public async Task IngestionThrows_DurablyPersistsFailedStatus()
    {
        var (kbId, docId) = await SeedKbAndProcessingDocAsync();

        var mock = new Mock<IDocumentIngestionService>();
        mock.Setup(s => s.IngestAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var job = CreateJob(mock);

        // 任务吞掉异常（catch 分支），但必须把 Failed 状态可靠落库
        await job.ExecuteAsync(JobArgs(kbId, docId));

        var doc = await ReloadDocAsync(docId);
        doc.Status.ShouldBe(DocumentStatus.Failed);
        doc.ErrorMessage.ShouldBe("boom");

        var kb = await ReloadKbAsync(kbId);
        kb.DocumentCount.ShouldBe(0);
        kb.ChunkCount.ShouldBe(0);
    }

    [Fact]
    public async Task MissingDocument_NoOp_DoesNotTouchCounters()
    {
        var kbId = await SeedKbAsync();
        var missingDocId = Guid.NewGuid();
        var mock = new Mock<IDocumentIngestionService>();
        var job = CreateJob(mock);

        await job.ExecuteAsync(JobArgs(kbId, missingDocId));

        // 文档不存在直接跳过，绝不调用摄取、绝不动计数器
        mock.Verify(s => s.IngestAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        var kb = await ReloadKbAsync(kbId);
        kb.DocumentCount.ShouldBe(0);
        kb.ChunkCount.ShouldBe(0);
    }

    #region Helpers

    private DocumentIngestionBackgroundJob CreateJob(Mock<IDocumentIngestionService> ingestionMock)
        => new(ingestionMock.Object, _docRepository, _kbRepository, NullLogger<DocumentIngestionBackgroundJob>.Instance, currentTenant: null);

    private static Mock<IDocumentIngestionService> IngestionReturning(IngestResult result)
    {
        var mock = new Mock<IDocumentIngestionService>();
        mock.Setup(s => s.IngestAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock;
    }

    private static DocumentIngestionJobArgs JobArgs(Guid kbId, Guid docId) => new()
    {
        KnowledgeBaseId = kbId,
        DocumentId = docId,
        FileContent = [1, 2, 3],
        FileName = "doc.txt"
    };

    private async Task<Guid> SeedKbAsync()
    {
        var kb = new KnowledgeBase { Name = "KB", EmbeddingProvider = "default" };
        await _kbRepository.InsertAsync(kb);
        return kb.Id;
    }

    private async Task<(Guid kbId, Guid docId)> SeedKbAndProcessingDocAsync()
    {
        var kbId = await SeedKbAsync();
        var doc = new KnowledgeDocument
        {
            KnowledgeBaseId = kbId,
            FileName = "doc.txt",
            Status = DocumentStatus.Processing
        };
        await _docRepository.InsertAsync(doc);
        return (kbId, doc.Id);
    }

    // ExecuteUpdateAsync 直接写库、绕过 ChangeTracker；必须清空跟踪缓存后再读，
    // 否则 GetAsync/FindAsync 会返回种子阶段缓存的陈旧实体（仍是 Processing / 计数 0）。
    private async Task<KnowledgeDocument> ReloadDocAsync(Guid docId)
    {
        DbContext.ChangeTracker.Clear();
        return (await _docRepository.GetAsync(docId))!;
    }

    private async Task<KnowledgeBase> ReloadKbAsync(Guid kbId)
    {
        DbContext.ChangeTracker.Clear();
        return (await _kbRepository.GetAsync(kbId))!;
    }

    #endregion
}
