using System.Reflection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.SignalR.Hubs;
using Tnzi.SignalR.Tests.TestDoubles;

namespace Tnzi.SignalR.Tests.Filters;

/// <summary>
/// <see cref="HubInvocationServicesFilter"/>：把 Hub 激活作用域暴露给同一次调用里的
/// <see cref="TnziHub"/>，调用结束后收回（不能让连接持有一个已释放的作用域）。
/// </summary>
public class HubInvocationServicesFilterTests
{
    private sealed class ProbeHub : TnziHub
    {
        public void Ping() { }
    }

    private static readonly MethodInfo PingMethod = typeof(ProbeHub).GetMethod(nameof(ProbeHub.Ping))!;

    private static object? ExposedDuring(HubCallerContext context) =>
        context.Items.TryGetValue(HubInvocationServicesFilter.ItemKey, out var value) ? value : null;

    [Fact]
    public async Task OnConnected_ExposesTheActivationScope_ThenRemovesIt()
    {
        var filter = new HubInvocationServicesFilter();
        var context = new FakeHubCallerContext();
        var scope = new ServiceCollection().BuildServiceProvider();
        using var hub = new ProbeHub();
        object? seen = null;

        await filter.OnConnectedAsync(
            new HubLifetimeContext(context, scope, hub),
            _ => { seen = ExposedDuring(context); return Task.CompletedTask; });

        seen.ShouldBeSameAs(scope);
        context.Items.ShouldNotContainKey(HubInvocationServicesFilter.ItemKey);
    }

    [Fact]
    public async Task OnDisconnected_ExposesTheActivationScope_ThenRemovesIt()
    {
        var filter = new HubInvocationServicesFilter();
        var context = new FakeHubCallerContext();
        var scope = new ServiceCollection().BuildServiceProvider();
        using var hub = new ProbeHub();
        object? seen = null;

        await filter.OnDisconnectedAsync(
            new HubLifetimeContext(context, scope, hub),
            exception: null,
            (_, _) => { seen = ExposedDuring(context); return Task.CompletedTask; });

        seen.ShouldBeSameAs(scope);
        context.Items.ShouldNotContainKey(HubInvocationServicesFilter.ItemKey);
    }

    [Fact]
    public async Task InvokeMethod_ExposesTheActivationScope_ThenRemovesIt()
    {
        var filter = new HubInvocationServicesFilter();
        var context = new FakeHubCallerContext();
        var scope = new ServiceCollection().BuildServiceProvider();
        using var hub = new ProbeHub();
        object? seen = null;

        var result = await filter.InvokeMethodAsync(
            new HubInvocationContext(context, scope, hub, PingMethod, []),
            _ => { seen = ExposedDuring(context); return new ValueTask<object?>("ok"); });

        result.ShouldBe("ok");
        seen.ShouldBeSameAs(scope);
        context.Items.ShouldNotContainKey(HubInvocationServicesFilter.ItemKey);
    }

    /// <summary>
    /// 调用失败也要收回：否则连接会一直持有一个即将被释放的作用域。
    /// </summary>
    [Fact]
    public async Task RemovesTheScope_EvenWhenTheInvocationThrows()
    {
        var filter = new HubInvocationServicesFilter();
        var context = new FakeHubCallerContext();
        using var hub = new ProbeHub();

        await Should.ThrowAsync<InvalidOperationException>(() => filter.InvokeMethodAsync(
            new HubInvocationContext(context, new ServiceCollection().BuildServiceProvider(), hub, PingMethod, []),
            _ => throw new InvalidOperationException("boom")).AsTask());

        context.Items.ShouldNotContainKey(HubInvocationServicesFilter.ItemKey);
    }

    /// <summary>
    /// 严格嵌套的两次调用：内层结束后外层的作用域回到可见位置。
    /// </summary>
    [Fact]
    public async Task RestoresThePreviousScope_WhenInvocationsNest()
    {
        var filter = new HubInvocationServicesFilter();
        var context = new FakeHubCallerContext();
        var outer = new ServiceCollection().BuildServiceProvider();
        var inner = new ServiceCollection().BuildServiceProvider();
        using var hub = new ProbeHub();
        object? afterInner = null;

        await filter.InvokeMethodAsync(
            new HubInvocationContext(context, outer, hub, PingMethod, []),
            async _ =>
            {
                await filter.InvokeMethodAsync(
                    new HubInvocationContext(context, inner, hub, PingMethod, []),
                    _ => new ValueTask<object?>(result: null));
                afterInner = ExposedDuring(context);
                return null;
            });

        afterInner.ShouldBeSameAs(outer);
        context.Items.ShouldNotContainKey(HubInvocationServicesFilter.ItemKey);
    }

    /// <summary>
    /// ★ 并行调用（MaximumParallelInvocationsPerClient &gt; 1）真正的顺序是<b>交错</b>而不是嵌套：
    /// A 开始、B 开始、A 结束、B 结束。「恢复进入前的值」在这里是错的 ——
    /// A 结束时把键删掉（B 还在跑，它的 Hub 退回 HttpContext 回退），B 结束时又把 A 那个
    /// <b>已释放</b>的作用域写回去并一直留到下一次回调。可见的必须始终是还在跑的那一次。
    /// </summary>
    [Fact]
    public async Task KeepsTheStillRunningScopeVisible_WhenInvocationsInterleave()
    {
        var filter = new HubInvocationServicesFilter();
        var context = new FakeHubCallerContext();
        var scopeA = new ServiceCollection().BuildServiceProvider();
        var scopeB = new ServiceCollection().BuildServiceProvider();
        using var hub = new ProbeHub();

        var bStarted = new TaskCompletionSource();
        var aFinished = new TaskCompletionSource();
        object? seenByBAfterAFinished = null;

        var a = filter.InvokeMethodAsync(
            new HubInvocationContext(context, scopeA, hub, PingMethod, []),
            async _ => { await bStarted.Task; return null; }).AsTask();

        var b = filter.InvokeMethodAsync(
            new HubInvocationContext(context, scopeB, hub, PingMethod, []),
            async _ =>
            {
                bStarted.SetResult();
                await aFinished.Task;
                seenByBAfterAFinished = ExposedDuring(context);
                return null;
            }).AsTask();

        await a;
        aFinished.SetResult();
        await b;

        seenByBAfterAFinished.ShouldBeSameAs(scopeB);
        context.Items.ShouldNotContainKey(HubInvocationServicesFilter.ItemKey);
    }
}
