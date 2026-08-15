using Microsoft.Extensions.DependencyInjection;
using System.Net;

namespace Tnzi.AspNetCore.Tests.Middleware;

/// <summary>
/// 限流的分区来源，以及取不到分区键时的处置。
///
/// 守的是一条容易静默失效的线：内置判定对匿名请求只有来源地址一个维度可用，
/// 而有些部署**刻意不采集来源地址**。两者叠加时匿名端点没有分区键，
/// 旧行为是直接放行 —— 不报错、不告警、日志里也看不出来，
/// 而配置里明明写着限流是开着的。
/// </summary>
public class RateLimitPartitionKeyTests
{
    private sealed class StaticMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    /// <summary>记录被问到的限流键，并按需报告超限。</summary>
    private sealed class RecordingRateLimitService(long count = 1) : IRateLimitService
    {
        public List<string> Keys { get; } = [];

        public Task<long> IncrementAndGetAsync(
            string key, int windowSeconds, RateLimitAlgorithm algorithm = RateLimitAlgorithm.FixedWindow)
        {
            Keys.Add(key);
            return Task.FromResult(count);
        }
    }

    private sealed class StubPartitionProvider(string? key, int order = 0) : IRateLimitPartitionKeyProvider
    {
        public int Order => order;
        public string? GetPartitionKey(HttpContext context) => key;
    }

    private static AspNetCoreOptions OptionsWith(
        MissingPartitionKeyBehavior missing = MissingPartitionKeyBehavior.Allow,
        bool collectIp = true)
        => new()
        {
            CollectClientIpAddress = collectIp,
            RateLimit = new RateLimitOptions
            {
                Enabled = true,
                DefaultLimit = 10,
                DefaultWindowSeconds = 60,
                MissingPartitionKey = missing
            }
        };

    /// <summary>跑一次中间件，返回「后续管道是否被调用」与限流服务看到的键。</summary>
    private static async Task<(bool nextCalled, int statusCode, RecordingRateLimitService service)> RunAsync(
        AspNetCoreOptions options,
        IEnumerable<IRateLimitPartitionKeyProvider>? providers = null,
        string? remoteIp = "203.0.113.7",
        long count = 1)
    {
        var nextCalled = false;
        var middleware = new RateLimitingMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            new StaticMonitor<AspNetCoreOptions>(options),
            new Mock<ILogger<RateLimitingMiddleware>>().Object);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/tips";
        context.Response.Body = new MemoryStream();
        if (remoteIp != null)
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        }

        var services = new ServiceCollection();
        services.AddSingleton<IOptionsMonitor<AspNetCoreOptions>>(new StaticMonitor<AspNetCoreOptions>(options));
        foreach (var provider in providers ?? [])
        {
            services.AddSingleton(provider);
        }

        context.RequestServices = services.BuildServiceProvider();

        var rateLimitService = new RecordingRateLimitService(count);
        await middleware.InvokeAsync(context, rateLimitService);

        return (nextCalled, context.Response.StatusCode, rateLimitService);
    }

    // ---- 分区来源 -----------------------------------------------------------

    [Fact]
    public async Task WithNoProvider_AnonymousRequestsArePartitionedByAddress()
    {
        // 既有行为，不能被这次改动动到。
        var (nextCalled, _, service) = await RunAsync(OptionsWith());

        Assert.True(nextCalled);
        Assert.Equal("ip:203.0.113.7:/api/tips", Assert.Single(service.Keys));
    }

    [Fact]
    public async Task ACustomProvider_SuppliesThePartition()
    {
        // 部署给出了自己的分区维度，就该由它说了算。
        var (nextCalled, _, service) = await RunAsync(
            OptionsWith(), [new StubPartitionProvider("ticket:abc123")]);

        Assert.True(nextCalled);
        Assert.Equal("ticket:abc123:/api/tips", Assert.Single(service.Keys));
    }

    [Fact]
    public async Task AProviderThatCannotDecide_FallsThroughToTheNextOne()
    {
        // 返回 null 表示「本提供者无法判定」，不是「不限流」。
        var (_, _, service) = await RunAsync(
            OptionsWith(),
            [new StubPartitionProvider(null, order: 0), new StubPartitionProvider("ticket:xyz", order: 1)]);

        Assert.Equal("ticket:xyz:/api/tips", Assert.Single(service.Keys));
    }

    [Fact]
    public async Task ProvidersAreAskedInOrder()
    {
        var (_, _, service) = await RunAsync(
            OptionsWith(),
            [new StubPartitionProvider("late", order: 10), new StubPartitionProvider("early", order: 1)]);

        Assert.Equal("early:/api/tips", Assert.Single(service.Keys));
    }

    [Fact]
    public async Task WhenAllProvidersDecline_ItFallsBackToTheBuiltInJudgement()
    {
        var (_, _, service) = await RunAsync(OptionsWith(), [new StubPartitionProvider(null)]);

        Assert.Equal("ip:203.0.113.7:/api/tips", Assert.Single(service.Keys));
    }

    // ---- 取不到分区键时的处置 -------------------------------------------------

    [Fact]
    public async Task WithoutAddressCollection_AndNoProvider_ThereIsNoPartitionKey()
    {
        // 这就是让限流静默失效的那个组合：匿名请求 + 关闭地址采集。
        // 默认行为仍是放行（兼容），但请求绝不该被计入任何额度。
        var (nextCalled, statusCode, service) = await RunAsync(OptionsWith(collectIp: false));

        Assert.True(nextCalled);
        Assert.Equal(200, statusCode);
        Assert.Empty(service.Keys);
    }

    [Fact]
    public async Task ConfiguredToDeny_TheRequestIsRejected()
    {
        var (nextCalled, statusCode, service) = await RunAsync(
            OptionsWith(MissingPartitionKeyBehavior.Deny, collectIp: false));

        Assert.False(nextCalled);
        Assert.Equal(429, statusCode);
        Assert.Empty(service.Keys);
    }

    [Fact]
    public async Task ConfiguredToGlobal_TheRequestFallsIntoAPerPathBucket()
    {
        var (nextCalled, _, service) = await RunAsync(
            OptionsWith(MissingPartitionKeyBehavior.Global, collectIp: false));

        Assert.True(nextCalled);
        Assert.Equal("global:/api/tips", Assert.Single(service.Keys));
    }

    [Fact]
    public async Task ConfiguredToGlobal_TheBucketStillEnforcesTheLimit()
    {
        // 全局桶保证「总量有上限」，虽然挡不住单个调用方占满额度。
        var (nextCalled, statusCode, _) = await RunAsync(
            OptionsWith(MissingPartitionKeyBehavior.Global, collectIp: false), count: 999);

        Assert.False(nextCalled);
        Assert.Equal(429, statusCode);
    }

    [Fact]
    public async Task ACustomProvider_RescuesTheDeploymentThatCollectsNoAddress()
    {
        // 这是这套契约存在的理由：不采集来源地址，同时限流照常生效。
        var (nextCalled, _, service) = await RunAsync(
            OptionsWith(collectIp: false), [new StubPartitionProvider("ticket:abc123")]);

        Assert.True(nextCalled);
        Assert.Equal("ticket:abc123:/api/tips", Assert.Single(service.Keys));
    }
}
