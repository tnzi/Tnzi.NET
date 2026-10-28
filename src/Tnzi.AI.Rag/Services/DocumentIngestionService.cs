namespace Tnzi.AI.Rag.Services;

/// <summary>
/// 文档摄取服务 - 提取文本 → 切块 → 生成嵌入 → 存储到 pgvector
/// </summary>
/// <remarks>
/// ★ 与删除文档并发时的约定：<c>KnowledgeBaseService.DeleteDocumentAsync</c> 先删文档行（并提交），再清分块与图谱；
/// 本服务写完分块与图谱之后再确认文档行还在，不在就把自己刚写下的清掉。两边各守一半，任何交错都不留孤儿：
/// 写在清理之前的会被清理带走，写在清理之后的必然看得见「文档已删」。少了末尾这一查，
/// 管理员删掉一份仍在处理中的文档后，摄取随后写入的整套分块会成为无主孤儿，照样被检索命中、却不出现在任何管理列表里。
/// <para>
/// ★ 分块表之外的向量存储（<see cref="IVectorStore.UsesRagChunkTable"/> 为 false：<c>InMemoryVectorStore</c>、外部向量库）
/// 在写完分块表后逐块 <see cref="IVectorStore.UpsertAsync"/>，替换旧块时先清掉它那边本文档的旧条目。此前摄取从不调用它，
/// 这类存储下文档显示完成、检索恒为空。写入失败时连同刚写的分块表行一起撤掉，文档按失败处理。
/// </para>
/// </remarks>
public class DocumentIngestionService : ApplicationService, IDocumentIngestionService
{
    private readonly IEnumerable<IFileExtractorService> _extractors;
    private readonly IChunkingStrategy _chunkingStrategy;
    private readonly IAsyncChunkingStrategy? _asyncChunkingStrategy;
    private readonly IEmbeddingService _embeddingService;
    private readonly IRepository<DocumentChunk, Guid> _chunkRepository;
    private readonly IVectorStore _vectorStore;
    private readonly IRepository<KnowledgeBase, Guid> _kbRepository;
    private readonly IRepository<KnowledgeDocument, Guid> _docRepository;
    private readonly IRepository<KnowledgeGraphNode, Guid> _graphNodeRepository;
    private readonly IRepository<KnowledgeGraphEdge, Guid> _graphEdgeRepository;
    private readonly IGraphExtractor _graphExtractor;
    private readonly AIRagOptions _options;

    public DocumentIngestionService(
        IEnumerable<IFileExtractorService> extractors,
        IChunkingStrategy chunkingStrategy,
        IEmbeddingService embeddingService,
        IRepository<DocumentChunk, Guid> chunkRepository,
        IVectorStore vectorStore,
        IRepository<KnowledgeBase, Guid> kbRepository,
        IRepository<KnowledgeDocument, Guid> docRepository,
        IRepository<KnowledgeGraphNode, Guid> graphNodeRepository,
        IRepository<KnowledgeGraphEdge, Guid> graphEdgeRepository,
        IGraphExtractor graphExtractor,
        IOptionsSnapshot<AIRagOptions> options,
        IServiceProvider serviceProvider,
        IAsyncChunkingStrategy? asyncChunkingStrategy = null) : base(serviceProvider)
    {
        _extractors = Check.NotNull(extractors);
        _chunkingStrategy = Check.NotNull(chunkingStrategy);
        _asyncChunkingStrategy = asyncChunkingStrategy;
        _embeddingService = Check.NotNull(embeddingService);
        _chunkRepository = Check.NotNull(chunkRepository);
        _vectorStore = Check.NotNull(vectorStore);
        _kbRepository = Check.NotNull(kbRepository);
        _docRepository = Check.NotNull(docRepository);
        _graphNodeRepository = Check.NotNull(graphNodeRepository);
        _graphEdgeRepository = Check.NotNull(graphEdgeRepository);
        _graphExtractor = Check.NotNull(graphExtractor);
        _options = Check.NotNull(options).Value;
    }

    /// <inheritdoc />
    public async Task<IngestResult> IngestAsync(Guid kbId, Guid documentId, Stream content, string fileName, CancellationToken ct = default)
    {
        // 1. 找到适合的文件提取器
        var extractor = _extractors.FirstOrDefault(e => e.Supports(fileName));
        if (extractor == null)
        {
            return IngestResult.Failure($"Unsupported file type: {fileName}");
        }

        // 获取知识库配置
        var kb = await _kbRepository.AsQueryable()
            .FirstOrDefaultAsync(e => e.Id == kbId, ct);
        if (kb == null)
        {
            return IngestResult.Failure($"Knowledge base not found: {kbId}");
        }

        using var activity = RagActivitySource.StartIngestionActivity(kbId, fileName);
        var sw = Stopwatch.StartNew();

        try
        {
            // 2. 提取文本
            var text = await extractor.ExtractTextAsync(content, fileName, ct);
            if (string.IsNullOrWhiteSpace(text))
            {
                return IngestResult.Failure("No text content extracted from file");
            }

            Logger.LogDebug("Extracted {Length} characters from {FileName}", text.Length, fileName);

            // 3. 切块（优先使用异步语义分块策略，否则回退到同步策略）
            var chunkSize = kb.ChunkSize > 0 ? kb.ChunkSize : _options.DefaultChunkSize;
            var chunkOverlap = kb.ChunkOverlap >= 0 ? kb.ChunkOverlap : _options.DefaultChunkOverlap;

            var chunks = _asyncChunkingStrategy != null
                ? await _asyncChunkingStrategy.ChunkAsync(text, chunkSize, chunkOverlap, ct)
                : _chunkingStrategy.Chunk(text, chunkSize, chunkOverlap);

            if (chunks.Count == 0)
            {
                return IngestResult.Failure("Text chunking produced no chunks");
            }

            Logger.LogDebug("Chunked into {Count} chunks (size={ChunkSize}, overlap={Overlap})",
                chunks.Count, chunkSize, chunkOverlap);

            // 4. 批量生成嵌入（provider/model 解析与查询路径共用同一 helper，保证向量空间一致）
            var embeddingOptions = RagEmbeddingOptionsResolver.Resolve(kb, _options);

            var allEmbeddings = new List<float[]>();
            var batchSize = _options.EmbeddingBatchSize;

            for (var i = 0; i < chunks.Count; i += batchSize)
            {
                // GetRange 是 O(batch)；Skip+Take 每轮都要重走 i 长度的前缀（O(n²)），
                // 大文档切出数千块时可测（与 KnowledgeBaseService.ReindexAsync 同处理）。
                var batch = chunks.GetRange(i, Math.Min(batchSize, chunks.Count - i));
                var embeddingResult = await _embeddingService.GenerateEmbeddingsAsync(batch, embeddingOptions, ct);

                if (!embeddingResult.Succeeded)
                {
                    return IngestResult.Failure($"Embedding generation failed: {embeddingResult.Message}");
                }

                allEmbeddings.AddRange(embeddingResult.Data!);

                Logger.LogDebug("Generated embeddings for batch {Batch}/{Total}",
                    Math.Min(i + batchSize, chunks.Count), chunks.Count);
            }

            // 5. 幂等性：清理本文档已存在的旧块再插入。
            // 后台摄取任务可能被 Hangfire 重试（chunk 在状态回写之前就已提交），
            // 若不先删除，重试会重复插入整套块。先按 DocumentId 删除已有块，使重试"替换"而非"叠加"。
            await _chunkRepository.DeleteAsync(c => c.DocumentId == documentId, ct);
            if (!_vectorStore.UsesRagChunkTable)
            {
                await _vectorStore.DeleteByDocumentAsync(documentId, ct);
            }

            // 6. 构建 DocumentChunk 实体并批量插入（ID 由框架自动生成）
            var chunkEntities = new List<DocumentChunk>();

            for (var i = 0; i < chunks.Count; i++)
            {
                chunkEntities.Add(new DocumentChunk
                {
                    KnowledgeBaseId = kbId,
                    DocumentId = documentId,
                    Content = chunks[i],
                    Embedding = new Vector(allEmbeddings[i]),
                    ChunkIndex = i,
                    Metadata = JsonSerializer.Serialize(new { source = fileName, chunkIndex = i })
                });
            }

            await _chunkRepository.InsertManyAsync(chunkEntities, ct);

            // 6b. 不读分块表的向量存储单独入库（见类注释），与分块表用同一个分块 Id
            if (!_vectorStore.UsesRagChunkTable)
            {
                await IndexInVectorStoreAsync(kbId, documentId, chunkEntities, allEmbeddings, ct);
            }

            // 7. GraphRAG 实体/关系抽取（默认关闭，opt-in via AI:Rag:GraphRag:Enabled）
            // 抽取失败不能影响文档摄取主流程：捕获并记录后继续。
            // 与分块同一语义：先清掉本文档上一次留下的图谱再抽（重试 / 重新摄取替换而非叠加），
            // 写下的每一行都标上来源文档，删除文档时才清得掉。清理失败就不抽：宁可这次没有图谱，也不叠一份重复的。
            if (_options.GraphRag.Enabled)
            {
                try
                {
                    await DocumentGraph.RemoveAsync(_graphEdgeRepository, _graphNodeRepository, documentId, ct);
                    var graphResult = await _graphExtractor.ExtractAsync(text, kbId, documentId, ct);
                    Logger.LogDebug(
                        "GraphRAG extraction for document {FileName} in KB {KbId}: {NodeCount} nodes, {EdgeCount} edges",
                        fileName, kbId, graphResult.Nodes.Count, graphResult.Edges.Count);
                }
                catch (Exception graphEx)
                {
                    // 静默降级：图谱抽取失败不影响已成功的向量摄取
                    Logger.LogWarning(graphEx,
                        "GraphRAG extraction failed for document {FileName} in KB {KbId}; vector ingestion already persisted, continuing",
                        fileName, kbId);
                }
            }

            // 8. 文档行还在吗？删除文档与摄取并发时，删除方在文档行删除提交之后才清分块与图谱；
            //    写在那次清理之后的由这里收尾（见类注释）。
            if (!await _docRepository.ExistsAsync(documentId, ct))
            {
                await RemoveArtifactsOfDeletedDocumentAsync(documentId);
                Logger.LogWarning(
                    "Document {DocumentId} ({FileName}) was deleted while it was being ingested; removed the chunks and graph written for it",
                    documentId, fileName);
                return IngestResult.DeletedDuringIngestion();
            }

            sw.Stop();
            RagActivitySource.RecordIngestion(kbId, chunks.Count, sw.Elapsed.TotalSeconds);
            activity?.SetStatus(ActivityStatusCode.Ok);

            Logger.LogInformation("Ingested {ChunkCount} chunks for document {FileName} in knowledge base {KbId} ({Duration:F2}s)",
                chunks.Count, fileName, kbId, sw.Elapsed.TotalSeconds);

            return IngestResult.Success(chunks.Count);
        }
        catch (Exception ex)
        {
            sw.Stop();
            RagActivitySource.RecordError("ingestion", ex);
            RagActivitySource.RecordActivityError(activity, ex);

            Logger.LogError(ex, "Document ingestion failed for {FileName} in knowledge base {KbId}", fileName, kbId);
            return IngestResult.Failure("Document ingestion failed due to an internal error.");
        }
    }

    /// <summary>
    /// 把刚写进分块表的分块逐块写进向量存储。先让框架生成分块 Id（事务内只是送进事务，无事务时插入已经落库、这里是空操作）。
    /// 写入失败（例如外部向量库没有实现 <see cref="IVectorStore.UpsertAsync"/>）时撤掉本文档刚写下的分块表行与已写入的条目再抛出，
    /// 文档按失败处理：留着它们，分块表上的关键词检索会命中一份失败文档的内容。
    /// </summary>
    private async Task IndexInVectorStoreAsync(
        Guid kbId, Guid documentId, List<DocumentChunk> chunkEntities, List<float[]> embeddings, CancellationToken ct)
    {
        await _chunkRepository.SaveChangesAsync(ct);

        try
        {
            for (var i = 0; i < chunkEntities.Count; i++)
            {
                var chunk = chunkEntities[i];
                await _vectorStore.UpsertAsync(
                    chunk.Id, embeddings[i], documentId, kbId, chunk.Content, chunk.ChunkIndex, chunk.Metadata, ct);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "Writing the chunks of document {DocumentId} to the vector store ({Store}) failed; removing what was written",
                documentId, _vectorStore.GetType().Name);
            await _chunkRepository.DeleteAsync(c => c.DocumentId == documentId, CancellationToken.None);
            await _vectorStore.DeleteByDocumentAsync(documentId, CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// 清掉为一份已被删除的文档写下的分块与图谱。用 <see cref="CancellationToken.None"/>：
    /// 走到这里说明无主的行已经落库，半途取消就把它们永久留下了。
    /// </summary>
    private async Task RemoveArtifactsOfDeletedDocumentAsync(Guid documentId)
    {
        await _chunkRepository.DeleteAsync(c => c.DocumentId == documentId, CancellationToken.None);
        if (!_vectorStore.UsesRagChunkTable)
        {
            await _vectorStore.DeleteByDocumentAsync(documentId, CancellationToken.None);
        }

        if (_options.GraphRag.Enabled)
        {
            await DocumentGraph.RemoveAsync(_graphEdgeRepository, _graphNodeRepository, documentId, CancellationToken.None);
        }
    }
}
