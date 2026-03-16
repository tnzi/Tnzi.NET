namespace Tnzi.AI.Tests.Memory;

/// <summary>
/// ChatHistoryMemoryProvider 单元测试 — BeforeAIInvoke/OnDemand 模式 + filter
/// </summary>
public class ChatHistoryMemoryProviderTests
{
    #region BeforeAIInvoke Mode

    [Fact]
    public async Task GetContextAsync_BeforeAIInvoke_SearchesAndInjects()
    {
        var mockSearch = new Mock<ITextSearchService>();
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
        var mockSearch = new Mock<ITextSearchService>();
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
        var mockSearch = new Mock<ITextSearchService>();
        var provider = CreateProvider(mockSearch.Object, ContextSearchTime.BeforeAIInvoke);

        var injection = await provider.GetContextAsync([]);

        injection.ShouldBe(ContextInjection.Empty);
    }

    [Fact]
    public async Task GetContextAsync_BeforeAIInvoke_ServiceThrows_ReturnsEmpty()
    {
        var mockSearch = new Mock<ITextSearchService>();
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
        var mockSearch = new Mock<ITextSearchService>();
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
        var mockSearch = new Mock<ITextSearchService>();
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

        var mockSearch = new Mock<ITextSearchService>();
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

            var mockSearch = new Mock<ITextSearchService>();
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
    public async Task GetContextAsync_EmptyScope_PassesNullFilter()
    {
        var scope = new ChatHistoryMemoryScope();
        var mockSearch = new Mock<ITextSearchService>();
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
        await provider.GetContextAsync(messages);

        mockSearch.Verify(s => s.SearchAsync(
            It.IsAny<string>(),
            null,
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region OnCompletedAsync

    [Fact]
    public async Task OnCompletedAsync_NoOp()
    {
        var provider = CreateProvider(Mock.Of<ITextSearchService>(), ContextSearchTime.BeforeAIInvoke);
        // 不应抛异常
        await provider.OnCompletedAsync([]);
    }

    #endregion

    #region Helpers

    private static ChatHistoryMemoryProvider CreateProvider(
        ITextSearchService searchService,
        ContextSearchTime searchTime)
    {
        var options = new ChatHistoryMemoryOptions
        {
            SearchTime = searchTime,
            MaxResults = 5
        };
        var scope = new ChatHistoryMemoryScope();
        return new ChatHistoryMemoryProvider(
            searchService, options, scope, Mock.Of<ILogger<ChatHistoryMemoryProvider>>());
    }

    #endregion
}
