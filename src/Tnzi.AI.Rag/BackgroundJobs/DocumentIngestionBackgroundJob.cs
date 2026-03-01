namespace Tnzi.AI.Rag.BackgroundJobs;

/// <summary>
/// 文档摄取后台任务 — 异步执行文档提取、切块、嵌入、存储流程
/// </summary>
public class DocumentIngestionBackgroundJob : IBackgroundJob<DocumentIngestionJobArgs>
{
    private readonly IDocumentIngestionService _ingestionService;
    private readonly IRepository<KnowledgeDocument, Guid> _docRepository;
    private readonly RagDbContext _dbContext;
    private readonly AIRagOptions _options;
    private readonly ILogger<DocumentIngestionBackgroundJob> _logger;

    public DocumentIngestionBackgroundJob(
        IDocumentIngestionService ingestionService,
        IRepository<KnowledgeDocument, Guid> docRepository,
        RagDbContext dbContext,
        IOptions<AIRagOptions> options,
        ILogger<DocumentIngestionBackgroundJob> logger)
    {
        _ingestionService = Check.NotNull(ingestionService);
        _docRepository = Check.NotNull(docRepository);
        _dbContext = Check.NotNull(dbContext);
        _options = Check.NotNull(options).Value;
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(DocumentIngestionJobArgs args, CancellationToken cancellationToken = default)
    {
        Check.NotNull(args);

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
                doc.Status = DocumentStatus.Completed;
                doc.ChunkCount = ingestResult.ChunkCount;
                await _docRepository.UpdateAsync(doc);

                // 原子更新知识库统计（避免并发竞态）
                var prefix = _options.TableNamePrefix;
                await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""UPDATE "{prefix}_KnowledgeBase" SET "DocumentCount" = "DocumentCount" + 1, "ChunkCount" = "ChunkCount" + {ingestResult.ChunkCount} WHERE "Id" = {args.KnowledgeBaseId}""");

                _logger.LogInformation(
                    "Background ingestion completed for document {DocumentId}: {ChunkCount} chunks",
                    args.DocumentId, ingestResult.ChunkCount);
            }
            else
            {
                doc.Status = DocumentStatus.Failed;
                doc.ErrorMessage = ingestResult.ErrorMessage;
                await _docRepository.UpdateAsync(doc);

                _logger.LogWarning(
                    "Background ingestion failed for document {DocumentId}: {Error}",
                    args.DocumentId, ingestResult.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            doc.Status = DocumentStatus.Failed;
            doc.ErrorMessage = ex.Message;
            await _docRepository.UpdateAsync(doc);

            _logger.LogError(ex,
                "Background ingestion threw exception for document {DocumentId}",
                args.DocumentId);
        }
    }
}
