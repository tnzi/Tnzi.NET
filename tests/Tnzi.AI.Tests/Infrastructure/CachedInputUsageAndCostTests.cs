using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using Anthropic;
using Microsoft.AspNetCore.Http;
using OpenAI;

namespace Tnzi.AI.Tests.Infrastructure;

/// <summary>
/// 缓存命中的输入 Token：从真实适配器读出来，按 <c>CachedInputCostPer1MTokens</c> 计价。
/// </summary>
/// <remarks>
/// OpenAI 与 Anthropic 的 MEAI 适配器都把缓存命中写在 <see cref="UsageDetails.CachedInputTokenCount"/>，
/// 且都把它算进 <see cref="UsageDetails.InputTokenCount"/>（Anthropic 的输入数还含缓存写入）。
/// 此前执行器只查附加计数里的几个键名，两家都不用那些键，记下的缓存数恒为 0；
/// 而配置里的缓存价从未参与计算，缓存命中按全价计。
/// </remarks>
public class CachedInputUsageAndCostTests
{
    [Fact]
    public async Task Executor_AnthropicAdapter_RecordsCacheReadAndCacheCreation()
    {
        const string response = """
            {"id":"msg_test","type":"message","role":"assistant","model":"claude-sonnet-4-5",
             "content":[{"type":"text","text":"ok"}],
             "stop_reason":"end_turn","stop_sequence":null,
             "usage":{"input_tokens":100,"output_tokens":10,"cache_read_input_tokens":50,"cache_creation_input_tokens":20}}
            """;
        var client = new AnthropicClient { ApiKey = "k", HttpClient = new HttpClient(new FixedResponse(response)) }
            .AsIChatClient("claude-sonnet-4-5");

        var result = await new AgentExecutor(client, new AgentExecutorOptions { Name = "probe" })
            .ExecuteAsync([new ChatMessage(ChatRole.User, "hi")]);

        result.Usage.ShouldNotBeNull();
        result.Usage.InputTokens.ShouldBe(170, "the adapter folds cache reads and writes into the input count");
        result.Usage.CachedInputTokens.ShouldBe(50);
        result.Usage.CacheCreationTokens.ShouldBe(20);
    }

    [Fact]
    public async Task Executor_OpenAiAdapter_RecordsCachedPromptTokens()
    {
        const string response = """
            {"id":"chatcmpl-1","object":"chat.completion","created":1,"model":"gpt-4o",
             "choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}],
             "usage":{"prompt_tokens":100,"completion_tokens":10,"total_tokens":110,"prompt_tokens_details":{"cached_tokens":60}}}
            """;
        var client = new OpenAIClient(new ApiKeyCredential("k"), new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(new HttpClient(new FixedResponse(response)))
        }).GetChatClient("gpt-4o").AsIChatClient();

        var result = await new AgentExecutor(client, new AgentExecutorOptions { Name = "probe" })
            .ExecuteAsync([new ChatMessage(ChatRole.User, "hi")]);

        result.Usage.ShouldNotBeNull();
        result.Usage.InputTokens.ShouldBe(100);
        result.Usage.CachedInputTokens.ShouldBe(60);
        result.Usage.TotalTokens.ShouldBe(110);
    }

    [Fact]
    public void CostCalculator_CachedRateConfigured_PricesCacheHitsAtTheCachedRate()
    {
        var calculator = CreateCalculator(cachedRate: 0.5m);

        // 1000 输入里 600 命中缓存：400 × 5 + 600 × 0.5 + 500 × 15 = 9800 → 0.0098
        calculator.CalculateCost("OpenAI", "gpt-4o", 1000, 500, cachedInputTokens: 600).ShouldBe(0.0098m);
        calculator.CalculateCost("OpenAI", "gpt-4o", 1000, 500).ShouldBe(0.0125m);
    }

    [Fact]
    public void CostCalculator_NoCachedRate_PricesEveryInputTokenAtTheInputRate()
    {
        var calculator = CreateCalculator(cachedRate: null);

        calculator.CalculateCost("OpenAI", "gpt-4o", 1000, 500, cachedInputTokens: 600).ShouldBe(0.0125m);
    }

    [Fact]
    public void CostCalculator_CachedCountAboveInput_IsClamped()
    {
        var calculator = CreateCalculator(cachedRate: 0.5m);

        // 命中数不可能超过输入数；超出的部分不能把成本算成负数或少算输出
        calculator.CalculateCost("OpenAI", "gpt-4o", 1000, 500, cachedInputTokens: 5000).ShouldBe(0.008m);
    }

    [Fact]
    public async Task UsageLogService_CachedOverload_PassesTheCachedCountToTheCalculator()
    {
        var calculator = new RecordingCalculator();
        var repository = new Mock<IRepository<UsageLog, Guid>>();
        UsageLog? logged = null;
        repository.Setup(r => r.InsertAsync(It.IsAny<UsageLog>(), It.IsAny<CancellationToken>()))
            .Callback<UsageLog, CancellationToken>((log, _) => logged = log)
            .Returns(Task.CompletedTask);
        var service = new UsageLogService(
            repository.Object,
            new HttpContextAccessor(),
            new ServiceCollection().AddLogging().BuildServiceProvider(),
            calculator);

        await service.LogUsageAsync("chat", "OpenAI", "gpt-4o", 1000, 500, 10, true, null, null, null,
            cachedInputTokens: 600, cacheCreationTokens: 0);

        calculator.CachedInputTokens.ShouldBe(600);
        logged!.EstimatedCostUsd.ShouldBe(0.0098m);
        logged.CachedInputTokens.ShouldBe(600);
    }

    [Fact]
    public void CalculatorWithoutTheCachedOverload_FallsBackToTheFourArgumentPrice()
    {
        ICostCalculator legacy = new FourArgumentOnlyCalculator();

        legacy.CalculateCost("OpenAI", "gpt-4o", 1000, 500, cachedInputTokens: 600).ShouldBe(1m);
    }

    private static CostCalculator CreateCalculator(decimal? cachedRate)
        => new(new StaticOptionsMonitor<AIOptions>(new AIOptions
        {
            CostTracking = new CostTrackingOptions
            {
                Enabled = true,
                ModelCosts = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["OpenAI"] = new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["gpt-4o"] = new ModelCostRate
                        {
                            InputCostPer1MTokens = 5m,
                            OutputCostPer1MTokens = 15m,
                            CachedInputCostPer1MTokens = cachedRate
                        }
                    }
                }
            }
        }));

    private sealed class RecordingCalculator : ICostCalculator
    {
        private readonly CostCalculator _inner = CreateCalculator(cachedRate: 0.5m);

        public int? CachedInputTokens { get; private set; }

        public decimal? CalculateCost(string provider, string model, int inputTokens, int outputTokens)
            => _inner.CalculateCost(provider, model, inputTokens, outputTokens);

        public decimal? CalculateCost(string provider, string model, int inputTokens, int outputTokens, int cachedInputTokens)
        {
            CachedInputTokens = cachedInputTokens;
            return _inner.CalculateCost(provider, model, inputTokens, outputTokens, cachedInputTokens);
        }
    }

    /// <summary>只实现四参数版本的既有实现：缓存版本走接口的默认实现。</summary>
    private sealed class FourArgumentOnlyCalculator : ICostCalculator
    {
        public decimal? CalculateCost(string provider, string model, int inputTokens, int outputTokens) => 1m;
    }

    private sealed class FixedResponse(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }
}
