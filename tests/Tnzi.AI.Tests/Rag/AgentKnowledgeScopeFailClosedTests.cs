namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// agent 侧检索（<see cref="ITextSearchService"/> 的两个 RAG 实现）对「没有知识库范围」与「表达不了的过滤维度」失败关闭。
/// </summary>
/// <remarks>
/// ★ 此前两条实现都只读 <see cref="TextSearchFilter.KnowledgeBaseIds"/>：为空就走 search-all，其余维度整体忽略。
/// 于是无 agent 的对话、没有知识库授权的 agent、以及按用户 / 线程过滤的「聊天历史记忆」提供器，
/// 都拿到了全部启用知识库的原文分块（含刻意不对普通用户开放的库），且随 citations 原文返回给用户。
/// 这里的替身向量库对任何检索（含 search-all）都答一条分块：断言的是「根本没去查」，而不只是「结果为空」。
/// </remarks>
public class AgentKnowledgeScopeFailClosedTests
{
    private static readonly float[] QueryVector = [0.1f];
    private static readonly Guid GrantedKb = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly Mock<IEmbeddingService> _embedding = new();
    private readonly Mock<IVectorStore> _vectorStore = new();
    private readonly Mock<IKeywordSearchProvider> _keyword = new();
    private readonly Mock<IReranker> _reranker = new();
    private readonly Mock<IRepository<KnowledgeDocument, Guid>> _docRepo = new();
    private readonly Mock<IRepository<KnowledgeBase, Guid>> _kbRepo = new();

    public AgentKnowledgeScopeFailClosedTests()
    {
        _embedding
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success(QueryVector));
        _vectorStore
            .Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((float[] _, int _, Guid? kbId, CancellationToken _) =>
            [
                new VectorSearchResult
                {
                    Id = Guid.NewGuid(), Content = "internal chunk", DocumentId = Guid.NewGuid(),
                    KnowledgeBaseId = kbId ?? Guid.NewGuid(), Score = 0.9
                }
            ]);
        _keyword
            .Setup(k => k.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KeywordSearchResult>());
        _reranker
            .Setup(r => r.RerankAsync(It.IsAny<string>(), It.IsAny<List<VectorSearchResult>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, List<VectorSearchResult> r, int k, CancellationToken _) => r.Take(k).ToList());
        _docRepo.Setup(r => r.AsQueryable(It.IsAny<bool>()))
            .Returns(new List<KnowledgeDocument>().BuildMock());
        _kbRepo.Setup(r => r.AsQueryable(It.IsAny<bool>()))
            .Returns(new List<KnowledgeBase> { new() { Id = GrantedKb, Name = "granted", EmbeddingProvider = "default" } }.BuildMock());
    }

    public static TheoryData<string, TextSearchFilter?> UnscopedFilters() => new()
    {
        // 无 agent 的对话 / 没有知识库授权的 agent：TextSearchProvider 传 null
        { "null", null },
        { "empty knowledge base list", new TextSearchFilter { KnowledgeBaseIds = [] } },
        // ChatHistoryMemoryProvider 的过滤：只有用户 / agent / 线程 / 集合，没有知识库
        { "chat history scope", new TextSearchFilter { UserId = Guid.NewGuid(), AgentId = Guid.NewGuid(), ThreadId = "t-1" } },
        { "collection only", new TextSearchFilter { CollectionName = "chat-history" } }
    };

    public static TheoryData<string, TextSearchFilter> UnsupportedDimensionFilters() => new()
    {
        // 带了知识库范围，但同时要求本实现表达不了的维度：忽略它们 = 静默放宽调用方要的范围
        { "user", new TextSearchFilter { KnowledgeBaseIds = [GrantedKb], UserId = Guid.NewGuid() } },
        { "agent", new TextSearchFilter { KnowledgeBaseIds = [GrantedKb], AgentId = Guid.NewGuid() } },
        { "thread", new TextSearchFilter { KnowledgeBaseIds = [GrantedKb], ThreadId = "t-1" } },
        { "collection", new TextSearchFilter { KnowledgeBaseIds = [GrantedKb], CollectionName = "c" } },
        { "metadata", new TextSearchFilter { KnowledgeBaseIds = [GrantedKb], Metadata = new() { ["k"] = "v" } } }
    };

    [Theory]
    [MemberData(nameof(UnscopedFilters))]
    public async Task VectorTextSearch_WithoutKnowledgeBaseScope_ReturnsNothingAndNeverSearches(string _, TextSearchFilter? filter)
    {
        var results = await CreateVectorTextSearchService().SearchAsync("what is the HR policy?", filter);

        results.ShouldBeEmpty();
        VerifyNeverSearched();
    }

    [Theory]
    [MemberData(nameof(UnsupportedDimensionFilters))]
    public async Task VectorTextSearch_WithUnsupportedFilterDimension_ReturnsNothingAndNeverSearches(string _, TextSearchFilter filter)
    {
        var results = await CreateVectorTextSearchService().SearchAsync("what is the HR policy?", filter);

        results.ShouldBeEmpty();
        VerifyNeverSearched();
    }

    [Fact]
    public async Task VectorTextSearch_FilterlessOverload_ReturnsNothingAndNeverSearches()
    {
        var results = await CreateVectorTextSearchService().SearchAsync("what is the HR policy?");

        results.ShouldBeEmpty();
        VerifyNeverSearched();
    }

    [Fact]
    public async Task VectorTextSearch_WithKnowledgeBaseScope_SearchesOnlyThoseKnowledgeBases()
    {
        // 正控制：有范围时照常检索，且只查这些库
        var kbId = GrantedKb;

        var results = (await CreateVectorTextSearchService()
            .SearchAsync("q", new TextSearchFilter { KnowledgeBaseIds = [kbId] })).ToList();

        results.ShouldHaveSingleItem().Text.ShouldBe("internal chunk");
        _vectorStore.Verify(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), kbId, It.IsAny<CancellationToken>()), Times.Once);
        _vectorStore.Verify(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), null, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [MemberData(nameof(UnscopedFilters))]
    public async Task HybridSearch_WithoutKnowledgeBaseScope_ReturnsNothingAndNeverSearches(string _, TextSearchFilter? filter)
    {
        var results = await CreateHybridSearchService().SearchAsync("what is the HR policy?", filter);

        results.ShouldBeEmpty();
        VerifyNeverSearched();
        _keyword.Verify(
            k => k.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [MemberData(nameof(UnsupportedDimensionFilters))]
    public async Task HybridSearch_WithUnsupportedFilterDimension_ReturnsNothingAndNeverSearches(string _, TextSearchFilter filter)
    {
        var results = await CreateHybridSearchService().SearchAsync("what is the HR policy?", filter);

        results.ShouldBeEmpty();
        VerifyNeverSearched();
    }

    [Fact]
    public async Task HybridSearch_FilterlessOverload_ReturnsNothingAndNeverSearches()
    {
        var results = await CreateHybridSearchService().SearchAsync("what is the HR policy?");

        results.ShouldBeEmpty();
        VerifyNeverSearched();
    }

    [Fact]
    public async Task HybridSearch_WithKnowledgeBaseScope_SearchesOnlyThoseKnowledgeBases()
    {
        var kbId = GrantedKb;

        var results = (await CreateHybridSearchService()
            .SearchAsync("q", new TextSearchFilter { KnowledgeBaseIds = [kbId] })).ToList();

        results.ShouldHaveSingleItem().Text.ShouldBe("internal chunk");
        _vectorStore.Verify(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), kbId, It.IsAny<CancellationToken>()), Times.Once);
        _vectorStore.Verify(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), null, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TextSearchProvider_ForAnAgentWithoutKnowledgeGrants_InjectsNoContextAndNoCitations()
    {
        // 端到端：核心的提供器在无授权时传 null，RAG 实现必须据此什么都不给
        var provider = new TextSearchProvider(
            CreateVectorTextSearchService(),
            new TextSearchOptions(),
            NullLogger<TextSearchProvider>.Instance,
            knowledgeBaseIds: null);

        var injection = await provider.GetContextAsync([new ChatMessage(ChatRole.User, "what is the HR policy?")]);

        injection.HasContent.ShouldBeFalse();
        (injection.Citations ?? []).ShouldBeEmpty();
        VerifyNeverSearched();
    }

    [Fact]
    public async Task ChatHistoryMemoryProvider_BackedByRag_InjectsNothing()
    {
        // RAG 不存聊天历史：按用户 / 线程过滤的「聊天历史」检索不能变成一次跨库知识检索
        var provider = new ChatHistoryMemoryProvider(
            CreateVectorTextSearchService(),
            new ChatHistoryMemoryOptions(),
            new ChatHistoryMemoryScope { UserId = Guid.NewGuid().ToString(), ThreadId = "t-1" },
            NullLogger<ChatHistoryMemoryProvider>.Instance);

        var injection = await provider.GetContextAsync([new ChatMessage(ChatRole.User, "what is the HR policy?")]);

        injection.HasContent.ShouldBeFalse();
        VerifyNeverSearched();
    }

    private void VerifyNeverSearched()
        => _vectorStore.Verify(
            v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);

    private static IServiceProvider LoggingServiceProvider()
    {
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(sp => sp.GetService(typeof(ILoggerFactory))).Returns(NullLoggerFactory.Instance);
        return serviceProvider.Object;
    }

    private static StaticOptionsMonitor<AIRagOptions> RagOptions() => new(new AIRagOptions
    {
        DefaultEmbeddingProvider = "openai",
        DefaultEmbeddingModel = "text-embedding-3-small"
    });

    private VectorTextSearchService CreateVectorTextSearchService()
    {
        var retriever = new RagRetriever(
            LoggingServiceProvider(),
            _embedding.Object,
            _vectorStore.Object,
            _reranker.Object,
            _kbRepo.Object,
            [],
            RagOptions());

        return new VectorTextSearchService(LoggingServiceProvider(), retriever, _docRepo.Object);
    }

    private HybridSearchService CreateHybridSearchService()
        => new(
            LoggingServiceProvider(),
            _vectorStore.Object,
            _keyword.Object,
            _embedding.Object,
            _reranker.Object,
            _kbRepo.Object,
            _docRepo.Object,
            Mock.Of<IRepository<DocumentChunk, Guid>>(),
            RagOptions());
}
