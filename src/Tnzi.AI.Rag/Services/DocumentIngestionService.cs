namespace Tnzi.AI.Rag.Services;

/// <summary>
/// 文档摄取服务 — 提取文本 → 切块 → 生成嵌入 → 存储到 pgvector
/// </summary>
public class DocumentIngestionService : ApplicationService, IDocumentIngestionService
{
    private readonly IEnumerable<IFileExtractorService> _extractors;
    private readonly IChunkingStrategy _chunkingStrategy;
    private readonly IAsyncChunkingStrategy? _asyncChunkingStrategy;
    private readonly IEmbeddingService _embeddingService;
    private readonly IRepository<DocumentChunk, Guid> _chunkRepository;
    private readonly IRepository<KnowledgeBase, Guid> _kbRepository;
    private readonly AIRagOptions _options;

    public DocumentIngestionService(
        IEnumerable<IFileExtractorService> extractors,
        IChunkingStrategy chunkingStrategy,
        IEmbeddingService embeddingService,
        IRepository<DocumentChunk, Guid> chunkRepository,
        IRepository<KnowledgeBase, Guid> kbRepository,
        IOptions<AIRagOptions> options,
        IServiceProvider serviceProvider,
        IAsyncChunkingStrategy? asyncChunkingStrategy = null) : base(serviceProvider)
    {
        _extractors = Check.NotNull(extractors);
        _chunkingStrategy = Check.NotNull(chunkingStrategy);
        _asyncChunkingStrategy = asyncChunkingStrategy;
        _embeddingService = Check.NotNull(embeddingService);
        _chunkRepository = Check.NotNull(chunkRepository);
        _kbRepository = Check.NotNull(kbRepository);
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

            // 4. 批量生成嵌入
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
                    return IngestResult.Failure($"Embedding generation failed: {embeddingResult.Message}");
                }

                allEmbeddings.AddRange(embeddingResult.Data!);

                Logger.LogDebug("Generated embeddings for batch {Batch}/{Total}",
                    Math.Min(i + batchSize, chunks.Count), chunks.Count);
            }

            // 5. 构建 DocumentChunk 实体并批量插入（ID 由框架自动生成）
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

            await _chunkRepository.InsertManyAsync(chunkEntities);

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
}
