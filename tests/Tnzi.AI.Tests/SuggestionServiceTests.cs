namespace Tnzi.AI.Tests;

/// <summary>
/// SuggestionService 测试
/// </summary>
public class SuggestionServiceTests
{
    private readonly Mock<IAiUtility> _aiUtility;
    private readonly Mock<IAgentThreadInternalService> _threadService;

    public SuggestionServiceTests()
    {
        _aiUtility = new Mock<IAiUtility>();
        _threadService = new Mock<IAgentThreadInternalService>();
    }

    private SuggestionService CreateService(SuggestionOptions? options = null)
    {
        var opts = Microsoft.Extensions.Options.Options.Create(options ?? new SuggestionOptions { AutoGenerate = true });
        return new SuggestionService(
            _aiUtility.Object,
            _threadService.Object,
            opts,
            NullLogger<SuggestionService>.Instance);
    }

    private static List<ChatMessage> CreateMessages(params (ChatRole role, string content)[] msgs)
    {
        return msgs.Select(m => new ChatMessage(m.role, m.content)).ToList();
    }

    [Fact]
    public async Task GenerateAsync_WithMessages_ReturnsSuggestions()
    {
        // Arrange
        _aiUtility.Setup(u => u.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("[\"What's the next step?\", \"Can you explain more?\", \"How does this compare to X?\"]");

        _threadService.Setup(s => s.GetMessageHistoryAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMessages(
                (ChatRole.User, "Tell me about React"),
                (ChatRole.Assistant, "React is a JavaScript library...")));

        var service = CreateService();

        // Act
        var suggestions = await service.GenerateAsync(Guid.NewGuid());

        // Assert
        Assert.Equal(3, suggestions.Count);
        Assert.Contains("What's the next step?", suggestions);
    }

    [Fact]
    public async Task GenerateAsync_MarkdownCodeFence_ParsesCorrectly()
    {
        // Arrange
        _aiUtility.Setup(u => u.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("```json\n[\"Question 1?\", \"Question 2?\"]\n```");

        _threadService.Setup(s => s.GetMessageHistoryAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMessages(
                (ChatRole.User, "Hello")));

        var service = CreateService();

        // Act
        var suggestions = await service.GenerateAsync(Guid.NewGuid());

        // Assert
        Assert.Equal(2, suggestions.Count);
    }

    [Fact]
    public async Task GenerateAsync_NoMessages_ReturnsEmpty()
    {
        // Arrange
        _threadService.Setup(s => s.GetMessageHistoryAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChatMessage>());

        var service = CreateService();

        // Act
        var suggestions = await service.GenerateAsync(Guid.NewGuid());

        // Assert
        Assert.Empty(suggestions);
    }

    [Fact]
    public async Task GenerateAsync_AiUtilityThrows_ReturnsEmpty()
    {
        // Arrange
        _aiUtility.Setup(u => u.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("AI failed"));

        _threadService.Setup(s => s.GetMessageHistoryAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMessages(
                (ChatRole.User, "Hello")));

        var service = CreateService();

        // Act
        var suggestions = await service.GenerateAsync(Guid.NewGuid());

        // Assert
        Assert.Empty(suggestions);
    }

    [Fact]
    public async Task GenerateAsync_RespectsCount()
    {
        // Arrange
        _aiUtility.Setup(u => u.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("[\"Q1?\", \"Q2?\", \"Q3?\", \"Q4?\", \"Q5?\"]");

        _threadService.Setup(s => s.GetMessageHistoryAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMessages(
                (ChatRole.User, "Hello"),
                (ChatRole.Assistant, "Hi there!")));

        var service = CreateService();

        // Act — request only 2
        var suggestions = await service.GenerateAsync(Guid.NewGuid(), count: 2);

        // Assert
        Assert.Equal(2, suggestions.Count);
    }

    [Fact]
    public async Task GenerateAsync_ChineseConversation_DetectsChinese()
    {
        // Arrange
        _aiUtility.Setup(u => u.ExecuteAsync(
                It.IsAny<string>(),
                It.Is<string>(prompt => prompt.Contains("Chinese") || prompt.Contains("中文")),
                It.IsAny<AiUtilityCallOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("[\"这个功能如何部署？\", \"有什么替代方案？\"]");

        _threadService.Setup(s => s.GetMessageHistoryAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMessages(
                (ChatRole.User, "请介绍一下这个框架"),
                (ChatRole.Assistant, "这是一个模块化框架...")));

        var service = CreateService();

        // Act
        var suggestions = await service.GenerateAsync(Guid.NewGuid());

        // Assert
        Assert.Equal(2, suggestions.Count);

        // Verify the prompt contained Chinese language instruction
        _aiUtility.Verify(u => u.ExecuteAsync(
            It.IsAny<string>(),
            It.Is<string>(prompt => prompt.Contains("Chinese")),
            It.IsAny<AiUtilityCallOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateAsync_UsesModelFromOptions()
    {
        // Arrange
        var options = new SuggestionOptions { ModelName = "gpt-4o-mini" };

        _aiUtility.Setup(u => u.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.Is<AiUtilityCallOptions?>(o => o != null && o.Model == "gpt-4o-mini"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("[\"Q1?\"]");

        _threadService.Setup(s => s.GetMessageHistoryAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMessages(
                (ChatRole.User, "Hello")));

        var service = CreateService(options);

        // Act
        await service.GenerateAsync(Guid.NewGuid());

        // Assert — verify the model was passed through
        _aiUtility.Verify(u => u.ExecuteAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.Is<AiUtilityCallOptions?>(o => o != null && o.Model == "gpt-4o-mini"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
