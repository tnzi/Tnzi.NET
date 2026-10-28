namespace Tnzi.AI.Tests.Services;

/// <summary>
/// <see cref="EmbeddingOptions.Dimensions"/> 与 <see cref="EmbeddingOptions.Parameters"/> 此前不传给生成器：
/// 为了对上 vector(1536) 列而设的维度被静默忽略，拿回的是模型的全尺寸向量。
/// </summary>
public class EmbeddingServiceOptionsTests
{
    [Fact]
    public async Task GenerateEmbedding_PassesDimensionsAndParametersToTheGenerator()
    {
        var (service, captured) = CreateService();

        var result = await service.GenerateEmbeddingAsync("text", new EmbeddingOptions
        {
            Dimensions = 256,
            Parameters = new Dictionary<string, object> { ["encoding_format"] = "float" }
        });

        result.Succeeded.ShouldBeTrue();
        var options = captured.ShouldHaveSingleItem().ShouldNotBeNull();
        options.Dimensions.ShouldBe(256);
        options.AdditionalProperties.ShouldNotBeNull()["encoding_format"].ShouldBe("float");
    }

    [Fact]
    public async Task GenerateEmbeddings_PassesDimensionsToTheGenerator()
    {
        var (service, captured) = CreateService();

        await service.GenerateEmbeddingsAsync(["a", "b"], new EmbeddingOptions { Dimensions = 512 });

        captured.ShouldHaveSingleItem().ShouldNotBeNull().Dimensions.ShouldBe(512);
    }

    [Fact]
    public async Task GenerateEmbedding_WithoutDimensionsOrParameters_LeavesTheGeneratorDefaults()
    {
        var (service, captured) = CreateService();

        await service.GenerateEmbeddingAsync("text", new EmbeddingOptions { Model = "text-embedding-3-small" });

        captured.ShouldHaveSingleItem().ShouldBeNull();
    }

    private static (EmbeddingService Service, List<EmbeddingGenerationOptions?> Captured) CreateService()
    {
        var captured = new List<EmbeddingGenerationOptions?>();
        var generator = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        generator.Setup(g => g.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<string>, EmbeddingGenerationOptions?, CancellationToken>((_, options, _) => captured.Add(options))
            .ReturnsAsync((IEnumerable<string> values, EmbeddingGenerationOptions? _, CancellationToken _) =>
                new GeneratedEmbeddings<Embedding<float>>(values.Select(_ => new Embedding<float>(new float[] { 0.1f, 0.2f }))));
        var factory = new Mock<IChatClientFactory>();
        factory.Setup(f => f.GetEmbeddingGenerator(It.IsAny<string?>(), It.IsAny<string?>())).Returns(generator.Object);

        var services = new ServiceCollection();
        services.AddLogging();
        var service = new EmbeddingService(factory.Object, services.BuildServiceProvider());
        return (service, captured);
    }
}
