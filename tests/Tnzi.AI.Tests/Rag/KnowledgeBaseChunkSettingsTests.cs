using Tnzi.AI.Rag.Dtos;

namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// 知识库的切块参数在创建时就按与全局默认值同一条规则校验，切块器自己也保证每一步都有实质推进。
/// </summary>
/// <remarks>
/// ★ 此前知识库级 <c>ChunkSize</c> / <c>ChunkOverlap</c> 原样入库：只填 <c>ChunkSize=100</c>（重叠取默认 128）的库，
/// 切块器每次只前进 1 个字符，50KB 文本切出约 5 万个几乎完全重叠的块，再付 5 万次嵌入调用、写 5 万行向量；
/// 这两个值创建后改不了，只能删库重建。
/// </remarks>
public class KnowledgeBaseChunkSettingsTests
{
    private readonly Mock<IRepository<KnowledgeBase, Guid>> _kbRepo = new();

    public KnowledgeBaseChunkSettingsTests()
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
    }

    [Theory]
    [InlineData(100, null)]   // 只填大小，默认重叠 128 比它还大
    [InlineData(1000, 600)]   // 重叠超过半个块
    [InlineData(1000, 1000)]
    [InlineData(1000, -1)]
    [InlineData(100, 0)]      // 小于下限
    [InlineData(500_000, 0)]  // 大于上限：单块超出嵌入模型的输入上限，这个库的每次上传都会失败
    public async Task Create_RejectsChunkSettingsThatCannotChunkSensibly(int chunkSize, int? chunkOverlap)
    {
        var result = await CreateService().CreateAsync(new CreateKnowledgeBaseDto
        {
            Name = "kb",
            ChunkSize = chunkSize,
            ChunkOverlap = chunkOverlap
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        _kbRepo.Verify(r => r.InsertAsync(It.IsAny<KnowledgeBase>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_RejectsAnOverlapThatOnlyTheDefaultMakesInvalid_AndSaysSo()
    {
        var result = await CreateService().CreateAsync(new CreateKnowledgeBaseDto { Name = "kb", ChunkSize = 100 });

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("ChunkOverlap");
        result.Message.ShouldContain("128");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(1000, 200)]
    [InlineData(1000, 500)]
    [InlineData(128, 0)]
    public async Task Create_AcceptsSensibleChunkSettings(int? chunkSize, int? chunkOverlap)
    {
        KnowledgeBase? saved = null;
        _kbRepo.Setup(r => r.InsertAsync(It.IsAny<KnowledgeBase>(), It.IsAny<CancellationToken>()))
            .Callback((KnowledgeBase kb, CancellationToken _) => saved = kb)
            .Returns(Task.CompletedTask);

        var result = await CreateService().CreateAsync(new CreateKnowledgeBaseDto
        {
            Name = "kb",
            ChunkSize = chunkSize,
            ChunkOverlap = chunkOverlap
        });

        result.Succeeded.ShouldBeTrue();
        saved.ShouldNotBeNull();
        saved.ChunkSize.ShouldBe(chunkSize ?? 1024);
        saved.ChunkOverlap.ShouldBe(chunkOverlap ?? 128);
    }

    [Fact]
    public void GlobalDefaults_FollowTheSameRule()
    {
        var validator = new AIRagOptionsValidator();

        validator.Validate(null, new AIRagOptions { DefaultChunkSize = 1000, DefaultChunkOverlap = 900 }).Failed.ShouldBeTrue();
        validator.Validate(null, new AIRagOptions { DefaultChunkSize = 1000, DefaultChunkOverlap = 500 }).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void FixedSizeChunking_OverlapAtLeastTheChunkSize_StillAdvancesByAWholeStep()
    {
        // 绕过校验的调用方（自己 new 切块器、或配置里早已存在的库）也不能把切块退化成逐字符前进
        var text = string.Concat(Enumerable.Repeat("All work and no play makes a dull day. ", 1300));

        var chunks = new FixedSizeChunkingStrategy().Chunk(text, chunkSize: 100, chunkOverlap: 128);

        // 每一步至少推进 0.3 个块（句子边界最早落在 0.8 处，重叠至多算半个块）
        chunks.Count.ShouldBeLessThanOrEqualTo(text.Length / 30 + 1);
        chunks.ShouldAllBe(c => c.Length <= 100);
        string.Concat(chunks).ShouldContain("dull day");
    }

    [Fact]
    public void FixedSizeChunking_SensibleOverlap_IsUnchanged()
    {
        var text = string.Concat(Enumerable.Range(0, 40).Select(i => $"Sentence number {i} is here. "));

        var chunks = new FixedSizeChunkingStrategy().Chunk(text, chunkSize: 200, chunkOverlap: 50);

        // 相邻块共享前一块末尾的重叠文本
        for (var i = 1; i < chunks.Count; i++)
        {
            var tail = chunks[i - 1][^20..];
            chunks[i].ShouldContain(tail.Trim()[..10]);
        }
    }

    private KnowledgeBaseService CreateService()
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
            new StaticOptionsMonitor<AIRagOptions>(new AIRagOptions { DefaultChunkSize = 1024, DefaultChunkOverlap = 128 }),
            serviceProvider.Object);
    }
}
