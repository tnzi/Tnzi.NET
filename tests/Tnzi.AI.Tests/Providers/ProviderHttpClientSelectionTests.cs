namespace Tnzi.AI.Tests.Providers;

/// <summary>
/// 内建 provider 必须按 <c>ProviderOptions.HttpClientName</c> 取命名客户端：工厂给租户自填端点指定的是
/// 带连接期 egress 检查、不跟随重定向的客户端，provider 若无视它照旧取默认客户端，那层守卫在运行时等于不存在。
/// </summary>
public class ProviderHttpClientSelectionTests
{
    [Fact]
    public void OpenAI_UsesTheNamedClientTheFactoryAssigned()
    {
        var httpClientFactory = RecordingFactory(out var requestedNames);
        var provider = new OpenAIChatClientProvider(httpClientFactory, NullLogger<OpenAIChatClientProvider>.Instance);

        provider.CreateChatClient(TenantEndpointOptions(), "gpt-4o");

        requestedNames.ShouldBe([ResilientHttpClientNames.TenantEndpoint]);
    }

    [Fact]
    public void Anthropic_UsesTheNamedClientTheFactoryAssigned()
    {
        var httpClientFactory = RecordingFactory(out var requestedNames);
        var provider = new AnthropicChatClientProvider(httpClientFactory, NullLogger<AnthropicChatClientProvider>.Instance);

        provider.CreateChatClient(TenantEndpointOptions(), "claude-sonnet-4");

        requestedNames.ShouldBe([ResilientHttpClientNames.TenantEndpoint]);
    }

    [Fact]
    public void WithoutAnAssignedClient_TheProviderKeepsItsResilientDefault()
    {
        var httpClientFactory = RecordingFactory(out var requestedNames);
        var provider = new AnthropicChatClientProvider(httpClientFactory, NullLogger<AnthropicChatClientProvider>.Instance);

        provider.CreateChatClient(new ProviderOptions { Name = "Unregistered", ApiKey = "key", BaseUrl = "https://api.example/v1" }, "claude-sonnet-4");

        requestedNames.ShouldBe([ResilientHttpClientNames.Fallback]);
    }

    /// <summary>
    /// 框架的命名客户端都挂着 Polly 管线，SDK 自己的重试要关掉，否则一次调用叠成多轮完整的 Polly 重试；
    /// 调用方另给的客户端名不保证有管线，SDK 重试照旧（null = SDK 默认）。
    /// </summary>
    [Theory]
    [InlineData(null, 0)]
    [InlineData(ResilientHttpClientNames.TenantEndpoint, 0)]
    [InlineData("caller-owned", null)]
    public void Anthropic_TurnsOffSdkRetriesOnlyOverAPollyPipeline(string? clientName, int? expectedMaxRetries)
    {
        var provider = new AnthropicChatClientProvider(RecordingFactory(out _), NullLogger<AnthropicChatClientProvider>.Instance);

        var client = provider.CreateNativeClient(new ProviderOptions { Name = "P", ApiKey = "key", HttpClientName = clientName })
            .ShouldBeOfType<global::Anthropic.AnthropicClient>();

        client.MaxRetries.ShouldBe(expectedMaxRetries);
    }

    private static ProviderOptions TenantEndpointOptions() => new()
    {
        Name = "TenantProvider",
        ApiKey = "tenant-key",
        BaseUrl = "https://tenant.example/v1",
        HttpClientName = ResilientHttpClientNames.TenantEndpoint
    };

    private static IHttpClientFactory RecordingFactory(out List<string> requestedNames)
    {
        var names = new List<string>();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns<string>(name =>
            {
                names.Add(name);
                return new HttpClient();
            });
        requestedNames = names;
        return factory.Object;
    }
}
