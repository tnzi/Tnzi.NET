using Tnzi.AI.Infrastructure.ContextProviders.Contributors;

namespace Tnzi.AI.Tests.Memory;

/// <summary>
/// ChatHistoryMemoryProvider 单元测试 - BeforeAIInvoke/OnDemand 模式 + filter
/// </summary>
public class ChatHistoryMemoryProviderTests
{
    #region BeforeAIInvoke Mode

    [Fact]
    public async Task GetContextAsync_BeforeAIInvoke_SearchesAndInjects()
    {
        var mockSearch = CreateSearchMock();
        mockSearch.Setup(s => s.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<TextSearchFilter?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TextSearchResult>
            {
                new() { Text = "Previous conversation about AI" },
                new() { Text = "Discussion about architecture" }
            });

        var provider = CreateProvider(mockSearch.Object, ContextSearchTime.BeforeAIInvoke);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Tell me about AI architecture")
        };

        var injection = await provider.GetContextAsync(messages);

        injection.HasContent.ShouldBeTrue();
        injection.Messages.ShouldNotBeNull();
        injection.Messages!.Count.ShouldBe(1);
        injection.Messages[0].Text!.ShouldContain("Previous conversation about AI");
        injection.Messages[0].Text!.ShouldContain("architecture");
    }

    [Fact]
    public async Task GetContextAsync_BeforeAIInvoke_NoResults_ReturnsEmpty()
    {
        var mockSearch = CreateSearchMock();
        mockSearch.Setup(s => s.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<TextSearchFilter?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TextSearchResult>());

        var provider = CreateProvider(mockSearch.Object, ContextSearchTime.BeforeAIInvoke);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Random question")
        };

        var injection = await provider.GetContextAsync(messages);

        injection.ShouldBe(ContextInjection.Empty);
    }

    [Fact]
    public async Task GetContextAsync_BeforeAIInvoke_EmptyMessages_ReturnsEmpty()
    {
        var mockSearch = CreateSearchMock();
        var provider = CreateProvider(mockSearch.Object, ContextSearchTime.BeforeAIInvoke);

        var injection = await provider.GetContextAsync([]);

        injection.ShouldBe(ContextInjection.Empty);
    }

    [Fact]
    public async Task GetContextAsync_BeforeAIInvoke_ServiceThrows_ReturnsEmpty()
    {
        var mockSearch = CreateSearchMock();
        mockSearch.Setup(s => s.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<TextSearchFilter?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("search error"));

        var provider = CreateProvider(mockSearch.Object, ContextSearchTime.BeforeAIInvoke);

        var messages = new List<ChatMessage> { new(ChatRole.User, "test") };
        var injection = await provider.GetContextAsync(messages);

        injection.ShouldBe(ContextInjection.Empty);
    }

    #endregion

    #region OnDemand Mode

    [Fact]
    public async Task GetContextAsync_OnDemand_InjectsSearchTool()
    {
        var mockSearch = CreateSearchMock();
        var provider = CreateProvider(mockSearch.Object, ContextSearchTime.OnDemandFunctionCalling);

        var messages = new List<ChatMessage> { new(ChatRole.User, "test") };
        var injection = await provider.GetContextAsync(messages);

        injection.HasContent.ShouldBeTrue();
        injection.Tools.ShouldNotBeNull();
        injection.Tools!.Count.ShouldBe(1);
    }

    [Fact]
    public async Task GetContextAsync_OnDemand_DoesNotAutoSearch()
    {
        var mockSearch = CreateSearchMock();
        var provider = CreateProvider(mockSearch.Object, ContextSearchTime.OnDemandFunctionCalling);

        var messages = new List<ChatMessage> { new(ChatRole.User, "test") };
        await provider.GetContextAsync(messages);

        // 不应自动搜索
        mockSearch.Verify(s => s.SearchAsync(
            It.IsAny<string>(),
            It.IsAny<TextSearchFilter?>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Filter Construction

    [Fact]
    public async Task GetContextAsync_WithScope_PassesFilter()
    {
        var userId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var scope = new ChatHistoryMemoryScope
        {
            UserId = userId.ToString(),
            AgentId = agentId.ToString(),
            ThreadId = "thread-123"
        };

        var mockSearch = CreateSearchMock();
        mockSearch.Setup(s => s.SearchAsync(
                It.IsAny<string>(),
                It.Is<TextSearchFilter?>(f => f != null && f.UserId == userId && f.AgentId == agentId && f.ThreadId == "thread-123"),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TextSearchResult> { new() { Text = "result" } });

        var options = new ChatHistoryMemoryOptions
        {
            SearchTime = ContextSearchTime.BeforeAIInvoke,
            MaxResults = 5
        };
        var provider = new ChatHistoryMemoryProvider(
            mockSearch.Object, options, scope, Mock.Of<ILogger<ChatHistoryMemoryProvider>>());

        var messages = new List<ChatMessage> { new(ChatRole.User, "test") };
        await provider.GetContextAsync(messages);

        mockSearch.Verify(s => s.SearchAsync(
            It.IsAny<string>(),
            It.Is<TextSearchFilter?>(f => f != null),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetContextAsync_WithExecutionContext_PrefersRequestFilter()
    {
        var requestUserId = Guid.NewGuid();
        var requestAgentId = Guid.NewGuid();
        var accessor = new AgentExecutionContextAccessor
        {
            CurrentRequest = new AgentRunRequest
            {
                UserId = requestUserId,
                AgentId = requestAgentId,
                ThreadId = Guid.NewGuid()
            }
        };

        try
        {
            var scope = new ChatHistoryMemoryScope
            {
                UserId = Guid.NewGuid().ToString(),
                AgentId = Guid.NewGuid().ToString(),
                ThreadId = "legacy-thread"
            };

            var mockSearch = CreateSearchMock();
            mockSearch.Setup(s => s.SearchAsync(
                    It.IsAny<string>(),
                    It.Is<TextSearchFilter?>(f =>
                        f != null &&
                        f.UserId == requestUserId &&
                        f.AgentId == requestAgentId &&
                        f.ThreadId == accessor.CurrentRequest!.ThreadId!.Value.ToString()),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<TextSearchResult> { new() { Text = "result" } });

            var provider = new ChatHistoryMemoryProvider(
                mockSearch.Object,
                new ChatHistoryMemoryOptions { SearchTime = ContextSearchTime.BeforeAIInvoke },
                scope,
                Mock.Of<ILogger<ChatHistoryMemoryProvider>>(),
                accessor);

            await provider.GetContextAsync([new ChatMessage(ChatRole.User, "test")]);

            mockSearch.VerifyAll();
        }
        finally
        {
            accessor.CurrentRequest = null;
        }
    }

    [Fact]
    public async Task GetContextAsync_EmptyScope_DoesNotSearch()
    {
        // 没有用户也没有线程就不是一个对话范围：不带范围的检索就是跨全部数据的检索，绝不发起
        var scope = new ChatHistoryMemoryScope();
        var mockSearch = CreateSearchMock();
        mockSearch.Setup(s => s.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<TextSearchFilter?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TextSearchResult> { new() { Text = "result" } });

        var options = new ChatHistoryMemoryOptions
        {
            SearchTime = ContextSearchTime.BeforeAIInvoke
        };
        var provider = new ChatHistoryMemoryProvider(
            mockSearch.Object, options, scope, Mock.Of<ILogger<ChatHistoryMemoryProvider>>());

        var messages = new List<ChatMessage> { new(ChatRole.User, "test") };
        var injection = await provider.GetContextAsync(messages);

        injection.ShouldBe(ContextInjection.Empty);
        mockSearch.Verify(s => s.SearchAsync(
            It.IsAny<string>(),
            It.IsAny<TextSearchFilter?>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetContextAsync_AgentScopeOnly_DoesNotSearch()
    {
        // 只按 Agent 限定会把同一 Agent 下所有用户的历史混在一起：Agent 不是对话范围
        var mockSearch = CreateSearchMock();
        var provider = new ChatHistoryMemoryProvider(
            mockSearch.Object,
            new ChatHistoryMemoryOptions { SearchTime = ContextSearchTime.BeforeAIInvoke },
            new ChatHistoryMemoryScope { AgentId = Guid.NewGuid().ToString() },
            Mock.Of<ILogger<ChatHistoryMemoryProvider>>());

        await provider.GetContextAsync([new ChatMessage(ChatRole.User, "test")]);

        mockSearch.Verify(s => s.SearchAsync(
            It.IsAny<string>(),
            It.IsAny<TextSearchFilter?>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region OnCompletedAsync

    [Fact]
    public async Task OnCompletedAsync_NoOp()
    {
        var provider = CreateProvider(CreateSearchMock().Object, ContextSearchTime.BeforeAIInvoke);
        // 不应抛异常
        await provider.OnCompletedAsync([]);
    }

    #endregion

    #region Search service without conversation scope (built-in knowledge-base search)

    // 内置的向量 / 混合检索只认 KnowledgeBaseIds，UserId / ThreadId 原样忽略：拿它做「历史对话」检索
    // 就是一次跨全部知识库的检索，绕过 Agent 的知识库授权，结果还被当作以前的对话注入。

    [Fact]
    public async Task GetContextAsync_ServiceWithoutConversationScope_DoesNotSearch()
    {
        var knowledgeBaseSearch = new Mock<ITextSearchService>();
        knowledgeBaseSearch.Setup(s => s.SearchAsync(
                It.IsAny<string>(), It.IsAny<TextSearchFilter?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TextSearchResult> { new() { Text = "HR salary table" } });

        var provider = CreateProvider(knowledgeBaseSearch.Object, ContextSearchTime.BeforeAIInvoke);
        var injection = await provider.GetContextAsync([new ChatMessage(ChatRole.User, "salaries")]);

        injection.ShouldBe(ContextInjection.Empty);
        knowledgeBaseSearch.Verify(s => s.SearchAsync(
            It.IsAny<string>(), It.IsAny<TextSearchFilter?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchTool_ServiceWithoutConversationScope_DoesNotSearch()
    {
        var knowledgeBaseSearch = new Mock<ITextSearchService>();
        knowledgeBaseSearch.Setup(s => s.SearchAsync(
                It.IsAny<string>(), It.IsAny<TextSearchFilter?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TextSearchResult> { new() { Text = "HR salary table" } });

        var provider = CreateProvider(knowledgeBaseSearch.Object, ContextSearchTime.OnDemandFunctionCalling);
        var injection = await provider.GetContextAsync([new ChatMessage(ChatRole.User, "salaries")]);
        var tool = (AIFunction)injection.Tools!.Single();
        var result = await tool.InvokeAsync(new AIFunctionArguments { ["query"] = "salaries" });

        result!.ToString()!.ShouldNotContain("HR salary table");
        knowledgeBaseSearch.Verify(s => s.SearchAsync(
            It.IsAny<string>(), It.IsAny<TextSearchFilter?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Contributor_ServiceWithoutConversationScope_CreatesNoProvider()
    {
        var options = new AIOptions();
        options.ContextProviders.ChatHistoryMemory.Enabled = true;
        var contributor = new ChatHistoryMemoryContributor(
            new Mock<ITextSearchService>().Object,
            new StaticOptionsMonitor<AIOptions>(options),
            new AgentExecutionContextAccessor(),
            NullLoggerFactory.Instance);

        var provider = contributor.TryCreate(new ContextProviderCreationContext { UserId = Guid.NewGuid(), AgentId = Guid.NewGuid() });

        provider.ShouldBeNull();
    }

    [Fact]
    public void Contributor_ServiceWithConversationScope_CreatesProvider()
    {
        var options = new AIOptions();
        options.ContextProviders.ChatHistoryMemory.Enabled = true;
        var contributor = new ChatHistoryMemoryContributor(
            CreateSearchMock().Object,
            new StaticOptionsMonitor<AIOptions>(options),
            new AgentExecutionContextAccessor(),
            NullLoggerFactory.Instance);

        var provider = contributor.TryCreate(new ContextProviderCreationContext { UserId = Guid.NewGuid() });

        provider.ShouldNotBeNull();
    }

    #endregion

    #region Helpers

    /// <summary>一个声明支持对话范围检索的实现（检索已索引的聊天历史，且按 UserId / ThreadId 限定结果）。</summary>
    private static Mock<ITextSearchService> CreateSearchMock()
    {
        var mock = new Mock<ITextSearchService>();
        mock.SetupGet(s => s.SupportsConversationScope).Returns(true);
        return mock;
    }

    private static ChatHistoryMemoryProvider CreateProvider(
        ITextSearchService searchService,
        ContextSearchTime searchTime)
    {
        var options = new ChatHistoryMemoryOptions
        {
            SearchTime = searchTime,
            MaxResults = 5
        };
        var scope = new ChatHistoryMemoryScope { UserId = Guid.NewGuid().ToString() };
        return new ChatHistoryMemoryProvider(
            searchService, options, scope, Mock.Of<ILogger<ChatHistoryMemoryProvider>>());
    }

    #endregion
}
