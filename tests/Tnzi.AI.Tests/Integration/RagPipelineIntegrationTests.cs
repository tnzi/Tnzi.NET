using Tnzi.AI.Rag.Dtos;
using Tnzi.AI.Rag.FileExtractors;
using Tnzi.AI.Rag.Metadata;

namespace Tnzi.AI.Tests.Integration;

/// <summary>
/// RAG 管道端到端集成测试 — 验证 Ingest→Chunk→Embed→Store→Search 完整流程
/// </summary>
/// <remarks>
/// <para>使用 InMemoryVectorStore + 真实 FixedSizeChunkingStrategy + 确定性 Mock Embedding。</para>
/// <para>
/// 由于 DocumentIngestionService 通过 EF Core Repository 写入 DocumentChunk，
/// 而 InMemoryVectorStore 维护独立的内存存储，测试通过拦截 Repository 的 InsertManyAsync
/// 将 chunk 同步写入 InMemoryVectorStore，模拟 PgVectorStore 的真实行为。
/// </para>
/// </remarks>
public class RagPipelineIntegrationTests : IDisposable
{
    // 嵌入维度（小维度足够用于测试余弦相似度）
    private const int EmbeddingDimensions = 8;

    private readonly InMemoryVectorStore _vectorStore;
    private readonly FixedSizeChunkingStrategy _chunkingStrategy;
    private readonly Mock<IEmbeddingService> _embeddingServiceMock;
    private readonly Mock<IRepository<DocumentChunk, Guid>> _chunkRepoMock;
    private readonly Mock<IRepository<KnowledgeBase, Guid>> _kbRepoMock;
    private readonly Mock<IRepository<KnowledgeDocument, Guid>> _docRepoMock;
    private readonly Mock<IReranker> _rerankerMock;
    private readonly AIRagOptions _ragOptions;

    // 真实服务实例
    private readonly DocumentIngestionService _ingestionService;
    private readonly KnowledgeBaseService _knowledgeBaseService;

    // 追踪已插入的 chunk（用于验证和同步到 InMemoryVectorStore）
    private readonly List<DocumentChunk> _insertedChunks = [];

    // 追踪已插入的 KnowledgeDocument
    private readonly List<KnowledgeDocument> _storedDocuments = [];

    // 追踪已创建的 KnowledgeBase
    private readonly List<KnowledgeBase> _storedKnowledgeBases = [];

    public RagPipelineIntegrationTests()
    {
        _vectorStore = new InMemoryVectorStore(NullLogger<InMemoryVectorStore>.Instance);
        _chunkingStrategy = new FixedSizeChunkingStrategy();
        _embeddingServiceMock = new Mock<IEmbeddingService>();
        _chunkRepoMock = new Mock<IRepository<DocumentChunk, Guid>>();
        _kbRepoMock = new Mock<IRepository<KnowledgeBase, Guid>>();
        _docRepoMock = new Mock<IRepository<KnowledgeDocument, Guid>>();
        _rerankerMock = new Mock<IReranker>();

        _ragOptions = new AIRagOptions
        {
            DefaultChunkSize = 200,
            DefaultChunkOverlap = 20,
            EmbeddingBatchSize = 100,
            DefaultEmbeddingProvider = "openai",
            DefaultEmbeddingModel = "text-embedding-3-small",
            MaxTopK = 100
        };

        // 配置 Mock Embedding — 基于文本内容生成确定性嵌入
        SetupDeterministicEmbeddings();

        // 配置 Reranker — 直通（NoOp 行为）
        _rerankerMock
            .Setup(r => r.RerankAsync(It.IsAny<string>(), It.IsAny<List<VectorSearchResult>>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, List<VectorSearchResult> results, int topK, CancellationToken _) =>
                results.Take(topK).ToList());

        // 配置 chunk repository — 拦截 InsertManyAsync 并同步到 InMemoryVectorStore
        SetupChunkRepository();

        // 配置 doc repository
        SetupDocRepository();

        // 构建服务
        var serviceProvider = CreateMockServiceProvider();
        var options = Microsoft.Extensions.Options.Options.Create(_ragOptions);

        var plainTextExtractor = new PlainTextFileExtractor();

        _ingestionService = new DocumentIngestionService(
            [plainTextExtractor],
            _chunkingStrategy,
            _embeddingServiceMock.Object,
            _chunkRepoMock.Object,
            _kbRepoMock.Object,
            options,
            serviceProvider);

        _knowledgeBaseService = new KnowledgeBaseService(
            _kbRepoMock.Object,
            _docRepoMock.Object,
            _chunkRepoMock.Object,
            _ingestionService,
            _vectorStore,
            _embeddingServiceMock.Object,
            _rerankerMock.Object,
            options,
            serviceProvider);
    }

    #region Test 1: Ingest→Chunk→Search

    [Fact]
    public async Task IngestThenSearch_ReturnsRelevantChunks()
    {
        // Arrange — 创建知识库并摄取一个文档
        var kbId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        SetupKnowledgeBase(kbId, "Test KB");

        var content = GenerateDocument("machine learning",
            "Machine learning is a subset of artificial intelligence. " +
            "It allows computers to learn from data without being explicitly programmed. " +
            "Deep learning is a branch of machine learning using neural networks. " +
            "Training requires large datasets and significant compute resources.");

        // Act — 摄取文档
        var ingestResult = await _ingestionService.IngestAsync(
            kbId, docId, CreateStream(content), "ml-guide.txt");

        // Assert — 摄取成功，产生了 chunk
        ingestResult.Succeeded.ShouldBeTrue();
        ingestResult.ChunkCount.ShouldBeGreaterThan(0);

        // 同步 chunk 到 vector store
        await SyncChunksToVectorStoreAsync();

        // 搜索
        var searchResult = await _knowledgeBaseService.SearchAsync(
            kbId, "machine learning neural networks", topK: 3);

        searchResult.Succeeded.ShouldBeTrue();
        searchResult.Data.ShouldNotBeNull();
        searchResult.Data.Count.ShouldBeGreaterThan(0);

        // 搜索结果应包含与机器学习相关的内容
        searchResult.Data.ShouldContain(r => r.Content!.Contains("machine learning", StringComparison.OrdinalIgnoreCase)
            || r.Content!.Contains("neural network", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Test 2: Multiple Documents

    [Fact]
    public async Task MultipleDocuments_SearchFindsResultsFromBoth()
    {
        // Arrange — 两个文档在同一个知识库
        var kbId = Guid.NewGuid();
        SetupKnowledgeBase(kbId, "Multi-doc KB");

        var doc1Id = Guid.NewGuid();
        var doc1Content = GenerateDocument("cooking",
            "Italian pasta recipes are popular worldwide. " +
            "Spaghetti carbonara uses eggs, cheese, and pancetta. " +
            "Pizza originated in Naples, Italy.");

        var doc2Id = Guid.NewGuid();
        var doc2Content = GenerateDocument("astronomy",
            "The solar system has eight planets orbiting the sun. " +
            "Jupiter is the largest planet in our solar system. " +
            "Stars produce energy through nuclear fusion.");

        // Act — 摄取两个文档
        var result1 = await _ingestionService.IngestAsync(
            kbId, doc1Id, CreateStream(doc1Content), "cooking.txt");
        var result2 = await _ingestionService.IngestAsync(
            kbId, doc2Id, CreateStream(doc2Content), "astronomy.txt");

        result1.Succeeded.ShouldBeTrue();
        result2.Succeeded.ShouldBeTrue();

        await SyncChunksToVectorStoreAsync();

        // 搜索应同时返回两个文档的 chunk
        var searchResult = await _knowledgeBaseService.SearchAsync(
            kbId, "Italian food and planets in space", topK: 10);

        searchResult.Succeeded.ShouldBeTrue();
        var results = searchResult.Data!;
        results.Count.ShouldBeGreaterThan(0);

        // 验证结果来自两个不同的文档（通过 DocumentId 去重判断）
        var uniqueDocIds = _insertedChunks
            .Where(c => results.Any(r => r.Content == c.Content))
            .Select(c => c.DocumentId)
            .Distinct()
            .ToList();
        uniqueDocIds.Count.ShouldBeGreaterThanOrEqualTo(2);
    }

    #endregion

    #region Test 3: Deduplication

    [Fact]
    public async Task DuplicateDocument_NoAdditionalChunksCreated()
    {
        // Arrange
        var kbId = Guid.NewGuid();
        SetupKnowledgeBase(kbId, "Dedup KB");

        var content = "This is a test document for deduplication verification. " +
                      "It contains enough text to be meaningful for chunking purposes.";
        var contentBytes = System.Text.Encoding.UTF8.GetBytes(content);

        // 第一次上传 — 注册文档并摄取
        var doc1Id = Guid.NewGuid();
        var doc1 = new KnowledgeDocument
        {
            Id = doc1Id,
            KnowledgeBaseId = kbId,
            FileName = "dedup-test.txt",
            Status = DocumentStatus.Completed,
            ContentHash = ComputeHash(contentBytes),
            ChunkCount = 1
        };
        _storedDocuments.Add(doc1);
        UpdateDocRepoQueryable();

        // Act — 尝试第二次上传相同内容
        var stream = new MemoryStream(contentBytes);
        var uploadResult = await _knowledgeBaseService.UploadDocumentAsync(
            kbId, stream, "dedup-test-copy.txt", "text/plain", contentBytes.Length);

        // Assert — 返回成功但标记为重复
        uploadResult.Succeeded.ShouldBeTrue();
        uploadResult.Data.ShouldNotBeNull();
        uploadResult.Data!.IsDuplicate.ShouldBeTrue();
        uploadResult.Data.DocumentId.ShouldBe(doc1Id);
    }

    #endregion

    #region Test 4: Chunk Metadata

    [Fact]
    public async Task ChunkMetadata_HasCorrectParentDocumentAndPosition()
    {
        // Arrange
        var kbId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        SetupKnowledgeBase(kbId, "Metadata KB");

        // 生成足够长的文本以产生多个 chunk（chunkSize=200）
        var content = string.Join(" ",
            Enumerable.Range(0, 50).Select(i =>
                $"Sentence number {i} provides additional content for chunking. " +
                $"This ensures we have enough text to create multiple chunks."));

        // Act
        var result = await _ingestionService.IngestAsync(
            kbId, docId, CreateStream(content), "metadata-test.txt");

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.ChunkCount.ShouldBeGreaterThan(1);

        // 验证所有 chunk 的元数据
        var docChunks = _insertedChunks.Where(c => c.DocumentId == docId).ToList();
        docChunks.Count.ShouldBe(result.ChunkCount);

        for (var i = 0; i < docChunks.Count; i++)
        {
            var chunk = docChunks[i];

            // 父文档引用正确
            chunk.DocumentId.ShouldBe(docId);
            chunk.KnowledgeBaseId.ShouldBe(kbId);

            // 位置索引正确（从 0 递增）
            chunk.ChunkIndex.ShouldBe(i);

            // 内容非空
            chunk.Content.ShouldNotBeNullOrWhiteSpace();

            // 嵌入向量非空
            chunk.Embedding.ShouldNotBeNull();

            // Metadata JSON 包含 source 和 chunkIndex
            chunk.Metadata.ShouldNotBeNullOrWhiteSpace();
            var metadata = JsonDocument.Parse(chunk.Metadata!);
            metadata.RootElement.GetProperty("source").GetString().ShouldBe("metadata-test.txt");
            metadata.RootElement.GetProperty("chunkIndex").GetInt32().ShouldBe(i);
        }
    }

    #endregion

    #region Test 5: Search Relevance

    [Fact]
    public async Task SearchRelevance_TopicAQuery_ReturnsPrimarilyTopicAChunks()
    {
        // Arrange — 两个话题截然不同的文档
        var kbId = Guid.NewGuid();
        SetupKnowledgeBase(kbId, "Relevance KB");

        // 文档 A：关于编程
        var docAId = Guid.NewGuid();
        var docAContent = GenerateDocument("programming",
            "Python is a versatile programming language used in web development and data science. " +
            "JavaScript runs in web browsers and enables interactive web applications. " +
            "Rust provides memory safety guarantees without garbage collection. " +
            "TypeScript adds static typing to JavaScript for better tooling.");

        // 文档 B：关于音乐
        var docBId = Guid.NewGuid();
        var docBContent = GenerateDocument("music",
            "Classical music includes works by Mozart, Beethoven, and Bach. " +
            "Jazz originated in New Orleans with improvisation as a key element. " +
            "Rock and roll emerged in the 1950s blending blues and country music. " +
            "Electronic music uses synthesizers and digital production techniques.");

        var resultA = await _ingestionService.IngestAsync(
            kbId, docAId, CreateStream(docAContent), "programming.txt");
        var resultB = await _ingestionService.IngestAsync(
            kbId, docBId, CreateStream(docBContent), "music.txt");

        resultA.Succeeded.ShouldBeTrue();
        resultB.Succeeded.ShouldBeTrue();

        await SyncChunksToVectorStoreAsync();

        // Act — 搜索编程相关内容
        var searchResult = await _knowledgeBaseService.SearchAsync(
            kbId, "programming language Python JavaScript", topK: 3);

        // Assert
        searchResult.Succeeded.ShouldBeTrue();
        var results = searchResult.Data!;
        results.Count.ShouldBeGreaterThan(0);

        // 最相关的结果应来自编程文档
        var topResult = results.First();
        var topChunk = _insertedChunks.FirstOrDefault(c => c.Content == topResult.Content);
        topChunk.ShouldNotBeNull();
        topChunk!.DocumentId.ShouldBe(docAId);

        // 编程文档的 chunk 相关性分数应高于音乐文档
        var progResults = results.Where(r =>
            _insertedChunks.Any(c => c.Content == r.Content && c.DocumentId == docAId));
        var musicResults = results.Where(r =>
            _insertedChunks.Any(c => c.Content == r.Content && c.DocumentId == docBId));

        if (progResults.Any() && musicResults.Any())
        {
            var avgProgScore = progResults.Average(r => r.Score);
            var avgMusicScore = musicResults.Average(r => r.Score);
            avgProgScore.ShouldBeGreaterThan(avgMusicScore);
        }
    }

    #endregion

    #region Test 6: Chunking Behavior

    [Fact]
    public async Task Ingest_RespectsKnowledgeBaseChunkSettings()
    {
        // Arrange — 知识库配置自定义 chunk 大小
        var kbId = Guid.NewGuid();
        SetupKnowledgeBase(kbId, "Custom Chunk KB", chunkSize: 100, chunkOverlap: 10);

        // 300 字符的文本应产生 3+ 个 chunk（chunkSize=100）
        var content = "A" + new string('a', 99) + ". " +
                      "B" + new string('b', 99) + ". " +
                      "C" + new string('c', 99) + ".";

        // Act
        var result = await _ingestionService.IngestAsync(
            kbId, Guid.NewGuid(), CreateStream(content), "custom-chunk.txt");

        // Assert — 至少产生 2 个 chunk
        result.Succeeded.ShouldBeTrue();
        result.ChunkCount.ShouldBeGreaterThanOrEqualTo(2);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// 配置确定性 Embedding — 基于文本内容的简单哈希生成固定维度向量，
    /// 确保语义相似的文本产生相似的向量
    /// </summary>
    private void SetupDeterministicEmbeddings()
    {
        // 定义关键词→向量方向映射（模拟语义空间）
        var topicVectors = new Dictionary<string, float[]>
        {
            ["programming"] = CreateTopicVector(0),
            ["python"] = CreateTopicVector(0),
            ["javascript"] = CreateTopicVector(0),
            ["language"] = CreateTopicVector(0),
            ["typescript"] = CreateTopicVector(0),
            ["rust"] = CreateTopicVector(0),
            ["code"] = CreateTopicVector(0),
            ["music"] = CreateTopicVector(1),
            ["classical"] = CreateTopicVector(1),
            ["jazz"] = CreateTopicVector(1),
            ["rock"] = CreateTopicVector(1),
            ["synthesizer"] = CreateTopicVector(1),
            ["machine"] = CreateTopicVector(2),
            ["learning"] = CreateTopicVector(2),
            ["neural"] = CreateTopicVector(2),
            ["deep"] = CreateTopicVector(2),
            ["data"] = CreateTopicVector(2),
            ["cooking"] = CreateTopicVector(3),
            ["pasta"] = CreateTopicVector(3),
            ["pizza"] = CreateTopicVector(3),
            ["italian"] = CreateTopicVector(3),
            ["food"] = CreateTopicVector(3),
            ["astronomy"] = CreateTopicVector(4),
            ["planet"] = CreateTopicVector(4),
            ["solar"] = CreateTopicVector(4),
            ["star"] = CreateTopicVector(4),
            ["jupiter"] = CreateTopicVector(4),
            ["space"] = CreateTopicVector(4)
        };

        // 单条文本嵌入
        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, EmbeddingOptions? _, CancellationToken _) =>
                Result<float[]>.Success(ComputeTopicEmbedding(text, topicVectors)));

        // 批量文本嵌入
        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string> texts, EmbeddingOptions? _, CancellationToken _) =>
                Result<List<float[]>>.Success(
                    texts.Select(t => ComputeTopicEmbedding(t, topicVectors)).ToList()));
    }

    /// <summary>
    /// 为第 topicIndex 个话题创建一个单位向量方向
    /// </summary>
    private static float[] CreateTopicVector(int topicIndex)
    {
        var v = new float[EmbeddingDimensions];
        // 主方向 + 少量噪声使不同话题有不同的基底
        v[topicIndex % EmbeddingDimensions] = 1.0f;
        // 加一点噪声让向量更真实
        v[(topicIndex + 3) % EmbeddingDimensions] = 0.1f;
        return v;
    }

    /// <summary>
    /// 基于文本中的关键词计算复合嵌入向量（加权平均），模拟语义嵌入
    /// </summary>
    private static float[] ComputeTopicEmbedding(string text, Dictionary<string, float[]> topicVectors)
    {
        var result = new float[EmbeddingDimensions];
        var lowerText = text.ToLower();
        var matchCount = 0;

        foreach (var (keyword, vector) in topicVectors)
        {
            if (!lowerText.Contains(keyword)) continue;

            for (var i = 0; i < EmbeddingDimensions; i++)
            {
                result[i] += vector[i];
            }
            matchCount++;
        }

        if (matchCount == 0)
        {
            // 没有匹配关键词时，返回基于哈希的随机向量
            var hash = text.GetHashCode();
            for (var i = 0; i < EmbeddingDimensions; i++)
            {
                result[i] = (float)((hash >> (i * 4) & 0xF) / 15.0 - 0.5);
            }
        }

        // 归一化
        var norm = (float)Math.Sqrt(result.Sum(x => x * x));
        if (norm > 0)
        {
            for (var i = 0; i < EmbeddingDimensions; i++)
            {
                result[i] /= norm;
            }
        }

        return result;
    }

    /// <summary>
    /// 配置 chunk repository — 拦截 InsertManyAsync 并记录所有插入的 chunk
    /// </summary>
    private void SetupChunkRepository()
    {
        _chunkRepoMock
            .Setup(r => r.InsertManyAsync(It.IsAny<IEnumerable<DocumentChunk>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<DocumentChunk>, CancellationToken>((chunks, _) =>
            {
                foreach (var chunk in chunks)
                {
                    // 模拟框架自动生成 ID
                    if (chunk.Id == Guid.Empty)
                    {
                        // 使用反射设置 ID（因为 EntityBase<Guid>.Id 可能有 init setter）
                        typeof(DocumentChunk).GetProperty("Id")!.SetValue(chunk, Guid.NewGuid());
                    }
                    _insertedChunks.Add(chunk);
                }
            })
            .Returns(Task.CompletedTask);
    }

    /// <summary>
    /// 将 Repository 中记录的 chunk 同步到 InMemoryVectorStore
    /// </summary>
    private async Task SyncChunksToVectorStoreAsync()
    {
        foreach (var chunk in _insertedChunks)
        {
            await _vectorStore.UpsertAsync(
                chunk.Id,
                chunk.Embedding.ToArray(),
                chunk.DocumentId,
                chunk.KnowledgeBaseId,
                chunk.Content,
                chunk.ChunkIndex,
                chunk.Metadata);
        }
    }

    /// <summary>
    /// 配置 doc repository — 支持文档查询和插入
    /// </summary>
    private void SetupDocRepository()
    {
        // 初始空查询
        UpdateDocRepoQueryable();

        _docRepoMock
            .Setup(r => r.InsertAsync(It.IsAny<KnowledgeDocument>(), It.IsAny<CancellationToken>()))
            .Callback<KnowledgeDocument, CancellationToken>((doc, _) =>
            {
                if (doc.Id == Guid.Empty)
                {
                    typeof(KnowledgeDocument).GetProperty("Id")!.SetValue(doc, Guid.NewGuid());
                }
                _storedDocuments.Add(doc);
                UpdateDocRepoQueryable();
            })
            .Returns(Task.CompletedTask);

        _docRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<KnowledgeDocument>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void UpdateDocRepoQueryable()
    {
        _docRepoMock
            .Setup(r => r.AsQueryable(It.IsAny<bool>()))
            .Returns(() => _storedDocuments.BuildMock());
    }

    /// <summary>
    /// 注册知识库到 mock repository
    /// </summary>
    private void SetupKnowledgeBase(Guid kbId, string name,
        int chunkSize = 0, int chunkOverlap = 0)
    {
        var kb = new KnowledgeBase
        {
            Id = kbId,
            Name = name,
            ChunkSize = chunkSize > 0 ? chunkSize : _ragOptions.DefaultChunkSize,
            ChunkOverlap = chunkOverlap > 0 ? chunkOverlap : _ragOptions.DefaultChunkOverlap,
            EmbeddingProvider = "default",
            EmbeddingModel = null,
            IsEnabled = true
        };
        _storedKnowledgeBases.Add(kb);

        _kbRepoMock
            .Setup(r => r.AsQueryable(It.IsAny<bool>()))
            .Returns(() => _storedKnowledgeBases.BuildMock());

        _kbRepoMock
            .Setup(r => r.GetAsync(kbId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(kb);
    }

    private static IServiceProvider CreateMockServiceProvider()
    {
        var mock = new Mock<IServiceProvider>();
        mock.Setup(sp => sp.GetService(typeof(ILoggerFactory)))
            .Returns(NullLoggerFactory.Instance);
        return mock.Object;
    }

    /// <summary>
    /// 生成带话题标识的文档文本
    /// </summary>
    private static string GenerateDocument(string topic, string content)
    {
        return $"Document about {topic}. {content}";
    }

    private static MemoryStream CreateStream(string text)
    {
        return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
    }

    private static string ComputeHash(byte[] content)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(content);
        return Convert.ToHexString(hash).ToLower();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    #endregion
}
