using Tnzi.AI.Rag.Dtos;

namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// 用户直连的 <c>/api/rag/query|chat|chat/stream</c> 的 <c>TopK</c> 必须 clamp 到 <c>AIRagOptions.MaxTopK</c>。
/// </summary>
/// <remarks>
/// ★ 此前 <c>MaxTopK</c> 只在管理端搜索（<c>KnowledgeBaseService</c>）里 clamp；用户端两个引擎把
/// <c>request.TopK</c> 原样递给检索器，检索器原样递给向量库的 <c>LIMIT</c> —— 一个持一个
/// <c>IsUserQueryable</c> 知识库的登录用户，一次 <c>topK=2000000, minRelevance=0</c> 就把整库分块
/// 拉进进程、拼进一条 prompt（账单 / 上下文超限），<c>/chat</c> 还会把它写进线程历史。
/// </remarks>
public class RagTopKClampTests
{
    private const int MaxTopK = 7;
    private static readonly Guid AllowedKb = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly Mock<IRagRetriever> _retriever = new();
    private readonly Mock<IAiUtility> _aiUtility = new();
    private readonly Mock<IAgentRuntime> _agentRuntime = new();
    private readonly Mock<IRagAccessAuthorizer> _authorizer = new();
    private readonly IServiceProvider _serviceProvider;
    private readonly List<int> _capturedTopK = [];

    public RagTopKClampTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();

        _retriever
            .Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<RagRetrievalOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, RagRetrievalOptions?, CancellationToken>((_, o, _) => _capturedTopK.Add(o!.TopK))
            .ReturnsAsync([new RetrievalResult { Content = "chunk", Score = 0.9 }]);
        _aiUtility
            .Setup(u => u.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("answer");
        _agentRuntime
            .Setup(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult { Response = "answer", ThreadId = Guid.NewGuid() });
        _agentRuntime
            .Setup(r => r.RunStreamingAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Empty());
        _authorizer
            .Setup(a => a.AuthorizeQueryAsync(It.IsAny<IReadOnlyList<Guid>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Guid>>.Success([AllowedKb]));
    }

    private static async IAsyncEnumerable<AgentStreamChunk> Empty()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static StaticOptionsMonitor<AIRagOptions> Options() => new(new AIRagOptions { MaxTopK = MaxTopK });

    private RagQueryEngine CreateQueryEngine()
        => new(_serviceProvider, _retriever.Object, _aiUtility.Object, _authorizer.Object, Options());

    private RagChatEngine CreateChatEngine()
        => new(_serviceProvider, _retriever.Object, _agentRuntime.Object, _authorizer.Object, Options());

    [Theory]
    [InlineData(int.MaxValue, MaxTopK)]
    [InlineData(MaxTopK + 1, MaxTopK)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(3, 3)]
    public async Task RagQueryEngine_TopK_IsClampedToMaxTopK(int requested, int expected)
    {
        var result = await CreateQueryEngine().QueryAsync(new RagQueryRequest { Query = "q", TopK = requested });

        result.Succeeded.ShouldBeTrue();
        _capturedTopK.ShouldBe([expected]);
    }

    [Theory]
    [InlineData(int.MaxValue, MaxTopK)]
    [InlineData(0, 1)]
    public async Task RagChatEngine_TopK_IsClamped(int requested, int expected)
    {
        var result = await CreateChatEngine().ChatAsync(new RagChatRequest { Query = "q", TopK = requested });

        result.Succeeded.ShouldBeTrue();
        _capturedTopK.ShouldBe([expected]);
    }

    [Fact]
    public async Task RagChatEngine_Streaming_TopK_IsClamped()
    {
        await foreach (var _ in CreateChatEngine().ChatStreamingAsync(new RagChatRequest { Query = "q", TopK = int.MaxValue }))
        {
        }

        _capturedTopK.ShouldBe([MaxTopK]);
    }
}

/// <summary>检索器自身也 clamp（纵深防御：别的调用方绕过引擎直接用它时同样受限）。</summary>
public class RagRetrieverTopKClampTests
{
    [Fact]
    public async Task RagRetriever_TopK_IsClampedBeforeReachingTheVectorStore()
    {
        var embedding = new Mock<IEmbeddingService>();
        embedding
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success([0.1f]));
        var capturedTopK = new List<int>();
        var vectorStore = new Mock<IVectorStore>();
        vectorStore
            .Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .Callback<float[], int, Guid?, CancellationToken>((_, k, _, _) => capturedTopK.Add(k))
            .ReturnsAsync(new List<VectorSearchResult>());
        var reranker = new Mock<IReranker>();
        reranker
            .Setup(r => r.RerankAsync(It.IsAny<string>(), It.IsAny<List<VectorSearchResult>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<string, List<VectorSearchResult>, int, CancellationToken>((_, _, k, _) => capturedTopK.Add(k))
            .ReturnsAsync((string _, List<VectorSearchResult> r, int k, CancellationToken _) => r.Take(k).ToList());
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(sp => sp.GetService(typeof(ILoggerFactory))).Returns(NullLoggerFactory.Instance);

        var retriever = new RagRetriever(
            serviceProvider.Object,
            embedding.Object,
            vectorStore.Object,
            reranker.Object,
            new Mock<IRepository<KnowledgeBase, Guid>>().Object,
            [],
            new StaticOptionsMonitor<AIRagOptions>(new AIRagOptions { MaxTopK = 4 }));

        var options = new RagRetrievalOptions { TopK = 1_000_000 };
        await retriever.RetrieveAsync("query", options);

        capturedTopK.ShouldAllBe(k => k == 4);
        capturedTopK.Count.ShouldBe(2);
        options.TopK.ShouldBe(1_000_000, "the caller's options object must not be mutated");
    }
}
