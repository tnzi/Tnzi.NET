namespace Tnzi.AI.Rag.Services;

/// <summary>
/// 知识库服务实现
/// </summary>
public class KnowledgeBaseService : ApplicationService, IKnowledgeBaseService
{
    private readonly IRepository<KnowledgeBase, Guid> _kbRepository;
    private readonly IRepository<KnowledgeDocument, Guid> _docRepository;
    private readonly IRepository<DocumentChunk, Guid> _chunkRepository;
    private readonly IRepository<KnowledgeGraphNode, Guid> _graphNodeRepository;
    private readonly IRepository<KnowledgeGraphEdge, Guid> _graphEdgeRepository;
    private readonly IDocumentIngestionService _ingestionService;
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly AIRagOptions _options;
    private readonly IReranker _reranker;
    private readonly IBackgroundJobManager? _backgroundJobManager;

    public KnowledgeBaseService(
        IRepository<KnowledgeBase, Guid> kbRepository,
        IRepository<KnowledgeDocument, Guid> docRepository,
        IRepository<DocumentChunk, Guid> chunkRepository,
        IRepository<KnowledgeGraphNode, Guid> graphNodeRepository,
        IRepository<KnowledgeGraphEdge, Guid> graphEdgeRepository,
        IDocumentIngestionService ingestionService,
        IVectorStore vectorStore,
        IEmbeddingService embeddingService,
        IReranker reranker,
        IOptionsSnapshot<AIRagOptions> options,
        IServiceProvider serviceProvider,
        IBackgroundJobManager? backgroundJobManager = null) : base(serviceProvider)
    {
        _kbRepository = Check.NotNull(kbRepository);
        _docRepository = Check.NotNull(docRepository);
        _chunkRepository = Check.NotNull(chunkRepository);
        _graphNodeRepository = Check.NotNull(graphNodeRepository);
        _graphEdgeRepository = Check.NotNull(graphEdgeRepository);
        _ingestionService = Check.NotNull(ingestionService);
        _vectorStore = Check.NotNull(vectorStore);
        _embeddingService = Check.NotNull(embeddingService);
        _reranker = Check.NotNull(reranker);
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

            // 图谱数据与 chunk/doc 同等显式删除：KB 是软删（MultiTenantAuditedEntity），
            // KnowledgeGraphNode/Edge 的 FK 级联只在 KB 物理删除时触发，软删后图谱表会永久残留悬挂数据。
            // 先删边（边对节点有 FK），再删节点。
            var edges = await _graphEdgeRepository.AsQueryable()
                .Where(e => e.KnowledgeBaseId == id)
                .ToListAsync(ct);
            if (edges.Count > 0)
            {
                await _graphEdgeRepository.DeleteManyAsync(edges, ct);
            }

            var nodes = await _graphNodeRepository.AsQueryable()
                .Where(n => n.KnowledgeBaseId == id)
                .ToListAsync(ct);
            if (nodes.Count > 0)
            {
                await _graphNodeRepository.DeleteManyAsync(nodes, ct);
            }

            await _kbRepository.DeleteAsync(entity, ct);

            Logger.LogInformation(
                "Deleted knowledge base {Id}: {Name} ({DocCount} documents, {NodeCount} graph nodes, {EdgeCount} graph edges)",
                id, entity.Name, docs.Count, nodes.Count, edges.Count);
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
        var ingestResult = await _ingestionService.IngestAsync(kbId, doc.Id, content, fileName, ct);

        if (ingestResult.Succeeded)
        {
            doc.Status = DocumentStatus.Completed;
            doc.ChunkCount = ingestResult.ChunkCount;

            // 原子更新知识库统计（避免并发竞态，通过 EF Core 查询过滤器保持租户隔离）
            await _kbRepository.AsQueryable()
                .Where(k => k.Id == kbId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(k => k.DocumentCount, k => k.DocumentCount + 1)
                    .SetProperty(k => k.ChunkCount, k => k.ChunkCount + ingestResult.ChunkCount), ct);
        }
        else
        {
            doc.Status = DocumentStatus.Failed;
            doc.ErrorMessage = ingestResult.ErrorMessage;
        }

        // doc 仍处于 Added 状态（InsertAsync 在 UnitOfWork 内延迟提交至 action 末尾），
        // 上面对 Status/ChunkCount/ErrorMessage 的修改会随末尾 SaveChanges 直接写入 INSERT。
        // 切勿在此调用 UpdateAsync(doc) —— 会把 ChangeTracker 状态从 Added 翻成 Modified，
        // 致 SaveChanges 对尚未 INSERT 的行发 UPDATE → "affected 0 rows" 乐观并发异常
        // （同 AgentThreadService Phase 1.5 InsertAsync-then-Update 教训）。

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

        // 原子更新知识库统计（避免并发竞态，通过 EF Core 查询过滤器保持租户隔离）
        var chunkCount = doc.ChunkCount;
        await _kbRepository.AsQueryable()
            .Where(k => k.Id == kbId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(k => k.DocumentCount, k => k.DocumentCount > 0 ? k.DocumentCount - 1 : 0)
                .SetProperty(k => k.ChunkCount, k => k.ChunkCount >= chunkCount ? k.ChunkCount - chunkCount : 0), ct);

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

        // 获取知识库配置：query 向量必须与摄取时使用相同的 embedding provider/model，
        // 否则向量空间不一致、检索无意义（此前漏传 options 致回退到 AI 顶层 DefaultProvider，
        // 与摄取使用的 DefaultEmbeddingProvider 不一致 - 例如 DeepSeek 根本无 embedding API）。
        var kb = await _kbRepository.GetAsync(kbId, ct);
        if (kb == null)
        {
            return Fail<List<SearchResultDto>>($"Knowledge base not found: {kbId}", 404);
        }

        // 生成查询向量（provider/model 与摄取对齐）
        var embeddingOptions = RagEmbeddingOptionsResolver.Resolve(kb, _options);
        var embeddingResult = await _embeddingService.GenerateEmbeddingAsync(query, embeddingOptions, ct);
        if (!embeddingResult.Succeeded)
        {
            return Fail<List<SearchResultDto>>($"Embedding generation failed: {embeddingResult.Message}");
        }

        var rawResults = await _vectorStore.SearchAsync(embeddingResult.Data!, topK, kbId, metadataFilter, ct);
        var results = await _reranker.RerankAsync(query, rawResults, topK, ct);

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
            KnowledgeBaseName = kb.Name,
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

        // 生成查询向量：跨库搜索使用全局默认 embedding provider/model
        // （此前漏传 options 致回退到 AI 顶层 DefaultProvider，可能无 embedding 能力）。
        var embeddingOptions = RagEmbeddingOptionsResolver.ResolveDefault(_options);
        var embeddingResult = await _embeddingService.GenerateEmbeddingAsync(query, embeddingOptions, ct);
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

    // 重新索引批处理大小 - 每批读取/嵌入/回写的块数量。
    // 选择 50 作为折中：足够大以摊销每批 LLM/HTTP 往返开销，又足够小以保持单批 UOW 提交可控、
    // 内存占用稳定（每个 chunk 文本通常 < 2KB，50 chunks ≈ 100KB）。
    private const int ReindexBatchSize = 50;

    // A reindex is considered abandoned (crashed worker) after this window, at which
    // point another caller is allowed to take over the lock.
    private static readonly TimeSpan ReindexStaleLockThreshold = TimeSpan.FromHours(2);

    /// <inheritdoc />
    /// <remarks>
    /// 设计选择：**前台分批同步执行**（Option A）。
    /// - KnowledgeBaseService 已注入 IBackgroundJobManager（可选），但后台任务模式无法返回 ReindexResultDto 的
    ///   完整统计（ChunkCount/DurationMs），UX 不一致。前台执行保持 admin 操作语义清晰：HTTP 请求完成即操作完成。
    /// - 适用规模：典型 KB（≤ 数千 chunks）。超大规模 KB 应在未来迭代引入后台任务 + 进度查询端点。
    /// - 幂等性：对同一 KB 多次调用产生相同终态（每个 chunk 的 Embedding 被对应文本的最新嵌入覆盖）。
    /// - 失败语义：单个 chunk 嵌入失败 → fail-fast，返回错误并附带失败的 chunk id；不静默跳过。
    /// - UOW 范围：按批提交（每批一次 UpdateManyAsync）。这是 stage-2 的明确权衡：
    ///   单一全量 UOW 对大 KB 内存压力过大；按批提交意味着中途失败时已嵌入的批次会持久化，
    ///   但由于操作是幂等的，重新触发即可恢复一致状态。
    /// </remarks>
    public async Task<Result<ReindexResultDto>> ReindexAsync(Guid knowledgeBaseId, CancellationToken cancellationToken = default)
    {
        var kb = await _kbRepository.GetAsync(knowledgeBaseId, cancellationToken);
        if (kb == null)
        {
            return Fail<ReindexResultDto>("Knowledge base not found", 404, Tnzi.Exceptions.ErrorCodes.RESOURCE_NOT_FOUND);
        }

        var now = DateTime.UtcNow;
        var acquired = await TryAcquireReindexLockAsync(kb, now, cancellationToken);

        if (!acquired)
        {
            Logger.LogInformation("Reindex rejected for knowledge base {KbId} ({Name}): another reindex is already in progress", knowledgeBaseId, kb.Name);
            return Fail<ReindexResultDto>(
                "A reindex operation is already in progress for this knowledge base. " +
                $"If you believe this is a stale lock from a crashed worker, wait up to {ReindexStaleLockThreshold.TotalHours:F0}h.",
                409);
        }

        var sw = Stopwatch.StartNew();
        try
        {
            // 加载所有块（按 ChunkIndex 排序保证确定性顺序，便于幂等性验证和日志追踪）
            var allChunks = await _chunkRepository.AsQueryable()
                .Where(c => c.KnowledgeBaseId == knowledgeBaseId)
                .OrderBy(c => c.DocumentId)
                .ThenBy(c => c.ChunkIndex)
                .ToListAsync(cancellationToken);

            if (allChunks.Count == 0)
            {
                sw.Stop();
                Logger.LogInformation("Reindex skipped for empty knowledge base {KbId} ({Name})", knowledgeBaseId, kb.Name);
                return Ok(new ReindexResultDto
                {
                    KnowledgeBaseId = knowledgeBaseId,
                    ChunkCount = 0,
                    DocumentCount = 0,
                    DurationMs = sw.ElapsedMilliseconds
                });
            }

            var embeddingOptions = RagEmbeddingOptionsResolver.Resolve(kb, _options);

            var documentCount = allChunks.Select(c => c.DocumentId).Distinct().Count();
            var totalProcessed = 0;

            for (var offset = 0; offset < allChunks.Count; offset += ReindexBatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // GetRange is O(batch); Skip+Take on List<T> is O(offset+batch) and walks
                // the whole prefix every iteration, which is measurable on multi-thousand-chunk KBs.
                var batchCount = Math.Min(ReindexBatchSize, allChunks.Count - offset);
                var batch = allChunks.GetRange(offset, batchCount);
                var texts = batch.Select(c => c.Content).ToList();

                var embeddingResult = await _embeddingService.GenerateEmbeddingsAsync(texts, embeddingOptions, cancellationToken);
                if (!embeddingResult.Succeeded)
                {
                    var firstChunkId = batch[0].Id;
                    Logger.LogError("Reindex failed for knowledge base {KbId} at batch starting chunk {ChunkId}: {Error}",
                        knowledgeBaseId, firstChunkId, embeddingResult.Message);
                    return Fail<ReindexResultDto>(
                        $"Embedding generation failed at chunk {firstChunkId}: {embeddingResult.Message}");
                }

                var embeddings = embeddingResult.Data!;
                if (embeddings.Count != batch.Count)
                {
                    var firstChunkId = batch[0].Id;
                    Logger.LogError("Reindex embedding count mismatch for knowledge base {KbId} at chunk {ChunkId}: expected {Expected}, got {Actual}",
                        knowledgeBaseId, firstChunkId, batch.Count, embeddings.Count);
                    return Fail<ReindexResultDto>(
                        $"Embedding count mismatch at chunk {firstChunkId}: expected {batch.Count}, got {embeddings.Count}");
                }

                for (var i = 0; i < batch.Count; i++)
                {
                    batch[i].Embedding = new Vector(embeddings[i]);
                }

                await _chunkRepository.UpdateManyAsync(batch, cancellationToken);
                totalProcessed += batch.Count;

                Logger.LogDebug("Reindex progress for knowledge base {KbId}: {Processed}/{Total} chunks",
                    knowledgeBaseId, totalProcessed, allChunks.Count);
            }

            sw.Stop();
            Logger.LogInformation("Reindexed knowledge base {KbId} ({Name}): {ChunkCount} chunks across {DocCount} documents in {DurationMs}ms",
                knowledgeBaseId, kb.Name, totalProcessed, documentCount, sw.ElapsedMilliseconds);

            return Ok(new ReindexResultDto
            {
                KnowledgeBaseId = knowledgeBaseId,
                ChunkCount = totalProcessed,
                DocumentCount = documentCount,
                DurationMs = sw.ElapsedMilliseconds
            });
        }
        finally
        {
            await ReleaseReindexLockAsync(kb, now);
        }
    }

    /// <summary>
    /// True when the exception indicates the LINQ provider doesn't support ExecuteUpdateAsync
    /// (in-memory EF Core provider, or test doubles missing IAsyncQueryProvider). Used to
    /// distinguish provider capability from genuine runtime errors so we don't silently
    /// swallow real failures.
    /// </summary>
    private static bool IsProviderUnsupportedExecuteUpdate(InvalidOperationException ex)
        => ex.Message.Contains("ExecuteUpdate", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("ExecuteAsync", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("AsyncQueryProvider", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("does not implement", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("provider does not support", StringComparison.OrdinalIgnoreCase);

    /// <remarks>
    /// Acquires the reindex mutex via conditional UPDATE. ExecuteUpdateAsync makes
    /// the check+set atomic server-side - safe across multiple app instances. Test
    /// doubles that don't implement IAsyncQueryProvider fall back to a load+update
    /// path with a narrower (milliseconds) race window that is adequate for a unit
    /// test harness.
    /// </remarks>
    private async Task<bool> TryAcquireReindexLockAsync(KnowledgeBase kb, DateTime now, CancellationToken ct)
    {
        var staleCutoff = now - ReindexStaleLockThreshold;
        try
        {
            var affected = await _kbRepository.AsQueryable()
                .Where(k => k.Id == kb.Id && (k.ReindexingAt == null || k.ReindexingAt < staleCutoff))
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.ReindexingAt, now), ct);
            return affected > 0;
        }
        catch (InvalidOperationException ex) when (IsProviderUnsupportedExecuteUpdate(ex))
        {
            // Test double / in-memory provider - fall back to load-then-update with a
            // narrower (millisecond) race window. Genuine runtime InvalidOperation now
            // propagates instead of being silently swallowed.
            if (kb.ReindexingAt != null && kb.ReindexingAt >= staleCutoff)
            {
                return false;
            }
            kb.ReindexingAt = now;
            await _kbRepository.UpdateAsync(kb, cancellationToken: ct);
            return true;
        }
    }

    /// <remarks>
    /// Guard with the acquired timestamp so a later run that legitimately took over
    /// a stale lock from this one is not stomped. Release is best-effort: uses
    /// CancellationToken.None and swallows all exceptions so the caller sees the
    /// real error (if any) from the reindex body, not a release failure. If release
    /// fails the stale-threshold mechanism eventually reclaims the lock.
    /// </remarks>
    private async Task ReleaseReindexLockAsync(KnowledgeBase kb, DateTime acquiredAt)
    {
        try
        {
            await _kbRepository.AsQueryable()
                .Where(k => k.Id == kb.Id && k.ReindexingAt == acquiredAt)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.ReindexingAt, (DateTime?)null), CancellationToken.None);
        }
        catch (InvalidOperationException ex) when (IsProviderUnsupportedExecuteUpdate(ex))
        {
            // Test double / in-memory fallback (mirrors TryAcquire path).
            if (kb.ReindexingAt == acquiredAt)
            {
                kb.ReindexingAt = null;
                try { await _kbRepository.UpdateAsync(kb, cancellationToken: CancellationToken.None); }
                catch (Exception updateEx)
                {
                    Logger.LogWarning(updateEx, "Failed to release reindex lock for knowledge base {KbId}", kb.Id);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to release reindex lock for knowledge base {KbId}", kb.Id);
        }
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
