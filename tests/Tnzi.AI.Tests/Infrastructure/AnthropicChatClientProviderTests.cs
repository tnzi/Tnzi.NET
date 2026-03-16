using Moq;
using Tnzi.AI.Infrastructure.Providers;

namespace Tnzi.AI.Tests.Infrastructure;

/// <summary>
/// AnthropicChatClientProvider 单元测试
/// </summary>
public class AnthropicChatClientProviderTests
{
    private readonly Mock<IHttpClientFactory> _httpClientFactory = new();
    private readonly Mock<ILogger<AnthropicChatClientProvider>> _logger = new();

    public AnthropicChatClientProviderTests()
    {
        _httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient());
    }

    [Fact]
    public void ProviderName_ReturnsAnthropic()
    {
        var provider = CreateProvider();
        provider.ProviderName.ShouldBe("Anthropic");
    }

    [Fact]
    public void CreateChatClient_MissingApiKey_ThrowsInvalidOperationException()
    {
        var provider = CreateProvider();
        var options = new ProviderOptions { ApiKey = "" };

        Should.Throw<InvalidOperationException>(() => provider.CreateChatClient(options, "claude-3-sonnet"));
    }

    [Fact]
    public void CreateEmbeddingGenerator_ReturnsNull()
    {
        var provider = CreateProvider();
        var options = new ProviderOptions { ApiKey = "sk-ant-test" };

        var result = provider.CreateEmbeddingGenerator(options, "claude-3-sonnet");
        result.ShouldBeNull();
    }

    [Fact]
    public void CreateChatClient_ValidApiKey_ReturnsChatClient()
    {
        var provider = CreateProvider();
        var options = new ProviderOptions { ApiKey = "sk-ant-test" };

        var client = provider.CreateChatClient(options, "claude-3-sonnet");
        client.ShouldNotBeNull();
    }

    [Fact]
    public void CreateChatClient_WrapsInnerClientWithAnthropicThinkingDecorator()
    {
        var provider = CreateProvider();
        var options = new ProviderOptions { ApiKey = "sk-ant-test" };

        var client = provider.CreateChatClient(options, "claude-3-sonnet");

        client.ShouldBeOfType<AnthropicThinkingChatClient>();
    }

    [Fact]
    public void CreateNativeClient_ValidApiKey_ReturnsAnthropicClient()
    {
        var provider = CreateProvider();
        var options = new ProviderOptions { ApiKey = "sk-ant-test" };

        var client = provider.CreateNativeClient(options);
        client.ShouldNotBeNull();
        client.ShouldBeOfType<Anthropic.AnthropicClient>();
    }

    [Fact]
    public void CreateChatClient_WithBaseUrl_DoesNotThrow()
    {
        var provider = CreateProvider();
        var options = new ProviderOptions
        {
            ApiKey = "sk-ant-test",
            BaseUrl = "https://custom-proxy.example.com/"
        };

        var client = provider.CreateChatClient(options, "claude-3-sonnet");
        client.ShouldNotBeNull();
    }

    [Fact]
    public void CreateChatClient_WithTimeout_DoesNotThrow()
    {
        var provider = CreateProvider();
        var options = new ProviderOptions
        {
            ApiKey = "sk-ant-test",
            TimeoutSeconds = 60
        };

        var client = provider.CreateChatClient(options, "claude-3-sonnet");
        client.ShouldNotBeNull();
    }

    private AnthropicChatClientProvider CreateProvider()
        => new(_httpClientFactory.Object, _logger.Object);
}
