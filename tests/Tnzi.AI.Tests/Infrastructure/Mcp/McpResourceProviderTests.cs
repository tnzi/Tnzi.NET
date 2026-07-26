using Tnzi.AI.Infrastructure.Mcp;

namespace Tnzi.AI.Tests.Infrastructure.Mcp;

/// <summary>
/// McpResourceProvider 单元测试 - 资源发现与读取
/// </summary>
public class McpResourceProviderTests
{
    private readonly Mock<IOptions<AIOptions>> _options = new();
    private readonly Mock<IMcpClientFactory> _clientFactory = new();
    private readonly Mock<ILogger<McpResourceProvider>> _logger = new();

    [Fact]
    public async Task ListResourcesAsync_ReturnsResourcesFromServer()
    {
        // Arrange
        var server = new McpServerConfig { Name = "test-server" };
        var aiOptions = CreateOptions(server);
        _options.Setup(x => x.Value).Returns(aiOptions);

        var mockAdapter = new Mock<IMcpClientAdapter>();
        mockAdapter.Setup(x => x.ListResourcesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<McpResourceInfo>
            {
                new("file:///docs/readme.md", "README", "text/markdown", "Project readme"),
                new("file:///docs/api.md", "API Docs", "text/markdown", null)
            });

        _clientFactory.Setup(x => x.GetOrCreateClientAsync(
                It.Is<McpServerConfig>(s => s.Name == "test-server"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAdapter.Object);

        var provider = CreateProvider();

        // Act
        var resources = await provider.ListResourcesAsync("test-server");

        // Assert
        resources.ShouldNotBeNull();
        resources.Count.ShouldBe(2);
        resources[0].Uri.ShouldBe("file:///docs/readme.md");
        resources[0].Name.ShouldBe("README");
        resources[0].MimeType.ShouldBe("text/markdown");
        resources[0].Description.ShouldBe("Project readme");
        resources[1].Description.ShouldBeNull();
    }

    [Fact]
    public async Task ListResourcesAsync_ServerNotConfigured_ReturnsEmpty()
    {
        // Arrange
        var aiOptions = CreateOptions();
        _options.Setup(x => x.Value).Returns(aiOptions);

        var provider = CreateProvider();

        // Act
        var resources = await provider.ListResourcesAsync("non-existent");

        // Assert
        resources.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListResourcesAsync_ServerError_ReturnsEmptyAndLogsWarning()
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
        var resources = await provider.ListResourcesAsync("broken-server");

        // Assert
        resources.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListResourcesAsync_McpDisabled_ReturnsEmpty()
    {
        // Arrange
        var server = new McpServerConfig { Name = "test-server" };
        var aiOptions = CreateOptions(server);
        aiOptions.Mcp!.Enabled = false;
        _options.Setup(x => x.Value).Returns(aiOptions);

        var provider = CreateProvider();

        // Act
        var resources = await provider.ListResourcesAsync("test-server");

        // Assert
        resources.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListAllResourcesAsync_AggregatesFromAllServers()
    {
        // Arrange
        var server1 = new McpServerConfig { Name = "server1" };
        var server2 = new McpServerConfig { Name = "server2" };
        var aiOptions = CreateOptions(server1, server2);
        _options.Setup(x => x.Value).Returns(aiOptions);

        var mockAdapter1 = new Mock<IMcpClientAdapter>();
        mockAdapter1.Setup(x => x.ListResourcesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<McpResourceInfo>
            {
                new("file:///a.md", "A", null, null)
            });

        var mockAdapter2 = new Mock<IMcpClientAdapter>();
        mockAdapter2.Setup(x => x.ListResourcesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<McpResourceInfo>
            {
                new("file:///b.md", "B", null, null),
                new("file:///c.md", "C", null, null)
            });

        _clientFactory.Setup(x => x.GetOrCreateClientAsync(
                It.Is<McpServerConfig>(s => s.Name == "server1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAdapter1.Object);
        _clientFactory.Setup(x => x.GetOrCreateClientAsync(
                It.Is<McpServerConfig>(s => s.Name == "server2"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAdapter2.Object);

        var provider = CreateProvider();

        // Act
        var resources = await provider.ListAllResourcesAsync();

        // Assert
        resources.Count.ShouldBe(3);
        resources.ShouldContain(r => r.Name == "A");
        resources.ShouldContain(r => r.Name == "B");
        resources.ShouldContain(r => r.Name == "C");
    }

    [Fact]
    public async Task ListAllResourcesAsync_OneServerFails_StillReturnsOtherServerResources()
    {
        // Arrange
        var server1 = new McpServerConfig { Name = "good-server" };
        var server2 = new McpServerConfig { Name = "bad-server" };
        var aiOptions = CreateOptions(server1, server2);
        _options.Setup(x => x.Value).Returns(aiOptions);

        var mockAdapter1 = new Mock<IMcpClientAdapter>();
        mockAdapter1.Setup(x => x.ListResourcesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<McpResourceInfo>
            {
                new("file:///good.md", "Good", null, null)
            });

        _clientFactory.Setup(x => x.GetOrCreateClientAsync(
                It.Is<McpServerConfig>(s => s.Name == "good-server"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAdapter1.Object);
        _clientFactory.Setup(x => x.GetOrCreateClientAsync(
                It.Is<McpServerConfig>(s => s.Name == "bad-server"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection failed"));

        var provider = CreateProvider();

        // Act
        var resources = await provider.ListAllResourcesAsync();

        // Assert
        resources.Count.ShouldBe(1);
        resources[0].Name.ShouldBe("Good");
    }

    [Fact]
    public async Task ListAllResourcesAsync_McpDisabled_ReturnsEmpty()
    {
        // Arrange
        var aiOptions = CreateOptions();
        aiOptions.Mcp!.Enabled = false;
        _options.Setup(x => x.Value).Returns(aiOptions);

        var provider = CreateProvider();

        // Act
        var resources = await provider.ListAllResourcesAsync();

        // Assert
        resources.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListAllResourcesAsync_NoServers_ReturnsEmpty()
    {
        // Arrange
        var aiOptions = CreateOptions();
        _options.Setup(x => x.Value).Returns(aiOptions);

        var provider = CreateProvider();

        // Act
        var resources = await provider.ListAllResourcesAsync();

        // Assert
        resources.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReadResourceAsync_ReturnsContent()
    {
        // Arrange
        var server = new McpServerConfig { Name = "test-server" };
        var aiOptions = CreateOptions(server);
        _options.Setup(x => x.Value).Returns(aiOptions);

        var mockAdapter = new Mock<IMcpClientAdapter>();
        mockAdapter.Setup(x => x.ReadResourceAsync("file:///docs/readme.md", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new McpResourceContent("# Hello World", "text/markdown"));

        _clientFactory.Setup(x => x.GetOrCreateClientAsync(
                It.Is<McpServerConfig>(s => s.Name == "test-server"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAdapter.Object);

        var provider = CreateProvider();

        // Act
        var content = await provider.ReadResourceAsync("test-server", "file:///docs/readme.md");

        // Assert
        content.ShouldNotBeNull();
        content.Text.ShouldBe("# Hello World");
        content.MimeType.ShouldBe("text/markdown");
    }

    [Fact]
    public async Task ReadResourceAsync_ServerNotConfigured_ReturnsNull()
    {
        // Arrange
        var aiOptions = CreateOptions();
        _options.Setup(x => x.Value).Returns(aiOptions);

        var provider = CreateProvider();

        // Act
        var content = await provider.ReadResourceAsync("non-existent", "file:///any.md");

        // Assert
        content.ShouldBeNull();
    }

    [Fact]
    public async Task ReadResourceAsync_ServerError_ReturnsNullAndLogsWarning()
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
        var content = await provider.ReadResourceAsync("broken-server", "file:///any.md");

        // Assert
        content.ShouldBeNull();
    }

    [Fact]
    public async Task ReadResourceAsync_McpDisabled_ReturnsNull()
    {
        // Arrange
        var server = new McpServerConfig { Name = "test-server" };
        var aiOptions = CreateOptions(server);
        aiOptions.Mcp!.Enabled = false;
        _options.Setup(x => x.Value).Returns(aiOptions);

        var provider = CreateProvider();

        // Act
        var content = await provider.ReadResourceAsync("test-server", "file:///any.md");

        // Assert
        content.ShouldBeNull();
    }

    [Fact]
    public async Task ListAllResourcesAsync_SkipsServersWithEmptyName()
    {
        // Arrange
        var server1 = new McpServerConfig { Name = "valid" };
        var server2 = new McpServerConfig { Name = "" };
        var aiOptions = CreateOptions(server1, server2);
        _options.Setup(x => x.Value).Returns(aiOptions);

        var mockAdapter = new Mock<IMcpClientAdapter>();
        mockAdapter.Setup(x => x.ListResourcesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<McpResourceInfo>
            {
                new("file:///a.md", "A", null, null)
            });

        _clientFactory.Setup(x => x.GetOrCreateClientAsync(
                It.Is<McpServerConfig>(s => s.Name == "valid"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAdapter.Object);

        var provider = CreateProvider();

        // Act
        var resources = await provider.ListAllResourcesAsync();

        // Assert
        resources.Count.ShouldBe(1);
    }

    private McpResourceProvider CreateProvider()
    {
        return new McpResourceProvider(new OptionsBackedMcpServerCatalog(_options.Object), _clientFactory.Object, _logger.Object);
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
