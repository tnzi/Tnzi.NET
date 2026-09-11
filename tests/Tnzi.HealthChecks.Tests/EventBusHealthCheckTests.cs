using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Tnzi.EventBus;
using Tnzi.HealthChecks.Checks;

namespace Tnzi.HealthChecks.Tests;

/// <summary>
/// 就绪探针到底知不知道消息中间件的状态。
/// </summary>
/// <remarks>
/// <para>
/// <b>被保护的缺陷</b>：这个检查此前注入 <c>IEventBus</c>，而 2026-07-07 总线分离之后
/// <c>IEventBus</c> <b>永远</b>是本地总线（<c>IsLocal</c> 恒 true）——
/// 于是它恒返回 Healthy，判断分布式总线的那条分支<b>不可达</b>。
/// 代理宕机时 <c>/health/ready</c> 照样报健康。
/// </para>
/// <para>
/// ★ 一个永远说"好"的就绪探针比没有探针更糟：编排器会继续把流量送进一个
/// 收不到消息的实例，而运维看到的仪表盘是全绿的。
/// </para>
/// </remarks>
public class EventBusHealthCheckTests
{
    [Fact]
    public async Task WithoutADistributedBus_ItIsHealthy()
    {
        var check = new EventBusHealthCheck(LocalBus(), []);

        var result = await check.CheckHealthAsync(Context());

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    /// <summary>
    /// 有分布式总线但没有传输探针：如实报 Degraded，因为连通性确实无法验证。
    /// </summary>
    [Fact]
    public async Task WithADistributedBusButNoProbe_ItIsDegraded()
    {
        var check = new EventBusHealthCheck(LocalBus(), [], DistributedBus());

        var result = await check.CheckHealthAsync(Context());

        result.Status.ShouldBe(HealthStatus.Degraded);
    }

    /// <summary>
    /// ★ 代理不可达必须让就绪探针失败。
    /// </summary>
    /// <remarks>
    /// 这是修复前<b>做不到</b>的那一条：无论代理死活，结果都是 Healthy。
    /// 选 Unhealthy 而不是 Degraded —— 只有 Unhealthy 会让 <c>/health/ready</c> 返回 503，
    /// 也就是把这个实例从轮转里摘出去；Degraded 仍然是 200，等于什么都没发生。
    /// </remarks>
    [Fact]
    public async Task WhenTheBrokerIsUnreachable_ItIsUnhealthy()
    {
        var probe = new StubProbe(new DistributedEventBusHealth(false, "the AMQP connection is closed"));
        var check = new EventBusHealthCheck(LocalBus(), [probe], DistributedBus());

        var result = await check.CheckHealthAsync(Context());

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Description.ShouldNotBeNull().ShouldContain("RabbitMQ");
    }

    [Fact]
    public async Task WhenTheBrokerIsReachable_ItIsHealthy()
    {
        var probe = new StubProbe(new DistributedEventBusHealth(true));
        var check = new EventBusHealthCheck(LocalBus(), [probe], DistributedBus());

        var result = await check.CheckHealthAsync(Context());

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    /// <summary>
    /// 探针自己抛异常也算不可达 —— 「证明不了它可用」与「它不可用」对调用方是同一件事。
    /// </summary>
    [Fact]
    public async Task WhenTheProbeItselfThrows_ItIsUnhealthy()
    {
        var probe = new ThrowingProbe();
        var check = new EventBusHealthCheck(LocalBus(), [probe], DistributedBus());

        var result = await check.CheckHealthAsync(Context());

        result.Status.ShouldBe(HealthStatus.Unhealthy);
    }

    /// <summary>
    /// 诊断数据里不得出现异常消息原文（可能带连接串），只给类型名。
    /// </summary>
    [Fact]
    public async Task TheProbeFailureData_DoesNotCarryTheExceptionMessage()
    {
        var check = new EventBusHealthCheck(LocalBus(), [new ThrowingProbe()], DistributedBus());

        var result = await check.CheckHealthAsync(Context());

        result.Description.ShouldNotBeNull().ShouldNotContain(ThrowingProbe.SecretBearingMessage);
    }

    /// <summary>
    /// 只注册了本地总线时也要能被容器造出来。
    /// </summary>
    /// <remarks>
    /// ★ 健康检查是经 <c>ActivatorUtilities</c> 实例化的，而分布式总线与探针都是**可选**依赖。
    /// 少了这条断言，"没加载 RabbitMQ 的应用一打开 /health 就 500" 这种失效只会在真实进程里出现 ——
    /// 上面每一条测试都直接 new，看不见构造这一步。
    /// </remarks>
    [Fact]
    public void ItCanBeConstructedByTheContainerWithOnlyALocalBusRegistered()
    {
        var services = new ServiceCollection();
        services.AddSingleton(LocalBus());
        using var provider = services.BuildServiceProvider();

        var check = ActivatorUtilities.CreateInstance<EventBusHealthCheck>(provider);

        check.ShouldNotBeNull();
    }

    private static HealthCheckContext Context() => new();

    private static IEventBus LocalBus()
    {
        var mock = new Mock<IEventBus>();
        mock.Setup(b => b.IsLocal).Returns(true);
        return mock.Object;
    }

    private static IDistributedEventBus DistributedBus()
    {
        var mock = new Mock<IDistributedEventBus>();
        mock.Setup(b => b.IsLocal).Returns(false);
        return mock.Object;
    }

    private sealed class StubProbe(DistributedEventBusHealth health) : IDistributedEventBusHealthProbe
    {
        public string TransportName => "RabbitMQ";

        public Task<DistributedEventBusHealth> CheckAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(health);
    }

    private sealed class ThrowingProbe : IDistributedEventBusHealthProbe
    {
        public const string SecretBearingMessage = "amqp://user:hunter2@broker:5672 refused the connection";

        public string TransportName => "RabbitMQ";

        public Task<DistributedEventBusHealth> CheckAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(SecretBearingMessage);
    }
}
