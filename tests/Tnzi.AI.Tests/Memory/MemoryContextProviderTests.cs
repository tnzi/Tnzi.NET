using Tnzi.AI.Memory;

namespace Tnzi.AI.Tests.Memory;

/// <summary>
/// MemoryContextProvider 单元测试 — 上下文注入 + 自动沉淀 + 合并
/// </summary>
public class MemoryContextProviderTests
{
    #region GetContextAsync

    [Fact]
    public async Task GetContextAsync_WithMemory_InjectsSystemMessage()
    {
        var mockStore = new Mock<IMemoryStore>();
        mockStore.Setup(s => s.ReadAsync(It.IsAny<MemoryScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("User prefers dark mode.");

        var provider = CreateProvider(mockStore.Object);

        var injection = await provider.GetContextAsync([]);

        injection.HasContent.ShouldBeTrue();
        injection.Messages.ShouldNotBeNull();
        injection.Messages!.Count.ShouldBe(1);
        injection.Messages[0].Text!.ShouldContain("dark mode");
        injection.Messages[0].Text!.ShouldContain("Persistent Memory");
    }

    [Fact]
    public async Task GetContextAsync_NoMemory_ReturnsEmpty()
    {
        var mockStore = new Mock<IMemoryStore>();
        mockStore.Setup(s => s.ReadAsync(It.IsAny<MemoryScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var provider = CreateProvider(mockStore.Object);

        var injection = await provider.GetContextAsync([]);

        injection.ShouldBe(ContextInjection.Empty);
    }

    [Fact]
    public async Task GetContextAsync_CachesMemory_OnSecondCall()
    {
        var mockStore = new Mock<IMemoryStore>();
        mockStore.Setup(s => s.ReadAsync(It.IsAny<MemoryScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("cached content");

        var provider = CreateProvider(mockStore.Object);

        await provider.GetContextAsync([]);
        await provider.GetContextAsync([]);

        // 只调用一次
        mockStore.Verify(s => s.ReadAsync(It.IsAny<MemoryScope>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetContextAsync_StoreThrows_ReturnsEmpty()
    {
        var mockStore = new Mock<IMemoryStore>();
        mockStore.Setup(s => s.ReadAsync(It.IsAny<MemoryScope>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB error"));

        var provider = CreateProvider(mockStore.Object);

        var injection = await provider.GetContextAsync([]);

        injection.ShouldBe(ContextInjection.Empty);
    }

    #endregion

    #region OnCompletedAsync — AutoPersist disabled

    [Fact]
    public async Task OnCompletedAsync_AutoPersistDisabled_DoesNothing()
    {
        var mockStore = new Mock<IMemoryStore>();
        var provider = CreateProvider(mockStore.Object, autoPersist: false);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello"),
            new(ChatRole.Assistant, "Hi there!")
        };

        await provider.OnCompletedAsync(messages);

        // 不应调用 AppendAsync
        mockStore.Verify(s => s.AppendAsync(It.IsAny<MemoryScope>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region OnCompletedAsync — AutoPersist enabled

    [Fact]
    public async Task OnCompletedAsync_AutoPersistEnabled_ExtractsAndAppends()
    {
        var mockStore = new Mock<IMemoryStore>();
        mockStore.Setup(s => s.ReadAsync(It.IsAny<MemoryScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("existing");

        var mockChatClient = CreateMockChatClient("- User prefers dark mode");
        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(mockChatClient);

        var memoryOptions = new MemoryOptions
        {
            AutoPersist = true,
            AutoConsolidate = false, // 不测合并
            ConsolidateThreshold = 100
        };

        var provider = CreateProvider(mockStore.Object, memoryOptions: memoryOptions, chatClientFactory: mockFactory.Object);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "I prefer dark mode"),
            new(ChatRole.Assistant, "Got it, dark mode preference noted!")
        };

        await provider.OnCompletedAsync(messages);

        mockStore.Verify(s => s.AppendAsync(It.IsAny<MemoryScope>(), It.Is<string>(c => c.Contains("dark mode")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnCompletedAsync_WithSystemInjectedContext_UsesRealUserMessage()
    {
        var mockStore = new Mock<IMemoryStore>();
        var mockChatClient = new Mock<IChatClient>();
        mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.Is<IList<ChatMessage>>(msgs =>
                    msgs.Count == 2 &&
                    msgs[1].Role == ChatRole.User &&
                    msgs[1].Text!.Contains("User: I prefer dark mode") &&
                    !msgs[1].Text!.Contains("Additional Context")),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "- User prefers dark mode")));

        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(mockChatClient.Object);

        var provider = CreateProvider(
            mockStore.Object,
            memoryOptions: new MemoryOptions { AutoPersist = true, AutoConsolidate = false },
            chatClientFactory: mockFactory.Object);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "## Additional Context\nRetrieved from docs"),
            new(ChatRole.User, "I prefer dark mode"),
            new(ChatRole.Assistant, "Noted.")
        };

        await provider.OnCompletedAsync(messages);

        mockChatClient.VerifyAll();
        mockStore.Verify(s => s.AppendAsync(It.IsAny<MemoryScope>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnCompletedAsync_LlmReturnsNone_DoesNotAppend()
    {
        var mockStore = new Mock<IMemoryStore>();
        var mockChatClient = CreateMockChatClient("NONE");
        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(mockChatClient);

        var memoryOptions = new MemoryOptions
        {
            AutoPersist = true,
            AutoConsolidate = false
        };

        var provider = CreateProvider(mockStore.Object, memoryOptions: memoryOptions, chatClientFactory: mockFactory.Object);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "What is 2+2?"),
            new(ChatRole.Assistant, "4")
        };

        await provider.OnCompletedAsync(messages);

        mockStore.Verify(s => s.AppendAsync(It.IsAny<MemoryScope>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OnCompletedAsync_NoUserMessage_DoesNotExtract()
    {
        var mockStore = new Mock<IMemoryStore>();
        var mockFactory = new Mock<IChatClientFactory>();

        var memoryOptions = new MemoryOptions { AutoPersist = true };
        var provider = CreateProvider(mockStore.Object, memoryOptions: memoryOptions, chatClientFactory: mockFactory.Object);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are helpful")
        };

        await provider.OnCompletedAsync(messages);

        // 没有 User+Assistant 消息对，不应调用 LLM
        mockFactory.Verify(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task OnCompletedAsync_LlmThrows_DoesNotRethrow()
    {
        var mockStore = new Mock<IMemoryStore>();
        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Throws(new InvalidOperationException("Provider not configured"));

        var memoryOptions = new MemoryOptions { AutoPersist = true };
        var provider = CreateProvider(mockStore.Object, memoryOptions: memoryOptions, chatClientFactory: mockFactory.Object);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello"),
            new(ChatRole.Assistant, "Hi")
        };

        // 不应抛异常
        await provider.OnCompletedAsync(messages);
    }

    #endregion

    #region OnCompletedAsync — AutoConsolidate

    [Fact]
    public async Task OnCompletedAsync_ConsolidateTriggered_WritesConsolidated()
    {
        var mockStore = new Mock<IMemoryStore>();
        // ReadAsync 返回超过阈值的行数（TryConsolidateAsync 中调用）
        var existingContent = string.Join("\n", Enumerable.Range(1, 25).Select(i => $"- Memory item {i}"));
        mockStore.Setup(s => s.ReadAsync(It.IsAny<MemoryScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingContent);

        // 提取 LLM 返回记忆
        var extractClient = CreateMockChatClient("- Dark mode preferred");
        // 合并 LLM 返回合并结果
        var consolidateClient = CreateMockChatClient("- Dark mode preferred\n- Key fact 1");

        var callCount = 0;
        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(() =>
            {
                callCount++;
                return callCount == 1 ? extractClient : consolidateClient;
            });

        var memoryOptions = new MemoryOptions
        {
            AutoPersist = true,
            AutoConsolidate = true,
            ConsolidateThreshold = 5 // 低阈值触发合并
        };

        var provider = CreateProvider(mockStore.Object, memoryOptions: memoryOptions, chatClientFactory: mockFactory.Object);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "I like dark mode"),
            new(ChatRole.Assistant, "Noted!")
        };

        await provider.OnCompletedAsync(messages);

        // 应先 Append，然后 WriteAsync (合并)
        mockStore.Verify(s => s.AppendAsync(It.IsAny<MemoryScope>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        mockStore.Verify(s => s.WriteAsync(It.IsAny<MemoryScope>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region MemoryScope isolation

    [Fact]
    public async Task GetContextAsync_UsesMemoryScope_WithUserId()
    {
        var userId = Guid.NewGuid();
        var scope = new MemoryScope("default", userId);
        var mockStore = new Mock<IMemoryStore>();
        mockStore.Setup(s => s.ReadAsync(It.Is<MemoryScope>(ms => ms.UserId == userId), It.IsAny<CancellationToken>()))
            .ReturnsAsync("user-specific memory");

        var provider = new MemoryContextProvider(mockStore.Object, scope, Mock.Of<ILogger<MemoryContextProvider>>());

        var injection = await provider.GetContextAsync([]);

        injection.HasContent.ShouldBeTrue();
        injection.Messages![0].Text!.ShouldContain("user-specific memory");
    }

    #endregion

    #region IncrementalConsolidate

    [Fact]
    public async Task OnCompletedAsync_IncrementalConsolidate_CallsConsolidator()
    {
        var mockStore = new Mock<IMemoryStore>();
        mockStore.Setup(s => s.ReadAsync(It.IsAny<MemoryScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("existing memory");

        // SearchAsync returns one existing result so consolidator gets called
        var existingResult = new MemorySearchResult { Id = Guid.NewGuid(), Content = "User likes dark mode", Score = 0.9 };
        mockStore.Setup(s => s.SearchAsync(It.IsAny<MemoryScope>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<MemorySearchResult>)[existingResult]);

        // LLM returns bracket-format memory
        var mockChatClient = CreateMockChatClient("[category=preference importance=0.8] User prefers dark theme");
        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(mockChatClient);

        var mockConsolidator = new Mock<IMemoryConsolidator>();
        mockConsolidator
            .Setup(c => c.ConsolidateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<MemorySearchResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryConsolidationResult(MemoryAction.Add));

        var memoryOptions = new MemoryOptions
        {
            AutoPersist = true,
            AutoConsolidate = false,
            IncrementalConsolidate = true,
            ConsolidateSearchTopK = 3,
            MaxConsolidationCallsPerPersist = 5
        };

        var provider = CreateProvider(mockStore.Object, memoryOptions: memoryOptions,
            chatClientFactory: mockFactory.Object, memoryConsolidator: mockConsolidator.Object);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "I prefer dark mode"),
            new(ChatRole.Assistant, "Noted!")
        };

        await provider.OnCompletedAsync(messages);

        // Consolidator should have been called once
        mockConsolidator.Verify(c => c.ConsolidateAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<MemorySearchResult>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnCompletedAsync_IncrementalConsolidate_NoExistingMemory_AppendsDirectly()
    {
        var mockStore = new Mock<IMemoryStore>();
        mockStore.Setup(s => s.ReadAsync(It.IsAny<MemoryScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // SearchAsync returns empty — no existing memory
        mockStore.Setup(s => s.SearchAsync(It.IsAny<MemoryScope>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<MemorySearchResult>)[]);

        var mockChatClient = CreateMockChatClient("[category=fact importance=0.9] User is the CTO");
        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(mockChatClient);

        var mockConsolidator = new Mock<IMemoryConsolidator>();

        var memoryOptions = new MemoryOptions
        {
            AutoPersist = true,
            AutoConsolidate = false,
            IncrementalConsolidate = true,
            ConsolidateSearchTopK = 3,
            MaxConsolidationCallsPerPersist = 5
        };

        var provider = CreateProvider(mockStore.Object, memoryOptions: memoryOptions,
            chatClientFactory: mockFactory.Object, memoryConsolidator: mockConsolidator.Object);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "I am the CTO"),
            new(ChatRole.Assistant, "Noted!")
        };

        await provider.OnCompletedAsync(messages);

        // No existing memories, consolidator not called, but AppendAsync called
        mockConsolidator.Verify(c => c.ConsolidateAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<MemorySearchResult>>(),
            It.IsAny<CancellationToken>()), Times.Never);
        mockStore.Verify(s => s.AppendAsync(
            It.IsAny<MemoryScope>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnCompletedAsync_IncrementalConsolidate_UpdateAction_CallsUpdateEntry()
    {
        var mockStore = new Mock<IMemoryStore>();
        var existingId = Guid.NewGuid();
        var existingResult = new MemorySearchResult { Id = existingId, Content = "User prefers light theme", Score = 0.95 };
        mockStore.Setup(s => s.SearchAsync(It.IsAny<MemoryScope>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<MemorySearchResult>)[existingResult]);

        var mockChatClient = CreateMockChatClient("[category=preference importance=0.9] User prefers dark theme");
        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(mockChatClient);

        var mockConsolidator = new Mock<IMemoryConsolidator>();
        mockConsolidator
            .Setup(c => c.ConsolidateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<MemorySearchResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryConsolidationResult(MemoryAction.Update, "User prefers dark theme", existingId));

        var memoryOptions = new MemoryOptions
        {
            AutoPersist = true,
            AutoConsolidate = false,
            IncrementalConsolidate = true,
            MaxConsolidationCallsPerPersist = 5
        };

        var provider = CreateProvider(mockStore.Object, memoryOptions: memoryOptions,
            chatClientFactory: mockFactory.Object, memoryConsolidator: mockConsolidator.Object);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "I switched to dark theme"),
            new(ChatRole.Assistant, "Got it!")
        };

        await provider.OnCompletedAsync(messages);

        mockStore.Verify(s => s.UpdateEntryAsync(It.IsAny<string>(), existingId, "User prefers dark theme", It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ParseMemoryLine

    [Theory]
    [InlineData("[category=preference importance=0.8] User prefers dark theme", "User prefers dark theme", "preference", 0.8)]
    [InlineData("[category=fact importance=0.9] User is the CTO of Acme Corp", "User is the CTO of Acme Corp", "fact", 0.9)]
    [InlineData("[category=decision importance=0.7] Team chose PostgreSQL", "Team chose PostgreSQL", "decision", 0.7)]
    public void ParseMemoryLine_BracketFormat_ParsesCorrectly(string input, string expectedContent, string expectedCategory, double expectedImportance)
    {
        // Use reflection to call the private static method
        var method = typeof(MemoryContextProvider).GetMethod("ParseMemoryLine",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        method.ShouldNotBeNull();

        var result = ((string content, string? category, double importance))method.Invoke(null, [input])!;

        result.content.ShouldBe(expectedContent);
        result.category.ShouldBe(expectedCategory);
        result.importance.ShouldBe(expectedImportance, tolerance: 0.001);
    }

    [Fact]
    public void ParseMemoryLine_BulletPoint_ReturnsDefaultImportance()
    {
        var method = typeof(MemoryContextProvider).GetMethod("ParseMemoryLine",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        method.ShouldNotBeNull();

        var result = ((string content, string? category, double importance))method.Invoke(null, ["- User prefers dark mode"])!;

        result.content.ShouldBe("User prefers dark mode");
        result.category.ShouldBeNull();
        result.importance.ShouldBe(0.5);
    }

    [Fact]
    public void ParseMemoryLine_ImportanceClamped_WhenOutOfRange()
    {
        var method = typeof(MemoryContextProvider).GetMethod("ParseMemoryLine",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        method.ShouldNotBeNull();

        // importance=1.5 should be clamped to 1.0
        var result = ((string content, string? category, double importance))method.Invoke(null, ["[category=fact importance=1.5] Some fact"])!;

        result.importance.ShouldBe(1.0);
    }

    #endregion

    #region Helpers

    private static MemoryContextProvider CreateProvider(
        IMemoryStore store,
        bool autoPersist = false,
        MemoryOptions? memoryOptions = null,
        IChatClientFactory? chatClientFactory = null,
        IMemoryConsolidator? memoryConsolidator = null)
    {
        var scope = new MemoryScope("default");
        var logger = Mock.Of<ILogger<MemoryContextProvider>>();

        memoryOptions ??= new MemoryOptions { AutoPersist = autoPersist };

        return new MemoryContextProvider(store, scope, logger, chatClientFactory, memoryOptions, memoryConsolidator);
    }

    private static IChatClient CreateMockChatClient(string responseText)
    {
        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));
        return mock.Object;
    }

    #endregion
}
