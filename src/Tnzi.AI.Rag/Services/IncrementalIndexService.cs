namespace Tnzi.AI.Rag.Services;

/// <summary>
/// 增量索引服务 — 比较新旧内容段落级别差异，仅对变更部分重新切块和嵌入
/// </summary>
public class IncrementalIndexService : ApplicationService, IIncrementalIndexService
{
    /// <summary>
    /// 段落变更超过此比例时回退为全量重建
    /// </summary>
    private const double FullReindexThreshold = 0.5;

    private readonly IRepository<KnowledgeDocument, Guid> _docRepository;
    private readonly IRepository<DocumentChunk, Guid> _chunkRepository;
    private readonly IRepository<KnowledgeBase, Guid> _kbRepository;
    private readonly IChunkingStrategy _chunkingStrategy;
    private readonly IEmbeddingService _embeddingService;
    private readonly AIRagOptions _options;

    public IncrementalIndexService(
        IRepository<KnowledgeDocument, Guid> docRepository,
        IRepository<DocumentChunk, Guid> chunkRepository,
        IRepository<KnowledgeBase, Guid> kbRepository,
        IChunkingStrategy chunkingStrategy,
        IEmbeddingService embeddingService,
        IOptions<AIRagOptions> options,
        IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _docRepository = Check.NotNull(docRepository);
        _chunkRepository = Check.NotNull(chunkRepository);
        _kbRepository = Check.NotNull(kbRepository);
        _chunkingStrategy = Check.NotNull(chunkingStrategy);
        _embeddingService = Check.NotNull(embeddingService);
        _options = Check.NotNull(options).Value;
    }

    /// <inheritdoc />
    public async Task<IncrementalIndexResult> ReindexAsync(Guid documentId, string newContent, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(newContent);

        // 1. 加载文档
        var doc = await _docRepository.GetAsync(documentId, ct);
        if (doc == null)
        {
            return IncrementalIndexResult.Failure($"Document not found: {documentId}");
        }

        // 2. 加载知识库配置（用于 chunking 和 embedding 参数）
        var kb = await _kbRepository.AsQueryable()
            .FirstOrDefaultAsync(e => e.Id == doc.KnowledgeBaseId, ct);
        if (kb == null)
        {
            return IncrementalIndexResult.Failure($"Knowledge base not found: {doc.KnowledgeBaseId}");
        }

        // 3. 计算新内容哈希
        var newContentHash = ComputeContentHash(newContent);

        // 若内容未变更，直接返回
        if (doc.ContentHash == newContentHash)
        {
            var existingCount = await _chunkRepository.AsQueryable()
                .CountAsync(c => c.DocumentId == documentId, ct);
            return IncrementalIndexResult.Success(0, 0, existingCount, newContentHash);
        }

        // 4. 加载现有块
        var existingChunks = await _chunkRepository.AsQueryable()
            .Where(c => c.DocumentId == documentId)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync(ct);

        // 5. 将新旧内容拆分为段落
        var oldParagraphs = ExtractParagraphs(existingChunks);
        var newParagraphs = SplitParagraphs(newContent);

        // 6. 计算每个段落的哈希，进行比较
        var oldHashes = oldParagraphs.Select(ComputeContentHash).ToHashSet();
        var newHashes = newParagraphs.Select(ComputeContentHash).ToList();

        var unchangedCount = newHashes.Count(h => oldHashes.Contains(h));
        var totalParagraphs = Math.Max(oldParagraphs.Count, newParagraphs.Count);
        var changedRatio = totalParagraphs > 0
            ? 1.0 - (double)unchangedCount / totalParagraphs
            : 1.0;

        // 7. 若变更超过 50%，回退为全量重建
        if (changedRatio > FullReindexThreshold)
        {
            return await FullReindexAsync(doc, kb, newContent, newContentHash, existingChunks, ct);
        }

        // 8. 增量索引：仅处理变更段落
        return await IncrementalReindexAsync(doc, kb, newContent, newContentHash, existingChunks, oldHashes, newParagraphs, newHashes, ct);
    }

    /// <summary>
    /// 全量重建 — 删除所有旧块，重新切块和嵌入
    /// </summary>
    private async Task<IncrementalIndexResult> FullReindexAsync(
        KnowledgeDocument doc,
        KnowledgeBase kb,
        string newContent,
        string newContentHash,
        List<DocumentChunk> existingChunks,
        CancellationToken ct)
    {
        var removedCount = existingChunks.Count;

        // 删除所有旧块
        if (existingChunks.Count > 0)
        {
            await _chunkRepository.DeleteAsync(c => c.DocumentId == doc.Id, ct);
        }

        // 重新切块
        var chunkSize = kb.ChunkSize > 0 ? kb.ChunkSize : _options.DefaultChunkSize;
        var chunkOverlap = kb.ChunkOverlap >= 0 ? kb.ChunkOverlap : _options.DefaultChunkOverlap;
        var chunks = _chunkingStrategy.Chunk(newContent, chunkSize, chunkOverlap);

        if (chunks.Count == 0)
        {
            await UpdateDocumentMetadataAsync(doc, newContentHash, 0, ct);
            return IncrementalIndexResult.Success(0, removedCount, 0, newContentHash, fullReindex: true);
        }

        // 生成嵌入
        var embeddings = await GenerateEmbeddingsAsync(kb, chunks, ct);
        if (embeddings == null)
        {
            return IncrementalIndexResult.Failure("Embedding generation failed during full reindex");
        }

        // 创建新块
        var newChunkEntities = BuildChunkEntities(doc, chunks, embeddings);
        await _chunkRepository.InsertManyAsync(newChunkEntities);

        // 更新文档元数据
        await UpdateDocumentMetadataAsync(doc, newContentHash, chunks.Count, ct);

        Logger.LogInformation(
            "Full reindex for document {DocId}: removed {Removed}, added {Added} chunks",
            doc.Id, removedCount, chunks.Count);

        return IncrementalIndexResult.Success(chunks.Count, removedCount, 0, newContentHash, fullReindex: true);
    }

    /// <summary>
    /// 增量重建 — 仅处理变更段落
    /// </summary>
    private async Task<IncrementalIndexResult> IncrementalReindexAsync(
        KnowledgeDocument doc,
        KnowledgeBase kb,
        string newContent,
        string newContentHash,
        List<DocumentChunk> existingChunks,
        HashSet<string> oldHashes,
        List<string> newParagraphs,
        List<string> newHashes,
        CancellationToken ct)
    {
        // 识别变更段落（新增的、未在旧哈希集合中的）
        var changedParagraphTexts = new List<string>();
        for (var i = 0; i < newParagraphs.Count; i++)
        {
            if (!oldHashes.Contains(newHashes[i]))
            {
                changedParagraphTexts.Add(newParagraphs[i]);
            }
        }

        // 识别已删除的段落对应的块（旧块内容哈希不在新哈希集合中的）
        var newHashSet = newHashes.ToHashSet();
        var chunksToRemove = existingChunks
            .Where(c => !newHashSet.Contains(ComputeContentHash(c.Content)))
            .ToList();

        // 删除过期块
        var removedCount = chunksToRemove.Count;
        if (chunksToRemove.Count > 0)
        {
            await _chunkRepository.DeleteManyAsync(chunksToRemove, ct);
        }

        // 对变更段落重新切块
        var addedCount = 0;
        if (changedParagraphTexts.Count > 0)
        {
            var changedText = string.Join("\n\n", changedParagraphTexts);
            var chunkSize = kb.ChunkSize > 0 ? kb.ChunkSize : _options.DefaultChunkSize;
            var chunkOverlap = kb.ChunkOverlap >= 0 ? kb.ChunkOverlap : _options.DefaultChunkOverlap;
            var newChunks = _chunkingStrategy.Chunk(changedText, chunkSize, chunkOverlap);

            if (newChunks.Count > 0)
            {
                // 生成嵌入
                var embeddings = await GenerateEmbeddingsAsync(kb, newChunks, ct);
                if (embeddings == null)
                {
                    return IncrementalIndexResult.Failure("Embedding generation failed during incremental reindex");
                }

                // 计算新块的起始索引
                var maxExistingIndex = existingChunks.Count > 0
                    ? existingChunks.Max(c => c.ChunkIndex)
                    : -1;
                var startIndex = maxExistingIndex + 1;

                var newChunkEntities = BuildChunkEntities(doc, newChunks, embeddings, startIndex);
                await _chunkRepository.InsertManyAsync(newChunkEntities);
                addedCount = newChunks.Count;
            }
        }

        // 计算未变更块数
        var unchangedCount = existingChunks.Count - removedCount;

        // 更新文档元数据
        var totalChunks = unchangedCount + addedCount;
        await UpdateDocumentMetadataAsync(doc, newContentHash, totalChunks, ct);

        Logger.LogInformation(
            "Incremental reindex for document {DocId}: added {Added}, removed {Removed}, unchanged {Unchanged} chunks",
            doc.Id, addedCount, removedCount, unchangedCount);

        return IncrementalIndexResult.Success(addedCount, removedCount, unchangedCount, newContentHash);
    }

    /// <summary>
    /// 批量生成嵌入
    /// </summary>
    private async Task<List<float[]>?> GenerateEmbeddingsAsync(KnowledgeBase kb, List<string> chunks, CancellationToken ct)
    {
        var embeddingOptions = new EmbeddingOptions
        {
            Provider = kb.EmbeddingProvider == "default" ? _options.DefaultEmbeddingProvider : kb.EmbeddingProvider,
            Model = kb.EmbeddingModel ?? _options.DefaultEmbeddingModel
        };

        var allEmbeddings = new List<float[]>();
        var batchSize = _options.EmbeddingBatchSize;

        for (var i = 0; i < chunks.Count; i += batchSize)
        {
            var batch = chunks.Skip(i).Take(batchSize).ToList();
            var embeddingResult = await _embeddingService.GenerateEmbeddingsAsync(batch, embeddingOptions, ct);

            if (!embeddingResult.Succeeded)
            {
                Logger.LogError("Embedding generation failed: {Message}", embeddingResult.Message);
                return null;
            }

            allEmbeddings.AddRange(embeddingResult.Data!);
        }

        return allEmbeddings;
    }

    /// <summary>
    /// 构建 DocumentChunk 实体列表
    /// </summary>
    private static List<DocumentChunk> BuildChunkEntities(
        KnowledgeDocument doc, List<string> chunks, List<float[]> embeddings, int startIndex = 0)
    {
        var entities = new List<DocumentChunk>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            entities.Add(new DocumentChunk
            {
                KnowledgeBaseId = doc.KnowledgeBaseId,
                DocumentId = doc.Id,
                Content = chunks[i],
                Embedding = new Vector(embeddings[i]),
                ChunkIndex = startIndex + i,
                Metadata = JsonSerializer.Serialize(new { source = doc.FileName, chunkIndex = startIndex + i })
            });
        }

        return entities;
    }

    /// <summary>
    /// 更新文档元数据（版本号、内容哈希、块数）
    /// </summary>
    private async Task UpdateDocumentMetadataAsync(KnowledgeDocument doc, string newContentHash, int chunkCount, CancellationToken ct)
    {
        await _docRepository.UpdateAsync(doc.Id, d =>
        {
            d.ContentHash = newContentHash;
            d.Version = doc.Version + 1;
            d.ChunkCount = chunkCount;
            d.Status = DocumentStatus.Completed;
        }, ct);
    }

    /// <summary>
    /// 从现有块中提取段落 — 将所有块内容拼接后用相同的段落拆分方式处理，
    /// 确保与新内容的 SplitParagraphs 粒度一致，避免 hash 不匹配。
    /// </summary>
    private static List<string> ExtractParagraphs(List<DocumentChunk> chunks)
    {
        if (chunks.Count == 0)
            return [];

        // 拼接所有块内容，然后用相同的段落拆分逻辑处理
        var fullText = string.Join("\n\n", chunks.Select(c => c.Content));
        return SplitParagraphs(fullText);
    }

    /// <summary>
    /// 将文本按双换行符拆分为段落
    /// </summary>
    private static List<string> SplitParagraphs(string text)
    {
        return text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
    }

    /// <summary>
    /// 计算文本的 SHA256 哈希
    /// </summary>
    private static string ComputeContentHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hashBytes);
    }
}
