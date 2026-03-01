namespace Tnzi.AI.Rag.Services;

/// <summary>
/// 知识库服务实现
/// </summary>
public class KnowledgeBaseService : ApplicationService, IKnowledgeBaseService
{
    private readonly IRepository<KnowledgeBase, Guid> _kbRepository;
    private readonly IRepository<KnowledgeDocument, Guid> _docRepository;
    private readonly IDocumentIngestionService _ingestionService;
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly AIRagOptions _options;
    private readonly IReranker _reranker;
    private readonly RagDbContext _dbContext;
    private readonly IBackgroundJobManager? _backgroundJobManager;

    public KnowledgeBaseService(
        IRepository<KnowledgeBase, Guid> kbRepository,
        IRepository<KnowledgeDocument, Guid> docRepository,
        IDocumentIngestionService ingestionService,
        IVectorStore vectorStore,
        IEmbeddingService embeddingService,
        IReranker reranker,
        RagDbContext dbContext,
        IOptions<AIRagOptions> options,
        IServiceProvider serviceProvider,
        IBackgroundJobManager? backgroundJobManager = null) : base(serviceProvider)
    {
        _kbRepository = Check.NotNull(kbRepository);
        _docRepository = Check.NotNull(docRepository);
        _ingestionService = Check.NotNull(ingestionService);
        _vectorStore = Check.NotNull(vectorStore);
        _embeddingService = Check.NotNull(embeddingService);
        _reranker = Check.NotNull(reranker);
        _dbContext = Check.NotNull(dbContext);
        _options = Check.NotNull(options).Value;
        _backgroundJobManager = backgroundJobManager;
    }

    /// <inheritdoc />
    public async Task<Result<KnowledgeBaseDto>> CreateAsync(CreateKnowledgeBaseDto input, CancellationToken ct = default)
    {
        Check.NotNull(input);

        var entity = new KnowledgeBase
        {
            Name = input.Name,
            Description = input.Description,
            EmbeddingProvider = input.EmbeddingProvider ?? "default",
            EmbeddingModel = input.EmbeddingModel,
            ChunkSize = input.ChunkSize ?? _options.DefaultChunkSize,
            ChunkOverlap = input.ChunkOverlap ?? _options.DefaultChunkOverlap
        };

        await _kbRepository.InsertAsync(entity, ct);
        Logger.LogInformation("Created knowledge base {Id}: {Name}", entity.Id, entity.Name);

        return Ok(entity.MapTo<KnowledgeBaseDto>());
    }

    /// <inheritdoc />
    public async Task<Result<KnowledgeBaseDto>> UpdateAsync(Guid id, UpdateKnowledgeBaseDto input, CancellationToken ct = default)
    {
        Check.NotNull(input);

        var entity = await _kbRepository.GetAsync(id, ct);
        if (entity == null)
        {
            return Fail<KnowledgeBaseDto>("Knowledge base not found", 404, Tnzi.Exceptions.ErrorCodes.RESOURCE_NOT_FOUND);
        }

        if (input.Name != null) entity.Name = input.Name;
        if (input.Description != null) entity.Description = input.Description;
        if (input.IsEnabled.HasValue) entity.IsEnabled = input.IsEnabled.Value;

        await _kbRepository.UpdateAsync(entity, ct);
        return Ok(entity.MapTo<KnowledgeBaseDto>());
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _kbRepository.GetAsync(id, ct);
        if (entity == null)
        {
            return Fail("Knowledge base not found", 404, Tnzi.Exceptions.ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // 级联删除：在事务中执行，确保原子性
        await ExecuteInUnitOfWorkAsync(async _ =>
        {
            await _vectorStore.DeleteByKnowledgeBaseAsync(id, ct);

            var docs = await _docRepository.AsQueryable()
                .Where(d => d.KnowledgeBaseId == id)
                .ToListAsync(ct);
            if (docs.Count > 0)
            {
                await _docRepository.DeleteManyAsync(docs, ct);
            }

            await _kbRepository.DeleteAsync(entity, ct);

            Logger.LogInformation("Deleted knowledge base {Id}: {Name} ({DocCount} documents)",
                id, entity.Name, docs.Count);
        }, ct);

        return Ok();
    }

    /// <inheritdoc />
    public async Task<Result<KnowledgeBaseDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _kbRepository.GetAsync(id, ct);
        if (entity == null)
        {
            return Fail<KnowledgeBaseDto>("Knowledge base not found", 404, Tnzi.Exceptions.ErrorCodes.RESOURCE_NOT_FOUND);
        }

        return Ok(entity.MapTo<KnowledgeBaseDto>());
    }

    /// <inheritdoc />
    public async Task<Result<IPagedList<KnowledgeBaseDto>>> GetListAsync(KnowledgeBaseQueryDto query, CancellationToken ct = default)
    {
        Check.NotNull(query);

        var queryable = _kbRepository.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            queryable = queryable.Where(e => e.Name.ToLower().Contains(query.Keyword.ToLower()));
        }

        var pagedList = await queryable
            .OrderByDescending(e => e.CreationTime)
            .ProjectTo<KnowledgeBase, KnowledgeBaseDto>()
            .CreateAsync(query, ct);

        return Ok(pagedList);
    }

    /// <inheritdoc />
    public async Task<Result<IPagedList<KnowledgeDocumentDto>>> GetDocumentsAsync(Guid kbId, DocumentQueryDto query, CancellationToken ct = default)
    {
        Check.NotNull(query);

        var queryable = _docRepository.AsQueryable()
            .Where(d => d.KnowledgeBaseId == kbId);

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            queryable = queryable.Where(d => d.FileName.ToLower().Contains(query.Keyword.ToLower()));
        }

        if (query.Status.HasValue)
        {
            queryable = queryable.Where(d => d.Status == query.Status.Value);
        }

        var pagedList = await queryable
            .OrderByDescending(d => d.CreationTime)
            .ProjectTo<KnowledgeDocument, KnowledgeDocumentDto>()
            .CreateAsync(query, ct);

        return Ok(pagedList);
    }

    /// <inheritdoc />
    public async Task<Result<DocumentUploadResultDto>> UploadDocumentAsync(
        Guid kbId, Stream content, string fileName, string? contentType = null, long fileSize = 0, CancellationToken ct = default)
    {
        Check.NotNull(content);
        Check.NotNullOrWhiteSpace(fileName);

        // 确认知识库存在
        var kb = await _kbRepository.GetAsync(kbId, ct);
        if (kb == null)
        {
            return Fail<DocumentUploadResultDto>("Knowledge base not found", 404, Tnzi.Exceptions.ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // 计算内容哈希用于去重
        var contentHash = await ComputeContentHashAsync(content, ct);
        content.Position = 0; // 重置流位置

        // 去重检测: 检查同一知识库中是否已有相同内容的已完成文档
        var existingDoc = await _docRepository.AsQueryable()
            .FirstOrDefaultAsync(d => d.KnowledgeBaseId == kbId
                && d.ContentHash == contentHash
                && d.Status == DocumentStatus.Completed, ct);

        if (existingDoc != null)
        {
            Logger.LogInformation("Duplicate document detected: {FileName} matches existing {ExistingId} (hash={Hash})",
                fileName, existingDoc.Id, contentHash);

            return Ok(new DocumentUploadResultDto
            {
                DocumentId = existingDoc.Id,
                FileName = existingDoc.FileName,
                Status = existingDoc.Status,
                ChunkCount = existingDoc.ChunkCount,
                IsDuplicate = true
            });
        }

        // 创建文档记录
        var doc = new KnowledgeDocument
        {
            KnowledgeBaseId = kbId,
            FileName = fileName,
            ContentType = contentType,
            FileSize = fileSize,
            Status = DocumentStatus.Processing,
            ContentHash = contentHash
        };
        await _docRepository.InsertAsync(doc, ct);

        // 异步摄取：有 BackgroundJobManager 时入队后台任务，否则同步执行（回退兼容）
        if (_backgroundJobManager != null)
        {
            // 将 Stream 缓冲为 byte[]，序列化到后台任务参数
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);

            _backgroundJobManager.Enqueue(new DocumentIngestionJobArgs
            {
                KnowledgeBaseId = kbId,
                DocumentId = doc.Id,
                FileContent = ms.ToArray(),
                FileName = fileName
            });

            Logger.LogInformation("Enqueued background ingestion for document {DocId} ({FileName})", doc.Id, fileName);

            return Ok(new DocumentUploadResultDto
            {
                DocumentId = doc.Id,
                FileName = doc.FileName,
                Status = DocumentStatus.Processing,
                ChunkCount = 0
            });
        }

        // 同步回退路径（无 Hangfire 模块时）
        var prefix = _options.TableNamePrefix;
        var ingestResult = await _ingestionService.IngestAsync(kbId, doc.Id, content, fileName, ct);

        if (ingestResult.Succeeded)
        {
            doc.Status = DocumentStatus.Completed;
            doc.ChunkCount = ingestResult.ChunkCount;

            // 原子更新知识库统计（避免并发竞态）
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "{prefix}_KnowledgeBase" SET "DocumentCount" = "DocumentCount" + 1, "ChunkCount" = "ChunkCount" + {ingestResult.ChunkCount} WHERE "Id" = {kbId}""", ct);
        }
        else
        {
            doc.Status = DocumentStatus.Failed;
            doc.ErrorMessage = ingestResult.ErrorMessage;
        }

        await _docRepository.UpdateAsync(doc, ct);

        return Ok(new DocumentUploadResultDto
        {
            DocumentId = doc.Id,
            FileName = doc.FileName,
            Status = doc.Status,
            ChunkCount = doc.ChunkCount,
            ErrorMessage = doc.ErrorMessage
        });
    }

    /// <inheritdoc />
    public async Task<Result> DeleteDocumentAsync(Guid kbId, Guid documentId, CancellationToken ct = default)
    {
        var doc = await _docRepository.AsQueryable()
            .FirstOrDefaultAsync(d => d.Id == documentId && d.KnowledgeBaseId == kbId, ct);
        if (doc == null)
        {
            return Fail("Document not found", 404, Tnzi.Exceptions.ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // 删除块
        await _vectorStore.DeleteByDocumentAsync(documentId, ct);

        // 原子更新知识库统计（避免并发竞态）
        var prefix = _options.TableNamePrefix;
        var chunkCount = doc.ChunkCount;
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "{prefix}_KnowledgeBase" SET "DocumentCount" = CASE WHEN "DocumentCount" - 1 < 0 THEN 0 ELSE "DocumentCount" - 1 END, "ChunkCount" = CASE WHEN "ChunkCount" - {chunkCount} < 0 THEN 0 ELSE "ChunkCount" - {chunkCount} END WHERE "Id" = {kbId}""", ct);

        await _docRepository.DeleteAsync(doc, ct);

        Logger.LogInformation("Deleted document {DocId} ({FileName}) from knowledge base {KbId}",
            documentId, doc.FileName, kbId);

        return Ok();
    }

    /// <inheritdoc />
    public async Task<Result<List<SearchResultDto>>> SearchAsync(
        Guid kbId, string query, int topK = 5, Dictionary<string, string>? metadataFilter = null, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(query);
        topK = Math.Clamp(topK, 1, _options.MaxTopK);

        using var activity = RagActivitySource.StartSearchActivity(topK, kbId);
        var sw = Stopwatch.StartNew();

        // 生成查询向量
        var embeddingResult = await _embeddingService.GenerateEmbeddingAsync(query, ct: ct);
        if (!embeddingResult.Succeeded)
        {
            return Fail<List<SearchResultDto>>($"Embedding generation failed: {embeddingResult.Message}");
        }

        var rawResults = await _vectorStore.SearchAsync(embeddingResult.Data!, topK, kbId, metadataFilter, ct);
        var results = await _reranker.RerankAsync(query, rawResults, topK, ct);

        // 获取知识库名称
        var kb = await _kbRepository.GetAsync(kbId, ct);

        // 获取文档名映射
        var docIds = results.Select(r => r.DocumentId).Distinct().ToList();
        var docs = await _docRepository.AsQueryable()
            .Where(d => docIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.FileName, ct);

        sw.Stop();
        RagActivitySource.RecordSearch(results.Count, sw.Elapsed.TotalSeconds, kbId);

        return Ok(results.Select(r => new SearchResultDto
        {
            Content = r.Content,
            SourceName = docs.GetValueOrDefault(r.DocumentId),
            KnowledgeBaseName = kb?.Name,
            Score = r.Score,
            ChunkIndex = r.ChunkIndex,
            Metadata = r.Metadata
        }).ToList());
    }

    /// <inheritdoc />
    public async Task<Result<List<SearchResultDto>>> SearchAllAsync(
        string query, int topK = 5, Dictionary<string, string>? metadataFilter = null, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(query);
        topK = Math.Clamp(topK, 1, _options.MaxTopK);

        using var activity = RagActivitySource.StartSearchActivity(topK);
        var sw = Stopwatch.StartNew();

        // 生成查询向量
        var embeddingResult = await _embeddingService.GenerateEmbeddingAsync(query, ct: ct);
        if (!embeddingResult.Succeeded)
        {
            return Fail<List<SearchResultDto>>($"Embedding generation failed: {embeddingResult.Message}");
        }

        var rawResults = await _vectorStore.SearchAsync(embeddingResult.Data!, topK, knowledgeBaseId: null, metadataFilter, ct);
        var results = await _reranker.RerankAsync(query, rawResults, topK, ct);

        // 获取文档名映射
        var docIds = results.Select(r => r.DocumentId).Distinct().ToList();
        var docs = await _docRepository.AsQueryable()
            .Where(d => docIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.FileName, ct);

        // 利用 VectorSearchResult.KnowledgeBaseId 直接获取知识库名（无 N+1 查询）
        var kbIds = results.Select(r => r.KnowledgeBaseId).Distinct().ToList();
        var kbs = await _kbRepository.AsQueryable()
            .Where(kb => kbIds.Contains(kb.Id))
            .ToDictionaryAsync(kb => kb.Id, kb => kb.Name, ct);

        sw.Stop();
        RagActivitySource.RecordSearch(results.Count, sw.Elapsed.TotalSeconds);

        return Ok(results.Select(r => new SearchResultDto
        {
            Content = r.Content,
            SourceName = docs.GetValueOrDefault(r.DocumentId),
            KnowledgeBaseName = kbs.GetValueOrDefault(r.KnowledgeBaseId),
            Score = r.Score,
            ChunkIndex = r.ChunkIndex,
            Metadata = r.Metadata
        }).ToList());
    }

    /// <inheritdoc />
    public async Task<Result<KnowledgeDocumentDto>> GetDocumentStatusAsync(Guid kbId, Guid documentId, CancellationToken ct = default)
    {
        var doc = await _docRepository.AsQueryable()
            .FirstOrDefaultAsync(d => d.Id == documentId && d.KnowledgeBaseId == kbId, ct);
        if (doc == null)
        {
            return Fail<KnowledgeDocumentDto>("Document not found", 404, Tnzi.Exceptions.ErrorCodes.RESOURCE_NOT_FOUND);
        }

        return Ok(doc.MapTo<KnowledgeDocumentDto>());
    }

    /// <summary>
    /// 计算流内容的 SHA256 哈希
    /// </summary>
    private static async Task<string> ComputeContentHashAsync(Stream content, CancellationToken ct)
    {
        var hashBytes = await SHA256.HashDataAsync(content, ct);
        return Convert.ToHexString(hashBytes).ToLower();
    }
}
