namespace Tnzi.AI.Tests;

/// <summary>
/// AiUtilityService 单元测试
/// </summary>
public class AiUtilityServiceTests
{
    private readonly Mock<IChatClientFactory> _mockFactory;
    private readonly Mock<IChatClient> _mockChatClient;
    private readonly Mock<IOptionsMonitor<AiUtilityOptions>> _mockOptions;

    public AiUtilityServiceTests()
    {
        _mockFactory = new Mock<IChatClientFactory>();
        _mockChatClient = new Mock<IChatClient>();
        _mockOptions = new Mock<IOptionsMonitor<AiUtilityOptions>>();

        _mockOptions.Setup(x => x.CurrentValue).Returns(new AiUtilityOptions
        {
            MaxTokens = 100,
            Temperature = 0.3
        });

        _mockFactory
            .Setup(x => x.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(_mockChatClient.Object);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidInput_ReturnsResponse()
    {
        _mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Test Title")));

        var service = new AiUtilityService(_mockFactory.Object, _mockOptions.Object);

        var result = await service.ExecuteAsync("You are a title generator.", "Generate a title for my article.");

        result.ShouldBe("Test Title");
    }

    [Fact]
    public async Task ExecuteAsync_WithCallOptions_UsesModelOverride()
    {
        _mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "response")));

        var service = new AiUtilityService(_mockFactory.Object, _mockOptions.Object);

        await service.ExecuteAsync(
            "System prompt",
            "User message",
            new AiUtilityCallOptions { Model = "gpt-4.1-mini" });

        _mockFactory.Verify(
            x => x.GetChatClient(null, "gpt-4.1-mini"),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenChatClientReturnsEmpty_ReturnsNull()
    {
        _mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "")));

        var service = new AiUtilityService(_mockFactory.Object, _mockOptions.Object);

        var result = await service.ExecuteAsync("System prompt", "User message");

        result.ShouldBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WhenExceptionThrown_ReturnsNull()
    {
        _mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection failed"));

        var service = new AiUtilityService(_mockFactory.Object, _mockOptions.Object);

        var result = await service.ExecuteAsync("System prompt", "User message");

        result.ShouldBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_Rethrows()
    {
        _mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var service = new AiUtilityService(_mockFactory.Object, _mockOptions.Object);

        await Should.ThrowAsync<OperationCanceledException>(
            () => service.ExecuteAsync("System prompt", "User message", cancellationToken: CancellationToken.None));
    }
}
