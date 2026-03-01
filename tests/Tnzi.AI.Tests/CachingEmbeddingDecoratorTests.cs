using Tnzi.Caching;

namespace Tnzi.AI.Tests;

/// <summary>
/// CachingEmbeddingDecorator 单元测试 — 验证 Embedding 缓存命中和未命中行为
/// </summary>
public class CachingEmbeddingDecoratorTests
{
    private readonly Mock<IEmbeddingService> _innerMock;
    private readonly Mock<ICache> _cacheMock;
    private readonly Mock<ILogger<CachingEmbeddingDecorator>> _loggerMock;
    private readonly IOptions<AIRagOptions> _options;

    public CachingEmbeddingDecoratorTests()
    {
        _innerMock = new Mock<IEmbeddingService>();
        _cacheMock = new Mock<ICache>();
        _loggerMock = new Mock<ILogger<CachingEmbeddingDecorator>>();
        _options = Microsoft.Extensions.Options.Options.Create(new AIRagOptions
        {
            EmbeddingCache = new EmbeddingCacheOptions
            {
                Enabled = true,
                TtlHours = 24
            }
        });
    }

    [Fact]
    public async Task GenerateEmbedding_CacheHit_ReturnsCachedResult()
    {
        var cached = new float[] { 0.1f, 0.2f, 0.3f };
        _cacheMock.Setup(c => c.GetAsync<float[]>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached);

        var decorator = new CachingEmbeddingDecorator(_innerMock.Object, _loggerMock.Object, _options, _cacheMock.Object);

        var result = await decorator.GenerateEmbeddingAsync("test text");

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(cached);
        _innerMock.Verify(s => s.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GenerateEmbedding_CacheMiss_CallsInnerAndCaches()
    {
        var embedding = new float[] { 0.4f, 0.5f, 0.6f };
        _cacheMock.Setup(c => c.GetAsync<float[]>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((float[]?)null);
        _innerMock.Setup(s => s.GenerateEmbeddingAsync("test text", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success(embedding));

        var decorator = new CachingEmbeddingDecorator(_innerMock.Object, _loggerMock.Object, _options, _cacheMock.Object);

        var result = await decorator.GenerateEmbeddingAsync("test text");

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(embedding);
        _innerMock.Verify(s => s.GenerateEmbeddingAsync("test text", null, It.IsAny<CancellationToken>()), Times.Once);
        _cacheMock.Verify(c => c.SetAsync(It.IsAny<string>(), embedding, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateEmbedding_NoCacheService_BypassesCache()
    {
        var embedding = new float[] { 0.7f, 0.8f, 0.9f };
        _innerMock.Setup(s => s.GenerateEmbeddingAsync("test", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success(embedding));

        var decorator = new CachingEmbeddingDecorator(_innerMock.Object, _loggerMock.Object, _options, cache: null);

        var result = await decorator.GenerateEmbeddingAsync("test");

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(embedding);
        _innerMock.Verify(s => s.GenerateEmbeddingAsync("test", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateEmbedding_CacheDisabled_BypassesCache()
    {
        var disabledOptions = Microsoft.Extensions.Options.Options.Create(new AIRagOptions
        {
            EmbeddingCache = new EmbeddingCacheOptions { Enabled = false }
        });

        var embedding = new float[] { 0.1f, 0.2f };
        _innerMock.Setup(s => s.GenerateEmbeddingAsync("test", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success(embedding));

        var decorator = new CachingEmbeddingDecorator(_innerMock.Object, _loggerMock.Object, disabledOptions, _cacheMock.Object);

        var result = await decorator.GenerateEmbeddingAsync("test");

        result.Succeeded.ShouldBeTrue();
        _cacheMock.Verify(c => c.GetAsync<float[]>(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GenerateEmbeddings_Batch_PartialCacheHit()
    {
        var texts = new List<string> { "text1", "text2", "text3" };
        var cached1 = new float[] { 1.0f };
        var generated2 = new float[] { 2.0f };
        var generated3 = new float[] { 3.0f };

        // text1 cached, text2 and text3 not cached
        _cacheMock.SetupSequence(c => c.GetAsync<float[]>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached1)
            .ReturnsAsync((float[]?)null)
            .ReturnsAsync((float[]?)null);

        _innerMock.Setup(s => s.GenerateEmbeddingsAsync(
                It.Is<List<string>>(l => l.Count == 2),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<float[]>>.Success(new List<float[]> { generated2, generated3 }));

        var decorator = new CachingEmbeddingDecorator(_innerMock.Object, _loggerMock.Object, _options, _cacheMock.Object);

        var result = await decorator.GenerateEmbeddingsAsync(texts);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Count.ShouldBe(3);
        result.Data[0].ShouldBe(cached1);
        result.Data[1].ShouldBe(generated2);
        result.Data[2].ShouldBe(generated3);
    }

    [Fact]
    public async Task GenerateEmbeddings_AllCached_DoesNotCallInner()
    {
        var texts = new List<string> { "text1", "text2" };
        var cached1 = new float[] { 1.0f };
        var cached2 = new float[] { 2.0f };

        _cacheMock.SetupSequence(c => c.GetAsync<float[]>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached1)
            .ReturnsAsync(cached2);

        var decorator = new CachingEmbeddingDecorator(_innerMock.Object, _loggerMock.Object, _options, _cacheMock.Object);

        var result = await decorator.GenerateEmbeddingsAsync(texts);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Count.ShouldBe(2);
        _innerMock.Verify(s => s.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<EmbeddingOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
