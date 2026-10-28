using System.Net;
using Tnzi.Services;

namespace Tnzi.Tests.Services;

/// <summary>
/// 自带的第三方 IP 定位实现（消费方显式注册才会启用）只对公网地址外呼，且同一个地址失败后不会每条记录都再等一次超时。
/// </summary>
/// <remarks>
/// 访问日志后台对批内每条记录逐条定位。失败结果此前不缓存：外呼被拦或被限流（免费接口每分钟 45 次）时，
/// 同一个来访地址的每一条日志都要再等一次最长 5 秒的超时，队列被拖满后访问日志被丢弃。
/// </remarks>
public class IpLocatorServiceTests
{
    private sealed class CountingHandler(HttpStatusCode status, string body = "") : HttpMessageHandler
    {
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private static IpLocatorService Create(CountingHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, disposeHandler: false));
        var cache = new MemoryCacheService(
            new MemoryCache(new MemoryCacheOptions()),
            Mock.Of<ILogger<MemoryCacheService>>(),
            Microsoft.Extensions.Options.Options.Create(new CachingOptions()));
        return new IpLocatorService(factory.Object, cache);
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("example.com")]
    [InlineData("1.2.3.4/../../admin")]
    public async Task AValueThatIsNotAnIpAddress_IsNeverSentOut(string value)
    {
        var handler = new CountingHandler(HttpStatusCode.OK, """{"status":"success","country":"X"}""");

        var result = await Create(handler).LocateAsync(value);

        Assert.Null(result);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("127.0.0.5")]
    [InlineData("::ffff:10.1.2.3")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("192.168.1.10")]
    public async Task ANonPublicAddress_IsAnsweredLocally_WithoutCallingOut(string address)
    {
        var handler = new CountingHandler(HttpStatusCode.OK, """{"status":"success","country":"X"}""");

        var result = await Create(handler).LocateAsync(address);

        Assert.NotNull(result);
        Assert.Equal("Local", result.Country);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task AFailedLookup_IsNotRetriedForTheSameAddress()
    {
        var handler = new CountingHandler(HttpStatusCode.TooManyRequests);
        var locator = Create(handler);

        Assert.Null(await locator.LocateAsync("203.0.113.7"));
        Assert.Null(await locator.LocateAsync("203.0.113.7"));
        Assert.Null(await locator.LocateAsync("203.0.113.7"));

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task ALookupCancelledByTheCaller_IsNotRememberedAsAFailure()
    {
        var handler = new CountingHandler(HttpStatusCode.OK, """{"status":"success","country":"Canada"}""");
        var locator = Create(handler);

        Assert.Null(await locator.LocateAsync("203.0.113.9", new CancellationToken(canceled: true)));
        var retried = await locator.LocateAsync("203.0.113.9");

        Assert.Equal("Canada", retried?.Country);
    }

    [Fact]
    public async Task ASuccessfulLookup_IsCached()
    {
        var handler = new CountingHandler(HttpStatusCode.OK, """{"status":"success","country":"Canada","regionName":"Ontario","city":"Toronto"}""");
        var locator = Create(handler);

        var first = await locator.LocateAsync("203.0.113.8");
        var second = await locator.LocateAsync("203.0.113.8");

        Assert.Equal("Canada", first?.Country);
        Assert.Equal("Toronto", second?.City);
        Assert.Equal(1, handler.Calls);
    }
}
