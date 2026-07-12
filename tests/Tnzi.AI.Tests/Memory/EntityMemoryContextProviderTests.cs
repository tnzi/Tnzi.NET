using Tnzi.AI.Infrastructure.Memory;

namespace Tnzi.AI.Tests.Memory;

/// <summary>
/// EntityMemoryContextProvider 单元测试 — 实体记忆上下文提供器
/// </summary>
public class EntityMemoryContextProviderTests
{
    #region OnCompletedAsync — entity extraction from User+Assistant

    [Fact]
    public async Task OnCompletedAsync_ExtractsFromBothUserAndAssistant()
    {
        var mockEntityStore = new Mock<IEntityMemoryStore>();
        mockEntityStore
            .Setup(s => s.UpsertEntitiesAsync(It.IsAny<IEnumerable<EntityMemoryEntry>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // LLM returns entity extracted from combined text
        var mockChatClient = new Mock<IChatClient>();
        mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.Is<IList<ChatMessage>>(msgs =>
                    msgs.Count == 2 &&
                    msgs[1].Role == ChatRole.User &&
                    msgs[1].Text!.Contains("Alice") &&
                    msgs[1].Text!.Contains("Microsoft")),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                """[{"name":"Alice","type":"person","properties":{}},{"name":"Microsoft","type":"organization","properties":{}}]""")));

        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(mockChatClient.Object);

        var options = new StaticOptionsMonitor<AIOptions>(new AIOptions());
        var extractor = new LlmEntityExtractor(mockFactory.Object, options, Mock.Of<ILogger<LlmEntityExtractor>>());
        var entityOptions = new EntityMemoryOptions();
        var logger = Mock.Of<ILogger<EntityMemoryContextProvider>>();

        var provider = new EntityMemoryContextProvider(mockEntityStore.Object, entityOptions, extractor, logger);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Alice works at Microsoft"),
            new(ChatRole.Assistant, "Got it, I've noted that Alice works at Microsoft.")
        };

        await provider.OnCompletedAsync(messages);

        // Both user and assistant text were joined, extractor should receive combined text
        mockChatClient.VerifyAll();

        // Entities should have been upserted
        mockEntityStore.Verify(s => s.UpsertEntitiesAsync(
            It.Is<IEnumerable<EntityMemoryEntry>>(entries => entries.Any()),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnCompletedAsync_OnlyUserMessage_StillExtractsEntities()
    {
        var mockEntityStore = new Mock<IEntityMemoryStore>();
        mockEntityStore
            .Setup(s => s.UpsertEntitiesAsync(It.IsAny<IEnumerable<EntityMemoryEntry>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var mockChatClient = new Mock<IChatClient>();
        mockChatClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                """[{"name":"Bob","type":"person","properties":{}}]""")));

        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(mockChatClient.Object);

        var options = new StaticOptionsMonitor<AIOptions>(new AIOptions());
        var extractor = new LlmEntityExtractor(mockFactory.Object, options, Mock.Of<ILogger<LlmEntityExtractor>>());
        var provider = new EntityMemoryContextProvider(
            mockEntityStore.Object, new EntityMemoryOptions(), extractor, Mock.Of<ILogger<EntityMemoryContextProvider>>());

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Bob is my colleague"),
            // No assistant message
        };

        await provider.OnCompletedAsync(messages);

        // Should still attempt extraction using the user message text
        mockEntityStore.Verify(s => s.UpsertEntitiesAsync(
            It.Is<IEnumerable<EntityMemoryEntry>>(entries => entries.Any()),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnCompletedAsync_OnlyAssistantMessage_StillExtractsEntities()
    {
        var mockEntityStore = new Mock<IEntityMemoryStore>();
        mockEntityStore
            .Setup(s => s.UpsertEntitiesAsync(It.IsAny<IEnumerable<EntityMemoryEntry>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var mockChatClient = new Mock<IChatClient>();
        mockChatClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                """[{"name":"Carol","type":"person","properties":{}}]""")));

        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(mockChatClient.Object);

        var options = new StaticOptionsMonitor<AIOptions>(new AIOptions());
        var extractor = new LlmEntityExtractor(mockFactory.Object, options, Mock.Of<ILogger<LlmEntityExtractor>>());
        var provider = new EntityMemoryContextProvider(
            mockEntityStore.Object, new EntityMemoryOptions(), extractor, Mock.Of<ILogger<EntityMemoryContextProvider>>());

        var messages = new List<ChatMessage>
        {
            // No user message
            new(ChatRole.Assistant, "Carol is the project lead.")
        };

        await provider.OnCompletedAsync(messages);

        mockEntityStore.Verify(s => s.UpsertEntitiesAsync(
            It.Is<IEnumerable<EntityMemoryEntry>>(entries => entries.Any()),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnCompletedAsync_NoTextMessages_DoesNotExtract()
    {
        var mockEntityStore = new Mock<IEntityMemoryStore>();
        var mockFactory = new Mock<IChatClientFactory>();

        var options = new StaticOptionsMonitor<AIOptions>(new AIOptions());
        var extractor = new LlmEntityExtractor(mockFactory.Object, options, Mock.Of<ILogger<LlmEntityExtractor>>());
        var provider = new EntityMemoryContextProvider(
            mockEntityStore.Object, new EntityMemoryOptions(), extractor, Mock.Of<ILogger<EntityMemoryContextProvider>>());

        var messages = new List<ChatMessage>
        {
            // System message only — no user or assistant text
            new(ChatRole.System, "You are helpful")
        };

        await provider.OnCompletedAsync(messages);

        mockEntityStore.Verify(s => s.UpsertEntitiesAsync(
            It.IsAny<IEnumerable<EntityMemoryEntry>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OnCompletedAsync_ExtractorReturnsEmpty_DoesNotUpsert()
    {
        var mockEntityStore = new Mock<IEntityMemoryStore>();

        var mockChatClient = new Mock<IChatClient>();
        mockChatClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "[]")));

        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(mockChatClient.Object);

        var options = new StaticOptionsMonitor<AIOptions>(new AIOptions());
        var extractor = new LlmEntityExtractor(mockFactory.Object, options, Mock.Of<ILogger<LlmEntityExtractor>>());
        var provider = new EntityMemoryContextProvider(
            mockEntityStore.Object, new EntityMemoryOptions(), extractor, Mock.Of<ILogger<EntityMemoryContextProvider>>());

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "What is 2+2?"),
            new(ChatRole.Assistant, "4")
        };

        await provider.OnCompletedAsync(messages);

        mockEntityStore.Verify(s => s.UpsertEntitiesAsync(
            It.IsAny<IEnumerable<EntityMemoryEntry>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion
}
