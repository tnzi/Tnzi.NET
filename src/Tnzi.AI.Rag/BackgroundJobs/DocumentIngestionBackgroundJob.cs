namespace Tnzi.AI.Rag.BackgroundJobs;

/// <summary>
/// 文档摄取后台任务 — 异步执行文档提取、切块、嵌入、存储流程
/// </summary>
/// <remarks>
/// <para>
/// <b>一致性约束</b>：单文档的 <c>Status</c>/<c>ChunkCount</c> 回写与知识库级
/// <c>DocumentCount</c>/<c>ChunkCount</c> 计数器更新必须保持一致，二者不能发散。
/// 历史 bug：成功分支先用 tracked <c>UpdateAsync(doc)</c>（依赖延迟 SaveChanges）回写文档状态，
/// 再用立即提交的 <c>ExecuteUpdateAsync</c> 递增 KB 计数器。若共享的 scoped <c>RagDbContext</c>
/// 在并发/批量任务下触发 "A second operation was started on this context" 之类异常，
/// 文档状态回写会在提交点丢失，而 KB 计数器已先行提交 → 文档永久停留在 <c>Processing</c>，
/// 计数器却已 +1（发散）。
/// </para>
/// <para>
/// <b>修复</b>：文档状态回写改用 <b>条件 <c>ExecuteUpdateAsync</c></b>（绕过 ChangeTracker、立即提交、
/// 不依赖延迟 SaveChanges，规避 second-operation 竞态），且 <b>排在 KB 计数器更新之前</b>。
/// 计数器递增仅在文档状态确实从 <c>Processing</c> 翻成 <c>Completed</c>（affected &gt; 0）时执行，
/// 因此 Hangfire 重试一篇已 <c>Completed</c> 的文档不会重复计数（幂等）。chunk 去重由
/// <see cref="Services.DocumentIngestionService"/> 在插入前清理同 DocumentId 旧块保证。
/// </para>
/// </remarks>
public class DocumentIngestionBackgroundJob : TenantAwareBackgroundJob<DocumentIngestionJobArgs>
{
    private readonly IDocumentIngestionService _ingestionService;
    private readonly IRepository<KnowledgeDocument, Guid> _docRepository;
    private readonly IRepository<KnowledgeBase, Guid> _kbRepository;
    private readonly ILogger<DocumentIngestionBackgroundJob> _logger;

    public DocumentIngestionBackgroundJob(
        IDocumentIngestionService ingestionService,
        IRepository<KnowledgeDocument, Guid> docRepository,
        IRepository<KnowledgeBase, Guid> kbRepository,
        ILogger<DocumentIngestionBackgroundJob> logger,
        ICurrentTenant? currentTenant = null)
        : base(currentTenant)
    {
        _ingestionService = Check.NotNull(ingestionService);
        _docRepository = Check.NotNull(docRepository);
        _kbRepository = Check.NotNull(kbRepository);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    protected override async Task ExecuteInTenantContextAsync(DocumentIngestionJobArgs args, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Starting background ingestion for document {DocumentId} in knowledge base {KbId}",
            args.DocumentId, args.KnowledgeBaseId);

        var doc = await _docRepository.GetAsync(args.DocumentId);
        if (doc == null)
        {
            _logger.LogWarning("Document {DocumentId} not found, skipping ingestion", args.DocumentId);
            return;
        }

        try
        {
            using var stream = new MemoryStream(args.FileContent);
            var ingestResult = await _ingestionService.IngestAsync(
                args.KnowledgeBaseId, args.DocumentId, stream, args.FileName, cancellationToken);

            if (ingestResult.Succeeded)
            {
                // 文档状态回写：条件 ExecuteUpdateAsync（仅当前仍为 Processing 时翻成 Completed），
                // 立即提交、绕过 ChangeTracker，规避共享 DbContext 的 second-operation 竞态。
                // affected == 0 表示该文档已是 Completed（Hangfire 重试），不应重复递增 KB 计数器。
                var affected = await _docRepository.AsQueryable()
                    .Where(d => d.Id == args.DocumentId && d.Status == DocumentStatus.Processing)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(d => d.Status, DocumentStatus.Completed)
                        .SetProperty(d => d.ChunkCount, ingestResult.ChunkCount)
                        .SetProperty(d => d.ErrorMessage, (string?)null), cancellationToken);

                if (affected > 0)
                {
                    // 原子更新知识库统计（避免并发竞态，通过 EF Core 查询过滤器保持租户隔离）。
                    // 只在文档真正完成转换时执行，保证与文档状态严格一致、且对重试幂等。
                    await _kbRepository.AsQueryable()
                        .Where(k => k.Id == args.KnowledgeBaseId)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(k => k.DocumentCount, k => k.DocumentCount + 1)
                            .SetProperty(k => k.ChunkCount, k => k.ChunkCount + ingestResult.ChunkCount), cancellationToken);

                    _logger.LogInformation(
                        "Background ingestion completed for document {DocumentId}: {ChunkCount} chunks",
                        args.DocumentId, ingestResult.ChunkCount);
                }
                else
                {
                    // 文档已是 Completed（典型为 Hangfire 重试）：chunk 已由摄取服务去重重建，
                    // 跳过计数器递增以避免双重计数。
                    _logger.LogInformation(
                        "Background ingestion re-ran for already-completed document {DocumentId}; chunks rebuilt, knowledge base counters left unchanged (idempotent retry)",
                        args.DocumentId);
                }
            }
            else
            {
                await MarkFailedAsync(args.DocumentId, ingestResult.ErrorMessage, cancellationToken);

                _logger.LogWarning(
                    "Background ingestion failed for document {DocumentId}: {Error}",
                    args.DocumentId, ingestResult.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            // 失败状态同样必须持久化：用 ExecuteUpdateAsync 立即提交，不依赖延迟 SaveChanges，
            // 否则在异常情况下状态回写可能与成功分支同样丢失。
            await MarkFailedAsync(args.DocumentId, ex.Message, cancellationToken);

            _logger.LogError(ex,
                "Background ingestion threw exception for document {DocumentId}",
                args.DocumentId);
        }
    }

    /// <summary>
    /// 将文档标记为失败并持久化错误信息。
    /// 使用 <c>ExecuteUpdateAsync</c> 立即提交（绕过 ChangeTracker / 延迟 SaveChanges），
    /// 确保即便在异常路径下失败状态也能可靠落库。错误信息按列长度截断（列上限 2000）。
    /// </summary>
    private async Task MarkFailedAsync(Guid documentId, string? errorMessage, CancellationToken cancellationToken)
    {
        var truncated = errorMessage is { Length: > 2000 } ? errorMessage[..2000] : errorMessage;

        await _docRepository.AsQueryable()
            .Where(d => d.Id == documentId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, DocumentStatus.Failed)
                .SetProperty(d => d.ErrorMessage, truncated), cancellationToken);
    }
}
