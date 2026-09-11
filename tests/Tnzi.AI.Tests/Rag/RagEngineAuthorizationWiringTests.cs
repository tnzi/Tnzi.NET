using Tnzi.AI.Rag.Dtos;

namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// 授权判定确实<b>接在</b>用户直连的两个引擎上，而不只是存在于某个契约里。
/// </summary>
/// <remarks>
/// 判定必须落在服务层：控制器可以被消费方整体替换，挂在其上的守卫会随之消失。
/// agent 侧检索直接用 <c>IRagRetriever</c>，不经过这两个引擎，因此不受影响 —— 这也是
/// 这些用例断言"检索用的是授权后的 id 列表"而不是"检索被拦住了"的原因。
/// </remarks>
public class RagEngineAuthorizationWiringTests
{
    private static readonly Guid AllowedKb = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly Mock<IRagRetriever> _retriever = new();
    private readonly Mock<IAiUtility> _aiUtility = new();
    private readonly Mock<IAgentRuntime> _agentRuntime = new();
    private readonly Mock<IRagAccessAuthorizer> _authorizer = new();
    private readonly IServiceProvider _serviceProvider;

    public RagEngineAuthorizationWiringTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();

        _retriever
            .Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<RagRetrievalOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RetrievalResult { Content = "chunk", Score = 0.9 }]);

        _aiUtility
            .Setup(u => u.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("answer");

        _agentRuntime
            .Setup(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult { Response = "answer", ThreadId = Guid.NewGuid() });
    }

    private RagQueryEngine CreateQueryEngine()
        => new(_serviceProvider, _retriever.Object, _aiUtility.Object, _authorizer.Object);

    private RagChatEngine CreateChatEngine()
        => new(_serviceProvider, _retriever.Object, _agentRuntime.Object, _authorizer.Object);

    private void Authorize(params Guid[] ids)
        => _authorizer
            .Setup(a => a.AuthorizeQueryAsync(It.IsAny<IReadOnlyList<Guid>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Guid>>.Success(ids));

    private void Deny()
        => _authorizer
            .Setup(a => a.AuthorizeQueryAsync(It.IsAny<IReadOnlyList<Guid>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Guid>>.Failure("Access denied to one or more of the requested knowledge bases.", 403));

    [Fact]
    public async Task Query_Denied_NeverReachesTheRetriever()
    {
        Deny();

        var result = await CreateQueryEngine().QueryAsync(new RagQueryRequest { Query = "secret?" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        _retriever.Verify(
            r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<RagRetrievalOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Query_Authorized_RetrievesWithTheAuthorizedIds_NotTheRequestedOnes()
    {
        // 授权器把 search-all 收敛成一个显式列表；引擎必须用它，而不是原样把
        // 请求里的（空）列表传下去 —— 传空就是 search-all，修复等于没做。
        Authorize(AllowedKb);
        RagRetrievalOptions? captured = null;
        _retriever
            .Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<RagRetrievalOptions?>(), It.IsAny<CancellationToken>()))
            .Callback((string _, RagRetrievalOptions? o, CancellationToken _) => captured = o)
            .ReturnsAsync([new RetrievalResult { Content = "chunk", Score = 0.9 }]);

        var result = await CreateQueryEngine().QueryAsync(new RagQueryRequest { Query = "hi", KnowledgeBaseIds = null });

        result.Succeeded.ShouldBeTrue();
        captured.ShouldNotBeNull();
        captured.KnowledgeBaseIds.ShouldBe([AllowedKb]);
    }

    [Fact]
    public async Task Query_PassesAnExplicitMaxTokens_NotTheGlobalDefault()
    {
        // IAiUtility 的全局默认上限是给标题生成这类极短输出定的；拿它生成一段带引用的
        // RAG 回答会被静默截断（不报错，只是话没说完）。
        Authorize(AllowedKb);
        AiUtilityCallOptions? captured = null;
        _aiUtility
            .Setup(u => u.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string _, AiUtilityCallOptions? o, CancellationToken _) => captured = o)
            .ReturnsAsync("answer");

        await CreateQueryEngine().QueryAsync(new RagQueryRequest { Query = "hi" });

        captured.ShouldNotBeNull();
        captured.MaxTokens.ShouldNotBeNull();
        captured.MaxTokens!.Value.ShouldBeGreaterThan(1000);
    }

    [Fact]
    public async Task Chat_Denied_NeverReachesTheRetrieverOrTheAgent()
    {
        Deny();

        var result = await CreateChatEngine().ChatAsync(new RagChatRequest { Query = "secret?" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        _retriever.Verify(
            r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<RagRetrievalOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _agentRuntime.Verify(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChatStreaming_Denied_YieldsAnErrorEventAndStops()
    {
        // 流式端点少了这一处判定，就是同一个洞的另一扇门。
        Deny();

        var events = new List<StreamEvent>();
        await foreach (var e in CreateChatEngine().ChatStreamingAsync(new RagChatRequest { Query = "secret?" }))
        {
            events.Add(e);
        }

        events.Count.ShouldBe(1);
        events[0].IsError.ShouldBeTrue();
        events[0].IsDone.ShouldBeTrue();
        _retriever.Verify(
            r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<RagRetrievalOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Chat_Authorized_RetrievesWithTheAuthorizedIds()
    {
        Authorize(AllowedKb);
        RagRetrievalOptions? captured = null;
        _retriever
            .Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<RagRetrievalOptions?>(), It.IsAny<CancellationToken>()))
            .Callback((string _, RagRetrievalOptions? o, CancellationToken _) => captured = o)
            .ReturnsAsync([new RetrievalResult { Content = "chunk", Score = 0.9 }]);

        var result = await CreateChatEngine().ChatAsync(new RagChatRequest { Query = "hi", KnowledgeBaseIds = null });

        result.Succeeded.ShouldBeTrue();
        captured.ShouldNotBeNull();
        captured.KnowledgeBaseIds.ShouldBe([AllowedKb]);
    }
}
