using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tnzi.Imaging.Tests;

/// <summary>
/// <see cref="VerifyCodeService"/> 的一次性必须是原子的。
/// </summary>
/// <remarks>
/// 被保护的缺陷：先读、再删、后比较三步之间没有原子性。对同一个 id 并发放进 N 个不同的猜测，
/// 在删除之前读到验证码的请求都会完成比较，4 位数字验证码的命中率从 1/10000 变成 N/10000。
/// 用真实的内存缓存跑，读侧用屏障把 N 个请求卡在「都读到了」之后再放行：没有屏障，
/// 内存缓存的同步完成会让并发调用在测试里天然串行化，测不到这个窗口。
/// </remarks>
public class VerifyCodeOneTimeTests
{
    [Fact]
    public async Task ConcurrentVerifies_OfOneCode_SucceedAtMostOnce()
    {
        const int attempts = 20;
        var (service, cache) = CreateService(readBarrier: attempts);
        var gate = (BarrierCache)(object)cache;
        gate.Enabled = false;
        var generated = await service.GenerateAsync("login-1", 4, VerifyCodeType.Number);
        gate.Enabled = true;

        var results = await RunConcurrentlyAsync(attempts, () => service.VerifyAsync("login-1", generated.Code));

        results.Count(ok => ok).ShouldBe(1);
    }

    [Fact]
    public async Task ARegeneratedCode_UnderTheSameId_CanBeVerified()
    {
        // 调用方可以指定 id（例如按手机号或会话复用同一个 id）。上一次验证留下的「已消费」标记
        // 不能挡住同一个 id 上新签的验证码。
        var (service, _) = CreateService();

        var first = await service.GenerateAsync("reused-id", 4, VerifyCodeType.Number);
        (await service.VerifyAsync("reused-id", first.Code)).ShouldBeTrue();

        var second = await service.GenerateAsync("reused-id", 4, VerifyCodeType.Number);
        (await service.VerifyAsync("reused-id", second.Code)).ShouldBeTrue();
    }

    [Fact]
    public async Task AVerifiedCode_CannotBeUsedTwice()
    {
        var (service, _) = CreateService();

        var generated = await service.GenerateAsync("once", 4, VerifyCodeType.Number);

        (await service.VerifyAsync("once", generated.Code)).ShouldBeTrue();
        (await service.VerifyAsync("once", generated.Code)).ShouldBeFalse();
    }

    private static (VerifyCodeService Service, ICache Cache) CreateService(int readBarrier = 0)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var cache = new MemoryCacheService(
            new MemoryCache(new MemoryCacheOptions()),
            Mock.Of<ILogger<MemoryCacheService>>(),
            Microsoft.Extensions.Options.Options.Create(new CachingOptions()),
            provider);
        ICache effective = readBarrier > 0 ? BarrierCache.Wrap(cache, readBarrier, "VerifyCode:") : cache;

        return (new VerifyCodeService(new ValidateCoder(), effective), effective);
    }

    private static async Task<T[]> RunConcurrentlyAsync<T>(int count, Func<Task<T>> attempt)
    {
        // 真线程：屏障是同步阻塞的，靠线程池慢慢扩容会让测试变慢甚至超时。
        var tasks = Enumerable.Range(0, count)
            .Select(_ => Task.Factory.StartNew(attempt, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap())
            .ToArray();
        return await Task.WhenAll(tasks);
    }
}
