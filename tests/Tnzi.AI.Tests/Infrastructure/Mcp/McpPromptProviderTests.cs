using Tnzi.AI.Infrastructure.Mcp;

namespace Tnzi.AI.Tests.Infrastructure.Mcp;

/// <summary>
/// McpPromptProvider 单元测试 — Prompt 模板发现与获取
/// </summary>
public class McpPromptProviderTests
{
    private readonly Mock<IOptions<AIOptions>> _options = new();
    private readonly Mock<IMcpClientFactory> _clientFactory = new();
    private readonly Mock<ILogger<McpPromptProvider>> _logger = new();

    [Fact]
    public async Task ListPromptsAsync_ReturnsPromptsFromServer()
    {
        // Arrange
        var server = new McpServerConfig { Name = "test-server" };
        var aiOptions = CreateOptions(server);
        _options.Setup(x => x.Value).Returns(aiOptions);

        var mockAdapter = new Mock<IMcpClientAdapter>();
        mockAdapter.Setup(x => x.ListPromptsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<McpPromptInfo>
            {
                new("summarize", "Summarize text", new List<McpPromptArgument>
                {
                    new("text", "Text to summarize", true)
                }),
                new("translate", "Translate text", null)
            });

        _clientFactory.Setup(x => x.GetOrCreateClientAsync(
                It.Is<McpServerConfig>(s => s.Name == "test-server"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAdapter.Object);

        var provider = CreateProvider();

        // Act
        var prompts = await provider.ListPromptsAsync("test-server");

        // Assert
        prompts.ShouldNotBeNull();
        prompts.Count.ShouldBe(2);
        prompts[0].Name.ShouldBe("summarize");
        prompts[0].Description.ShouldBe("Summarize text");
        prompts[0].Arguments.ShouldNotBeNull();
        prompts[0].Arguments!.Count.ShouldBe(1);
        prompts[0].Arguments![0].Name.ShouldBe("text");
        prompts[0].Arguments![0].Required.ShouldBeTrue();
        prompts[1].Name.ShouldBe("translate");
        prompts[1].Arguments.ShouldBeNull();
    }

    [Fact]
    public async Task ListPromptsAsync_ServerNotConfigured_ReturnsEmpty()
    {
        // Arrange
        var aiOptions = CreateOptions();
        _options.Setup(x => x.Value).Returns(aiOptions);

        var provider = CreateProvider();

        // Act
        var prompts = await provider.ListPromptsAsync("non-existent");

        // Assert
        prompts.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListPromptsAsync_ServerError_ReturnsEmptyAndLogsWarning()
    {
        // Arrange
        var server = new McpServerConfig { Name = "broken-server" };
        var aiOptions = CreateOptions(server);
        _options.Setup(x => x.Value).Returns(aiOptions);

        _clientFactory.Setup(x => x.GetOrCreateClientAsync(
                It.Is<McpServerConfig>(s => s.Name == "broken-server"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection failed"));

        var provider = CreateProvider();

        // Act
        var prompts = await provider.ListPromptsAsync("broken-server");

        // Assert
        prompts.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListPromptsAsync_McpDisabled_ReturnsEmpty()
    {
        // Arrange
        var server = new McpServerConfig { Name = "test-server" };
        var aiOptions = CreateOptions(server);
        aiOptions.Mcp!.Enabled = false;
        _options.Setup(x => x.Value).Returns(aiOptions);

        var provider = CreateProvider();

        // Act
        var prompts = await provider.ListPromptsAsync("test-server");

        // Assert
        prompts.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetPromptAsync_ReturnsPromptResult()
    {
        // Arrange
        var server = new McpServerConfig { Name = "test-server" };
        var aiOptions = CreateOptions(server);
        _options.Setup(x => x.Value).Returns(aiOptions);

        var mockAdapter = new Mock<IMcpClientAdapter>();
        mockAdapter.Setup(x => x.GetPromptAsync("summarize",
                It.Is<Dictionary<string, string>>(d => d["text"] == "Hello world"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new McpPromptResult(new List<McpPromptMessage>
            {
                new("user", "Please summarize: Hello world"),
                new("assistant", "Summary: A greeting.")
            }));

        _clientFactory.Setup(x => x.GetOrCreateClientAsync(
                It.Is<McpServerConfig>(s => s.Name == "test-server"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAdapter.Object);

        var provider = CreateProvider();
        var args = new Dictionary<string, string> { ["text"] = "Hello world" };

        // Act
        var result = await provider.GetPromptAsync("test-server", "summarize", args);

        // Assert
        result.ShouldNotBeNull();
        result!.Messages.Count.ShouldBe(2);
        result.Messages[0].Role.ShouldBe("user");
        result.Messages[0].Content.ShouldBe("Please summarize: Hello world");
        result.Messages[1].Role.ShouldBe("assistant");
    }

    [Fact]
    public async Task GetPromptAsync_WithoutArguments_ReturnsPromptResult()
    {
        // Arrange
        var server = new McpServerConfig { Name = "test-server" };
        var aiOptions = CreateOptions(server);
        _options.Setup(x => x.Value).Returns(aiOptions);

        var mockAdapter = new Mock<IMcpClientAdapter>();
        mockAdapter.Setup(x => x.GetPromptAsync("greeting", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new McpPromptResult(new List<McpPromptMessage>
            {
                new("user", "Say hello")
            }));

        _clientFactory.Setup(x => x.GetOrCreateClientAsync(
                It.Is<McpServerConfig>(s => s.Name == "test-server"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAdapter.Object);

        var provider = CreateProvider();

        // Act
        var result = await provider.GetPromptAsync("test-server", "greeting");

        // Assert
        result.ShouldNotBeNull();
        result!.Messages.Count.ShouldBe(1);
    }

    [Fact]
    public async Task GetPromptAsync_ServerNotConfigured_ReturnsNull()
    {
        // Arrange
        var aiOptions = CreateOptions();
        _options.Setup(x => x.Value).Returns(aiOptions);

        var provider = CreateProvider();

        // Act
        var result = await provider.GetPromptAsync("non-existent", "any-prompt");

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetPromptAsync_ServerError_ReturnsNullAndLogsWarning()
    {
        // Arrange
        var server = new McpServerConfig { Name = "broken-server" };
        var aiOptions = CreateOptions(server);
        _options.Setup(x => x.Value).Returns(aiOptions);

        _clientFactory.Setup(x => x.GetOrCreateClientAsync(
                It.Is<McpServerConfig>(s => s.Name == "broken-server"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection failed"));

        var provider = CreateProvider();

        // Act
        var result = await provider.GetPromptAsync("broken-server", "any-prompt");

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetPromptAsync_McpDisabled_ReturnsNull()
    {
        // Arrange
        var server = new McpServerConfig { Name = "test-server" };
        var aiOptions = CreateOptions(server);
        aiOptions.Mcp!.Enabled = false;
        _options.Setup(x => x.Value).Returns(aiOptions);

        var provider = CreateProvider();

        // Act
        var result = await provider.GetPromptAsync("test-server", "any-prompt");

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task ListAllPromptsAsync_AggregatesFromAllServers()
    {
        // Arrange
        var server1 = new McpServerConfig { Name = "server1" };
        var server2 = new McpServerConfig { Name = "server2" };
        var aiOptions = CreateOptions(server1, server2);
        _options.Setup(x => x.Value).Returns(aiOptions);

        var mockAdapter1 = new Mock<IMcpClientAdapter>();
        mockAdapter1.Setup(x => x.ListPromptsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<McpPromptInfo>
            {
                new("summarize", "Summarize", null)
            });

        var mockAdapter2 = new Mock<IMcpClientAdapter>();
        mockAdapter2.Setup(x => x.ListPromptsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<McpPromptInfo>
            {
                new("translate", "Translate", null),
                new("review", "Code review", null)
            });

        _clientFactory.Setup(x => x.GetOrCreateClientAsync(
                It.Is<McpServerConfig>(s => s.Name == "server1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAdapter1.Object);
        _clientFactory.Setup(x => x.GetOrCreateClientAsync(
                It.Is<McpServerConfig>(s => s.Name == "server2"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAdapter2.Object);

        var provider = CreateProvider();

        // Act
        var prompts = await provider.ListAllPromptsAsync();

        // Assert
        prompts.Count.ShouldBe(3);
        prompts.ShouldContain(p => p.Name == "summarize");
        prompts.ShouldContain(p => p.Name == "translate");
        prompts.ShouldContain(p => p.Name == "review");
    }

    [Fact]
    public async Task ListAllPromptsAsync_OneServerFails_StillReturnsOtherServerPrompts()
    {
        // Arrange
        var server1 = new McpServerConfig { Name = "good-server" };
        var server2 = new McpServerConfig { Name = "bad-server" };
        var aiOptions = CreateOptions(server1, server2);
        _options.Setup(x => x.Value).Returns(aiOptions);

        var mockAdapter1 = new Mock<IMcpClientAdapter>();
        mockAdapter1.Setup(x => x.ListPromptsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<McpPromptInfo>
            {
                new("good-prompt", "Good", null)
            });

        _clientFactory.Setup(x => x.GetOrCreateClientAsync(
                It.Is<McpServerConfig>(s => s.Name == "good-server"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAdapter1.Object);
        _clientFactory.Setup(x => x.GetOrCreateClientAsync(
                It.Is<McpServerConfig>(s => s.Name == "bad-server"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection failed"));

        var provider = CreateProvider();

        // Act
        var prompts = await provider.ListAllPromptsAsync();

        // Assert
        prompts.Count.ShouldBe(1);
        prompts[0].Name.ShouldBe("good-prompt");
    }

    [Fact]
    public async Task ListAllPromptsAsync_McpDisabled_ReturnsEmpty()
    {
        // Arrange
        var aiOptions = CreateOptions();
        aiOptions.Mcp!.Enabled = false;
        _options.Setup(x => x.Value).Returns(aiOptions);

        var provider = CreateProvider();

        // Act
        var prompts = await provider.ListAllPromptsAsync();

        // Assert
        prompts.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListAllPromptsAsync_NoServers_ReturnsEmpty()
    {
        // Arrange
        var aiOptions = CreateOptions();
        _options.Setup(x => x.Value).Returns(aiOptions);

        var provider = CreateProvider();

        // Act
        var prompts = await provider.ListAllPromptsAsync();

        // Assert
        prompts.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListAllPromptsAsync_SkipsServersWithEmptyName()
    {
        // Arrange
        var server1 = new McpServerConfig { Name = "valid" };
        var server2 = new McpServerConfig { Name = "" };
        var aiOptions = CreateOptions(server1, server2);
        _options.Setup(x => x.Value).Returns(aiOptions);

        var mockAdapter = new Mock<IMcpClientAdapter>();
        mockAdapter.Setup(x => x.ListPromptsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<McpPromptInfo>
            {
                new("prompt-a", "A", null)
            });

        _clientFactory.Setup(x => x.GetOrCreateClientAsync(
                It.Is<McpServerConfig>(s => s.Name == "valid"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAdapter.Object);

        var provider = CreateProvider();

        // Act
        var prompts = await provider.ListAllPromptsAsync();

        // Assert
        prompts.Count.ShouldBe(1);
    }

    private McpPromptProvider CreateProvider()
    {
        return new McpPromptProvider(_options.Object, _clientFactory.Object, _logger.Object);
    }

    private static AIOptions CreateOptions(params McpServerConfig[] servers)
    {
        return new AIOptions
        {
            Mcp = new McpOptions
            {
                Enabled = true,
                Servers = servers.ToList()
            }
        };
    }
}
