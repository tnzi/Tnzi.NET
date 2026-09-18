using Tnzi.AI.Rag.Options;
using Tnzi.AI.Rag.Tests.TestInfrastructure;
using Tnzi.BackgroundJobs;
using Tnzi.Data;

namespace Tnzi.AI.Rag.Tests;

/// <summary>
/// <see cref="KnowledgeBaseService.UploadDocumentAsync"/> 的两条持久化路径。
/// </summary>
/// <remarks>
/// <para>
/// 路径 A（框架默认 <c>EnableGlobalUnitOfWork=false</c>、无 Hangfire）：<c>InsertAsync</c> 立即落库，
/// 同步摄取后只改了内存里的 <c>doc.Status</c>，没有任何东西再 SaveChanges —— 响应说 Completed/N，
/// 行永远停在 Processing/0，去重永不命中、删除按 0 扣计数。写下那段「切勿 UpdateAsync」注释的人
/// 假定了一个环境事务，而参考消费方恰好开着它，于是从没在那里看到过。
/// </para>
/// <para>
/// 路径 B（全局 UoW + Hangfire）：INSERT 延迟到提交，而 <c>Enqueue</c> 立刻执行；worker 抢在提交前
/// 跑起来就查不到行，记一条 Warning 后当成功返回 —— 文档永远 Processing，且不会重试。
/// </para>
/// 用真实 SQLite + 真实 <see cref="EFCoreRepository{TDbContext, TEntity, TKey}"/>：Mock 仓储既不会立即保存
/// 也不会延迟保存，这两个缺陷在 Mock 下根本不存在。
/// </remarks>
public class KnowledgeBaseServiceUploadTests : IntegratedTestBase<RagJobTestDbContext>
{
    private readonly Mock<IDocumentIngestionService> _ingestion = new();
    private readonly Mock<IBackgroundJobManager> _jobManager = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        var entityManager = new Mock<IEntityManager>();
        entityManager.Setup(m => m.GetAllDbContextTypes()).Returns([typeof(RagJobTestDbContext)]);
        services.AddSingleton(_ => entityManager.Object);
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();
        services.AddScoped<IPostCommitActionQueue, PostCommitActionQueue>();

        services.AddScoped<IRepository<KnowledgeBase, Guid>, EFCoreRepository<RagJobTestDbContext, KnowledgeBase, Guid>>();
        services.AddScoped<IRepository<KnowledgeDocument, Guid>, EFCoreRepository<RagJobTestDbContext, KnowledgeDocument, Guid>>();
    }

    private IUnitOfWorkManager Manager => ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

    private KnowledgeBaseService CreateService(bool withJobManager)
        => new(
            ServiceProvider.GetRequiredService<IRepository<KnowledgeBase, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<KnowledgeDocument, Guid>>(),
            new Mock<IRepository<DocumentChunk, Guid>>().Object,
            new Mock<IRepository<KnowledgeGraphNode, Guid>>().Object,
            new Mock<IRepository<KnowledgeGraphEdge, Guid>>().Object,
            _ingestion.Object,
            new Mock<IVectorStore>().Object,
            new Mock<IEmbeddingService>().Object,
            new Mock<IReranker>().Object,
            new StaticOptionsMonitor<AIRagOptions>(new AIRagOptions()),
            ServiceProvider,
            withJobManager ? _jobManager.Object : null);

    private void IngestionReturns(IngestResult result)
        => _ingestion
            .Setup(s => s.IngestAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    private async Task<Guid> SeedKbAsync()
    {
        var kb = new KnowledgeBase { Name = "KB", EmbeddingProvider = "default" };
        DbContext.Set<KnowledgeBase>().Add(kb);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return kb.Id;
    }

    private async Task<KnowledgeDocument?> ReadDocAsync(Guid docId)
        => await DbContext.Set<KnowledgeDocument>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == docId);

    private async Task<KnowledgeBase> ReadKbAsync(Guid kbId)
        => await DbContext.Set<KnowledgeBase>().AsNoTracking().SingleAsync(k => k.Id == kbId);

    private static MemoryStream Content(string text) => new(System.Text.Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task UploadDocumentAsync_SyncFallback_WithoutUnitOfWork_PersistsCompletedStatus()
    {
        var kbId = await SeedKbAsync();
        IngestionReturns(IngestResult.Success(3));

        var result = await CreateService(withJobManager: false).UploadDocumentAsync(kbId, Content("hello"), "a.txt");

        result.Succeeded.ShouldBeTrue();
        result.Data!.Status.ShouldBe(DocumentStatus.Completed);

        var row = await ReadDocAsync(result.Data.DocumentId);
        row.ShouldNotBeNull();
        row!.Status.ShouldBe(DocumentStatus.Completed, "the response said Completed but the row must say so too");
        row.ChunkCount.ShouldBe(3);

        var kb = await ReadKbAsync(kbId);
        kb.DocumentCount.ShouldBe(1);
        kb.ChunkCount.ShouldBe(3);
    }

    [Fact]
    public async Task UploadDocumentAsync_SyncFallback_WithoutUnitOfWork_PersistsFailedStatus()
    {
        var kbId = await SeedKbAsync();
        IngestionReturns(IngestResult.Failure("bad file"));

        var result = await CreateService(withJobManager: false).UploadDocumentAsync(kbId, Content("hello"), "a.txt");

        result.Succeeded.ShouldBeTrue();
        var row = await ReadDocAsync(result.Data!.DocumentId);
        row!.Status.ShouldBe(DocumentStatus.Failed);
        row.ErrorMessage.ShouldBe("bad file");
        (await ReadKbAsync(kbId)).DocumentCount.ShouldBe(0);
    }

    [Fact]
    public async Task UploadDocumentAsync_SyncFallback_SecondUploadOfSameContent_IsDeduplicated()
    {
        // 去重谓词是 Status == Completed：状态没落库，同一份文件每次上传都会再切一遍块
        var kbId = await SeedKbAsync();
        IngestionReturns(IngestResult.Success(2));
        var service = CreateService(withJobManager: false);

        var first = await service.UploadDocumentAsync(kbId, Content("same"), "a.txt");
        var second = await service.UploadDocumentAsync(kbId, Content("same"), "b.txt");

        second.Data!.IsDuplicate.ShouldBeTrue();
        second.Data.DocumentId.ShouldBe(first.Data!.DocumentId);
        _ingestion.Verify(s => s.IngestAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadDocumentAsync_SyncFallback_UnderUnitOfWork_PersistsCompletedStatusAfterCommit()
    {
        var kbId = await SeedKbAsync();
        IngestionReturns(IngestResult.Success(4));
        Manager.EnableTransaction();

        var result = await CreateService(withJobManager: false).UploadDocumentAsync(kbId, Content("hello"), "a.txt");
        await Manager.CommitTransactionAsync();

        var row = await ReadDocAsync(result.Data!.DocumentId);
        row!.Status.ShouldBe(DocumentStatus.Completed);
        row.ChunkCount.ShouldBe(4);
        (await ReadKbAsync(kbId)).ChunkCount.ShouldBe(4);
    }

    [Fact]
    public async Task UploadDocumentAsync_WithBackgroundJobManager_UnderUnitOfWork_EnqueuesOnlyAfterCommit()
    {
        var kbId = await SeedKbAsync();
        var visibleAtEnqueue = new List<bool>();
        _jobManager
            .Setup(m => m.Enqueue(It.IsAny<DocumentIngestionJobArgs>()))
            .Callback<DocumentIngestionJobArgs>(args =>
                visibleAtEnqueue.Add(ReadDocAsync(args.DocumentId).GetAwaiter().GetResult() is not null))
            .Returns("job-1");
        Manager.EnableTransaction();

        var result = await CreateService(withJobManager: true).UploadDocumentAsync(kbId, Content("hello"), "a.txt");

        result.Data!.Status.ShouldBe(DocumentStatus.Processing);
        visibleAtEnqueue.ShouldBeEmpty("the job must not be enqueued while the INSERT is still uncommitted");

        await Manager.CommitTransactionAsync();

        visibleAtEnqueue.ShouldBe([true], "the job runs after commit and must find the row");
    }

    [Fact]
    public async Task UploadDocumentAsync_WithBackgroundJobManager_WithoutUnitOfWork_EnqueuesImmediately()
    {
        var kbId = await SeedKbAsync();
        var visibleAtEnqueue = new List<bool>();
        _jobManager
            .Setup(m => m.Enqueue(It.IsAny<DocumentIngestionJobArgs>()))
            .Callback<DocumentIngestionJobArgs>(args =>
                visibleAtEnqueue.Add(ReadDocAsync(args.DocumentId).GetAwaiter().GetResult() is not null))
            .Returns("job-1");

        await CreateService(withJobManager: true).UploadDocumentAsync(kbId, Content("hello"), "a.txt");

        visibleAtEnqueue.ShouldBe([true]);
    }
}
