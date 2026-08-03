
namespace Tnzi.AI.Tests.Memory;

/// <summary>
/// MemoryContextProvider AutoPersist 互斥与节流测试
/// </summary>
public class MemoryAutoPersistTests
{
    [Fact]
    public async Task OnCompletedAsync_SaveMemoryCalledInConversation_SkipsAutoPersist()
    {
        // Arrange: 对话中包含 save_memory 工具调用
        var mockStore = new Mock<IMemoryStore>();
        var mockFactory = new Mock<IChatClientFactory>();

        var memoryOptions = new MemoryOptions
        {
            AutoPersist = true,
            AutoConsolidate = false,
            MinTurnsBetweenExtractions = 1
        };

        var provider = CreateProvider(mockStore.Object, memoryOptions: memoryOptions, chatClientFactory: mockFactory.Object);

        var saveMemoryCall = new FunctionCallContent("call_1", "save_memory",
            new Dictionary<string, object?> { ["content"] = "User prefers dark mode" });

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Remember that I like dark mode"),
            new(ChatRole.Assistant, [saveMemoryCall]),
            new(ChatRole.Tool, [new FunctionResultContent("call_1", "Memory saved.")]),
            new(ChatRole.Assistant, "I've saved that preference for you.")
        };

        // Act
        await provider.OnCompletedAsync(messages);

        // Assert: ChatClient 不应被调用（AutoPersist 被跳过）
        mockFactory.Verify(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task OnCompletedAsync_TurnsBelowThreshold_SkipsAutoPersist()
    {
        // Arrange: 用户消息轮数低于 MinTurnsBetweenExtractions
        var mockStore = new Mock<IMemoryStore>();
        var mockFactory = new Mock<IChatClientFactory>();

        var memoryOptions = new MemoryOptions
        {
            AutoPersist = true,
            AutoConsolidate = false,
            MinTurnsBetweenExtractions = 5 // 需要至少 5 轮
        };

        var provider = CreateProvider(mockStore.Object, memoryOptions: memoryOptions, chatClientFactory: mockFactory.Object);

        // 只有 2 轮用户消息（低于阈值 5）
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello"),
            new(ChatRole.Assistant, "Hi there!"),
            new(ChatRole.User, "How are you?"),
            new(ChatRole.Assistant, "I'm doing well!")
        };

        // Act
        await provider.OnCompletedAsync(messages);

        // Assert: ChatClient 不应被调用（节流跳过）
        mockFactory.Verify(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task OnCompletedAsync_AboveThreshold_NoSaveMemory_Proceeds()
    {
        // Arrange: 用户消息轮数达到阈值，且没有 save_memory 调用
        var mockStore = new Mock<IMemoryStore>();
        mockStore.Setup(s => s.ReadAsync(It.IsAny<MemoryScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("existing");

        var mockChatClient = new Mock<IChatClient>();
        mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "- User likes coding")));

        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(mockChatClient.Object);

        var memoryOptions = new MemoryOptions
        {
            AutoPersist = true,
            AutoConsolidate = false,
            MinTurnsBetweenExtractions = 3 // 需要至少 3 轮
        };

        var provider = CreateProvider(mockStore.Object, memoryOptions: memoryOptions, chatClientFactory: mockFactory.Object);

        // 4 轮用户消息（超过阈值 3）
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello"),
            new(ChatRole.Assistant, "Hi!"),
            new(ChatRole.User, "I like coding"),
            new(ChatRole.Assistant, "Great!"),
            new(ChatRole.User, "Especially in C#"),
            new(ChatRole.Assistant, "Nice choice!"),
            new(ChatRole.User, "Thanks"),
            new(ChatRole.Assistant, "You're welcome!")
        };

        // Act
        await provider.OnCompletedAsync(messages);

        // Assert: ChatClient 应被调用（AutoPersist 正常执行）
        mockFactory.Verify(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()), Times.Once);
        mockStore.Verify(s => s.AppendAsync(
            It.IsAny<MemoryScope>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnCompletedAsync_ThrottleResetsAfterSuccessfulExtraction()
    {
        // Arrange: 验证成功提取后 _lastAutoExtractTurn 被更新
        var mockStore = new Mock<IMemoryStore>();
        mockStore.Setup(s => s.ReadAsync(It.IsAny<MemoryScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("existing");

        var mockChatClient = new Mock<IChatClient>();
        mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "- Some memory")));

        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(mockChatClient.Object);

        var memoryOptions = new MemoryOptions
        {
            AutoPersist = true,
            AutoConsolidate = false,
            MinTurnsBetweenExtractions = 2
        };

        var provider = CreateProvider(mockStore.Object, memoryOptions: memoryOptions, chatClientFactory: mockFactory.Object);

        // 第一次调用: 3 轮用户消息，应执行
        var messages1 = new List<ChatMessage>
        {
            new(ChatRole.User, "A"),
            new(ChatRole.Assistant, "B"),
            new(ChatRole.User, "C"),
            new(ChatRole.Assistant, "D"),
            new(ChatRole.User, "E"),
            new(ChatRole.Assistant, "F")
        };
        await provider.OnCompletedAsync(messages1);

        // 第二次调用: 相同消息（轮数没变化），应跳过
        await provider.OnCompletedAsync(messages1);

        // ChatClient 只应在第一次调用
        mockFactory.Verify(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task OnCompletedAsync_SaveMemoryInToolContent_SkipsAutoPersist()
    {
        // Arrange: save_memory 在 Assistant 消息的 Contents 中
        var mockStore = new Mock<IMemoryStore>();
        var mockFactory = new Mock<IChatClientFactory>();

        var memoryOptions = new MemoryOptions
        {
            AutoPersist = true,
            AutoConsolidate = false,
            MinTurnsBetweenExtractions = 1
        };

        var provider = CreateProvider(mockStore.Object, memoryOptions: memoryOptions, chatClientFactory: mockFactory.Object);

        // 混合内容消息：文本 + save_memory 工具调用
        var textContent = new TextContent("Let me save that for you.");
        var saveMemoryCall = new FunctionCallContent("call_2", "save_memory");
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Remember my timezone is PST"),
            new(ChatRole.Assistant, [textContent, saveMemoryCall]),
            new(ChatRole.Tool, [new FunctionResultContent("call_2", "Saved.")]),
            new(ChatRole.User, "Thanks"),
            new(ChatRole.Assistant, "You're welcome!")
        };

        // Act
        await provider.OnCompletedAsync(messages);

        // Assert: 应跳过
        mockFactory.Verify(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    #region Helpers

    private static MemoryContextProvider CreateProvider(
        IMemoryStore store,
        MemoryOptions? memoryOptions = null,
        IChatClientFactory? chatClientFactory = null,
        IMemoryConsolidator? memoryConsolidator = null)
    {
        var scope = new MemoryScope("default");
        var logger = Mock.Of<ILogger<MemoryContextProvider>>();

        memoryOptions ??= new MemoryOptions();

        return new MemoryContextProvider(store, scope, logger, chatClientFactory, memoryOptions, memoryConsolidator);
    }

    #endregion
}
