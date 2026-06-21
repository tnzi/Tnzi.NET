namespace Tnzi.AI.Tests;

/// <summary>
/// SemanticChunkingStrategy 单元测试 — 验证基于嵌入相似度的语义分块
/// </summary>
public class SemanticChunkingStrategyTests
{
    private readonly Mock<IEmbeddingService> _embeddingServiceMock = new();
    private readonly Mock<ILogger<SemanticChunkingStrategy>> _loggerMock = new();

    /// <summary>
    /// 辅助方法：创建指定方向的归一化向量
    /// </summary>
    private static float[] MakeVector(float x, float y, float z)
    {
        var norm = MathF.Sqrt(x * x + y * y + z * z);
        if (norm == 0) return [0f, 0f, 0f];
        return [x / norm, y / norm, z / norm];
    }

    [Fact]
    public async Task ChunkAsync_SplitsAtSimilarityDrop()
    {
        // 构造 4 个句子：前 2 个语义相似（向量接近），后 2 个语义相似但与前 2 个不同
        var text = "Cats are great pets. Dogs are wonderful companions. " +
                   "Python is a programming language. JavaScript runs in browsers.";

        // 模拟嵌入：句子 1,2 指向 (1,0,0)，句子 3,4 指向 (0,1,0)
        var embeddings = new List<float[]>
        {
            MakeVector(1f, 0f, 0f),     // "Cats are great pets."
            MakeVector(0.95f, 0.1f, 0f), // "Dogs are wonderful companions." (与 1 相似)
            MakeVector(0.1f, 0.95f, 0f), // "Python is a programming language." (与 2 不相似)
            MakeVector(0f, 1f, 0f)       // "JavaScript runs in browsers." (与 3 相似)
        };

        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<float[]>>.Success(embeddings));

        var strategy = new SemanticChunkingStrategy(
            _embeddingServiceMock.Object, _loggerMock.Object,
            similarityThreshold: 0.5, minChunkSize: 10);

        // chunkSize 必须小于文本总长度，否则直接返回整段文本
        var chunks = await strategy.ChunkAsync(text, chunkSize: 100, chunkOverlap: 0);

        // 应该分成 2 个块：前 2 句和后 2 句（语义边界在句子 2 和 3 之间）
        chunks.Count.ShouldBe(2);
        chunks[0].ShouldContain("Cats");
        chunks[0].ShouldContain("Dogs");
        chunks[1].ShouldContain("Python");
        chunks[1].ShouldContain("JavaScript");
    }

    [Fact]
    public async Task ChunkAsync_MergesSmallChunks()
    {
        // 5 个句子：每对之间相似度都低于阈值 → 理论上 5 个块
        // 但 minChunkSize=200 会导致小块合并
        var text = "A. B. C. D. E.";

        var embeddings = new List<float[]>
        {
            MakeVector(1f, 0f, 0f),
            MakeVector(0f, 1f, 0f),
            MakeVector(0f, 0f, 1f),
            MakeVector(-1f, 0f, 0f),
            MakeVector(0f, -1f, 0f)
        };

        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<float[]>>.Success(embeddings));

        var strategy = new SemanticChunkingStrategy(
            _embeddingServiceMock.Object, _loggerMock.Object,
            similarityThreshold: 0.5, minChunkSize: 200);

        var chunks = await strategy.ChunkAsync(text, chunkSize: 500, chunkOverlap: 0);

        // 所有块都很短（< minChunkSize），应该被合并为 1 个块
        chunks.Count.ShouldBe(1);
        chunks[0].ShouldContain("A.");
        chunks[0].ShouldContain("E.");
    }

    [Fact]
    public async Task ChunkAsync_SplitsLargeChunks()
    {
        // 2 个句子属于同一语义组，但总长度超过 chunkSize → 必须用 FixedSize 回退拆分
        var longSentenceA = "Word " + string.Join(" word ", Enumerable.Range(1, 50).Select(i => i.ToString())) + ".";
        var longSentenceB = "Another " + string.Join(" text ", Enumerable.Range(1, 50).Select(i => i.ToString())) + ".";
        var text = longSentenceA + " " + longSentenceB;

        var embeddings = new List<float[]>
        {
            MakeVector(1f, 0f, 0f),
            MakeVector(0.99f, 0.1f, 0f) // 非常相似，不会在此断开
        };

        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<float[]>>.Success(embeddings));

        var strategy = new SemanticChunkingStrategy(
            _embeddingServiceMock.Object, _loggerMock.Object,
            similarityThreshold: 0.5, minChunkSize: 10);

        // chunkSize 设为 100，合并后的块远超 100，应该被回退拆分
        var chunks = await strategy.ChunkAsync(text, chunkSize: 100, chunkOverlap: 0);

        chunks.Count.ShouldBeGreaterThan(1);
        foreach (var chunk in chunks)
        {
            chunk.Length.ShouldBeLessThanOrEqualTo(100);
        }
    }

    [Fact]
    public async Task ChunkAsync_SingleSentence_ReturnsOneChunk()
    {
        var text = "Just a single short sentence.";

        // 文本短于 chunkSize，直接返回
        var chunks = await new SemanticChunkingStrategy(
                _embeddingServiceMock.Object, _loggerMock.Object)
            .ChunkAsync(text, chunkSize: 500, chunkOverlap: 0);

        chunks.Count.ShouldBe(1);
        chunks[0].ShouldBe(text);
    }

    [Fact]
    public async Task ChunkAsync_EmptyText_ReturnsEmpty()
    {
        var strategy = new SemanticChunkingStrategy(
            _embeddingServiceMock.Object, _loggerMock.Object);

        (await strategy.ChunkAsync("", 500, 0)).ShouldBeEmpty();
        (await strategy.ChunkAsync("   ", 500, 0)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ChunkAsync_EmbeddingFailure_FallsBackToFixedSize()
    {
        var text = "First sentence. Second sentence. Third sentence. Fourth sentence. Fifth sentence.";

        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<float[]>>.Failure("Embedding service unavailable"));

        var strategy = new SemanticChunkingStrategy(
            _embeddingServiceMock.Object, _loggerMock.Object,
            similarityThreshold: 0.5, minChunkSize: 10);

        // 嵌入失败应回退到固定大小分块，不应抛异常
        var chunks = await strategy.ChunkAsync(text, chunkSize: 40, chunkOverlap: 0);

        chunks.ShouldNotBeEmpty();
        chunks.Count.ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task ChunkAsync_WithRagOptions_AlignsEmbeddingProviderAndModel()
    {
        // 提供 ragOptions 时，句子嵌入须按 RAG 默认 provider/model 对齐（向量空间一致性）
        var text = "Cats are great pets. Python is a programming language.";

        EmbeddingOptions? capturedOptions = null;
        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<List<string>, EmbeddingOptions?, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(Result<List<float[]>>.Success(new List<float[]>
            {
                MakeVector(1f, 0f, 0f),
                MakeVector(0f, 1f, 0f)
            }));

        var ragOptions = Microsoft.Extensions.Options.Options.Create(new AIRagOptions
        {
            DefaultEmbeddingProvider = "rag-embed-provider",
            DefaultEmbeddingModel = "rag-embed-model"
        });

        var strategy = new SemanticChunkingStrategy(
            _embeddingServiceMock.Object, _loggerMock.Object,
            similarityThreshold: 0.5, minChunkSize: 10, ragOptions: ragOptions);

        await strategy.ChunkAsync(text, chunkSize: 30, chunkOverlap: 0);

        capturedOptions.ShouldNotBeNull();
        capturedOptions!.Provider.ShouldBe("rag-embed-provider");
        capturedOptions.Model.ShouldBe("rag-embed-model");
    }

    [Fact]
    public async Task ChunkAsync_WithoutRagOptions_PassesNullEmbeddingOptions()
    {
        // 不提供 ragOptions 时回退到 null（AI 顶层默认 provider），保持向后兼容
        var text = "Cats are great pets. Python is a programming language.";

        EmbeddingOptions? capturedOptions = new EmbeddingOptions { Provider = "sentinel" };
        _embeddingServiceMock
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<List<string>, EmbeddingOptions?, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(Result<List<float[]>>.Success(new List<float[]>
            {
                MakeVector(1f, 0f, 0f),
                MakeVector(0f, 1f, 0f)
            }));

        var strategy = new SemanticChunkingStrategy(
            _embeddingServiceMock.Object, _loggerMock.Object,
            similarityThreshold: 0.5, minChunkSize: 10);

        await strategy.ChunkAsync(text, chunkSize: 30, chunkOverlap: 0);

        capturedOptions.ShouldBeNull();
    }

    [Fact]
    public void SplitIntoSentences_SplitsCorrectly()
    {
        var text = "Hello world. How are you? I'm fine! Great\nNew line here.";

        var sentences = SemanticChunkingStrategy.SplitIntoSentences(text);

        sentences.Count.ShouldBe(5);
        sentences[0].ShouldBe("Hello world.");
        sentences[1].ShouldBe("How are you?");
        sentences[2].ShouldBe("I'm fine!");
        sentences[3].ShouldBe("Great");
        sentences[4].ShouldBe("New line here.");
    }

    [Fact]
    public void SplitIntoSentences_TrailingText_IncludesRemainder()
    {
        var text = "First sentence. Trailing text without period";

        var sentences = SemanticChunkingStrategy.SplitIntoSentences(text);

        sentences.Count.ShouldBe(2);
        sentences[0].ShouldBe("First sentence.");
        sentences[1].ShouldBe("Trailing text without period");
    }
}
