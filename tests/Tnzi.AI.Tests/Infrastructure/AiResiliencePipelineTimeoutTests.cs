using System.Net;
using Microsoft.Extensions.Http.Resilience;

namespace Tnzi.AI.Tests.Infrastructure;

/// <summary>
/// 提供商客户端的 Polly 标准管线不另设比 <c>TimeoutSeconds</c> 更短的超时。
/// </summary>
/// <remarks>
/// 标准管线默认每次尝试 10 秒、总计 30 秒：<c>TimeoutSeconds=300</c> 的非流式调用在第 10 秒被掐断、重试（每次都可能计费），
/// 30 秒整体失败，而同一份配置在核心 <c>IAiUtility</c> 默认实现下按 300 秒生效。
/// </remarks>
public class AiResiliencePipelineTimeoutTests
{
    [Theory]
    [InlineData(300)]
    [InlineData(600)]
    [InlineData(5)]
    public async Task Pipeline_AttemptTimeoutFollowsTheProviderTimeout_AndStillValidates(int timeoutSeconds)
    {
        var attempt = TimeSpan.FromSeconds(timeoutSeconds);
        await using var sp = BuildClient("tnzi-test", attempt, out var handler);

        var options = sp.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>().Get("tnzi-test-standard");

        options.AttemptTimeout.Timeout.ShouldBe(attempt);
        options.TotalRequestTimeout.Timeout.ShouldBeGreaterThan(attempt * 4,
            "the total budget must outlast every attempt, or it cuts the request short before the attempt timeout does");
        options.CircuitBreaker.SamplingDuration.ShouldBeGreaterThanOrEqualTo(attempt * 2);

        // 真的发一次请求：管线在首次使用时才构建并校验（SamplingDuration >= 2 × AttemptTimeout 等不变量）。
        using var response = await sp.GetRequiredService<IHttpClientFactory>().CreateClient("tnzi-test").GetAsync("http://provider.test/v1/models");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        handler.Calls.ShouldBe(1);
    }

    [Fact]
    public void ResolveProviderAttemptTimeout_UsesTimeoutSeconds_OrTheDefault()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Providers:Slow:TimeoutSeconds"] = "300",
                ["AI:Providers:Plain:BaseUrl"] = "https://example.test",
                ["AI:Providers:Broken:TimeoutSeconds"] = "0"
            })
            .Build();
        var providers = configuration.GetSection("AI:Providers");

        AIModule.ResolveProviderAttemptTimeout(providers.GetSection("Slow")).ShouldBe(TimeSpan.FromSeconds(300));
        AIModule.ResolveProviderAttemptTimeout(providers.GetSection("Plain")).ShouldBe(AIModule.DefaultProviderAttemptTimeout);
        AIModule.ResolveProviderAttemptTimeout(providers.GetSection("Broken")).ShouldBe(AIModule.DefaultProviderAttemptTimeout);
    }

    [Fact]
    public void FallbackPipeline_AllowsTheLargestTimeoutAProviderMayConfigure()
    {
        // 运行期才出现的提供商走回退管线，注册时读不到它们的 TimeoutSeconds。
        AIModule.FallbackAttemptTimeout.ShouldBe(TimeSpan.FromSeconds(600));
    }

    private static ServiceProvider BuildClient(string name, TimeSpan attemptTimeout, out CountingHandler handler)
    {
        var counting = new CountingHandler();
        handler = counting;
        var services = new ServiceCollection();
        services.AddHttpClient(name)
            .ConfigurePrimaryHttpMessageHandler(() => counting)
            .AddStandardResilienceHandler(options => AIModule.ConfigureAiResilience(options, attemptTimeout));
        return services.BuildServiceProvider();
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private int _calls;
        public int Calls => _calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
