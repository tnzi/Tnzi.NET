using Tnzi.AI.Rag.Dtos;

namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// 知识库指定了另一家嵌入提供商时，嵌入模型必须显式给出。
/// </summary>
/// <remarks>
/// 模型留空会回退到 <c>AI:Rag:DefaultEmbeddingModel</c>，而那个模型名属于默认嵌入提供商：
/// 默认是 ollama / nomic-embed-text、建库时选 openai 不填模型，每次摄取都向 OpenAI 请求 nomic-embed-text，文档一律失败。
/// 嵌入字段创建后改不了，只能删库重建，所以在创建时拦下。
/// </remarks>
public class KnowledgeBaseEmbeddingModelTests
{
    private readonly Mock<IRepository<KnowledgeBase, Guid>> _kbRepo = new();

    public KnowledgeBaseEmbeddingModelTests()
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
    }

    [Fact]
    public async Task Create_AnotherProviderWithoutAModel_IsRejected()
    {
        var result = await CreateService(DefaultsFor("ollama", "nomic-embed-text"))
            .CreateAsync(new CreateKnowledgeBaseDto { Name = "kb", EmbeddingProvider = "openai" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("EmbeddingModel");
        _kbRepo.Verify(r => r.InsertAsync(It.IsAny<KnowledgeBase>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("openai", "text-embedding-3-small")] // another provider with its own model
    [InlineData("ollama", null)]                      // the default provider named explicitly
    [InlineData("OLLAMA", null)]
    [InlineData("default", null)]                     // the sentinel
    [InlineData(null, null)]
    public async Task Create_ProviderAndModelThatResolveConsistently_AreAccepted(string? provider, string? model)
    {
        var result = await CreateService(DefaultsFor("ollama", "nomic-embed-text"))
            .CreateAsync(new CreateKnowledgeBaseDto { Name = "kb", EmbeddingProvider = provider, EmbeddingModel = model });

        result.Succeeded.ShouldBeTrue(result.Message);
    }

    [Fact]
    public async Task Create_AnotherProviderWithoutAModel_IsAcceptedWhenNoDefaultModelIsConfigured()
    {
        // 没配默认模型时不会借用别家的模型名：模型由该提供商自己的默认值决定，与此前一致
        var result = await CreateService(new AIRagOptions { DefaultEmbeddingProvider = "ollama" })
            .CreateAsync(new CreateKnowledgeBaseDto { Name = "kb", EmbeddingProvider = "openai" });

        result.Succeeded.ShouldBeTrue(result.Message);
    }

    private static AIRagOptions DefaultsFor(string provider, string model)
        => new() { DefaultEmbeddingProvider = provider, DefaultEmbeddingModel = model };

    private KnowledgeBaseService CreateService(AIRagOptions options)
    {
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(sp => sp.GetService(typeof(ILoggerFactory))).Returns(NullLoggerFactory.Instance);

        return new KnowledgeBaseService(
            _kbRepo.Object,
            Mock.Of<IRepository<KnowledgeDocument, Guid>>(),
            Mock.Of<IRepository<DocumentChunk, Guid>>(),
            Mock.Of<IRepository<KnowledgeGraphNode, Guid>>(),
            Mock.Of<IRepository<KnowledgeGraphEdge, Guid>>(),
            Mock.Of<IDocumentIngestionService>(),
            Mock.Of<IVectorStore>(),
            Mock.Of<IEmbeddingService>(),
            Mock.Of<IReranker>(),
            new StaticOptionsMonitor<AIRagOptions>(options),
            serviceProvider.Object);
    }
}
