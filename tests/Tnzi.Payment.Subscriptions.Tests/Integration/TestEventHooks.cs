using System.Collections.Concurrent;
using Tnzi.EventBus;

namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 在一次后台扫描的中途插进一个并发动作：扫描处理完第一条订阅时发出的事件触发登记好的动作，
/// 动作在它自己的 scope 里改第二条订阅，扫描随后才轮到第二条。用来确定性地复现「扫描读到的快照已经过时」。
/// </summary>
internal sealed class TestEventHooks
{
    private readonly ConcurrentDictionary<string, Func<Task>> _onPaymentCompleted = new();
    private readonly ConcurrentDictionary<string, Func<Task>> _onSubscriptionExpired = new();

    /// <summary>业务单号为 <paramref name="businessOrderNo"/> 的支付完成时运行一次。</summary>
    public void OnPaymentCompleted(string businessOrderNo, Func<Task> action) => _onPaymentCompleted[businessOrderNo] = action;

    /// <summary>订阅 <paramref name="subscriptionNo"/> 过期时运行一次。</summary>
    public void OnSubscriptionExpired(string subscriptionNo, Func<Task> action) => _onSubscriptionExpired[subscriptionNo] = action;

    public Task FirePaymentCompletedAsync(string businessOrderNo)
        => _onPaymentCompleted.TryRemove(businessOrderNo, out var action) ? action() : Task.CompletedTask;

    public Task FireSubscriptionExpiredAsync(string subscriptionNo)
        => _onSubscriptionExpired.TryRemove(subscriptionNo, out var action) ? action() : Task.CompletedTask;

    /// <summary>把两个钩子挂到事件总线上。</summary>
    public void Register(IServiceCollection services)
    {
        services.AddSingleton(this);
        services.AddScoped<IEventHandler<PaymentCompletedEvent>, PaymentCompletedHook>();
        services.AddScoped<IEventHandler<SubscriptionExpiredEvent>, SubscriptionExpiredHook>();
    }

    private sealed class PaymentCompletedHook(TestEventHooks hooks) : IEventHandler<PaymentCompletedEvent>
    {
        public Task HandleAsync(PaymentCompletedEvent eventData, CancellationToken cancellationToken = default)
            => hooks.FirePaymentCompletedAsync(eventData.BusinessOrderNo);
    }

    private sealed class SubscriptionExpiredHook(TestEventHooks hooks) : IEventHandler<SubscriptionExpiredEvent>
    {
        public Task HandleAsync(SubscriptionExpiredEvent eventData, CancellationToken cancellationToken = default)
            => hooks.FireSubscriptionExpiredAsync(eventData.SubscriptionNo);
    }
}
