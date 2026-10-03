using Microsoft.Extensions.Logging;

namespace Tnzi.Payment.Subscriptions.Tests;

/// <summary>
/// 支付回流处理器不丢弃状态机的失败结果：状态机返回的是 <see cref="Result"/> 而不是异常，
/// 丢掉它 = 「钱到账了却找不到订阅」这类事件零痕迹。
/// </summary>
public class SubscriptionPaymentHandlerFailureTraceTests
{
    private static readonly string ExtraData = new SubscriptionBillingMetadata
    {
        Purpose = SubscriptionBillingPurpose.Renewal,
        SubscriptionId = Guid.NewGuid()
    }.ToExtraData();

    private static Mock<ISubscriptionService> ServiceReturningNotFound()
    {
        var service = new Mock<ISubscriptionService>();
        service.Setup(s => s.ApplyPaymentCompletedAsync(It.IsAny<SubscriptionPaymentContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(ErrorCodes.SubscriptionNotFound, 404));
        service.Setup(s => s.ApplyPaymentFailedAsync(It.IsAny<SubscriptionPaymentContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(ErrorCodes.SubscriptionNotFound, 404));
        return service;
    }

    [Fact]
    public async Task ACompletedPaymentThatCannotBeApplied_IsLoggedAsAnError()
    {
        var logger = new CapturingLogger<SubscriptionPaymentCompletedHandler>();
        var handler = new SubscriptionPaymentCompletedHandler(logger, ServiceReturningNotFound().Object);

        await handler.HandleAsync(new PaymentCompletedEvent
        {
            TradeNo = "T-1",
            BusinessOrderNo = "SUB-GONE",
            BusinessType = BusinessType.Subscription,
            Amount = 30m,
            Currency = "USD",
            ExtraData = ExtraData
        });

        var entry = logger.Entries.Where(e => e.Level == LogLevel.Error).ShouldHaveSingleItem();
        entry.Message.ShouldContain("T-1");
        entry.Message.ShouldContain(ErrorCodes.SubscriptionNotFound);
    }

    [Fact]
    public async Task AFailedPaymentThatCannotBeApplied_IsLoggedAsAWarning()
    {
        var logger = new CapturingLogger<SubscriptionPaymentFailedHandler>();
        var handler = new SubscriptionPaymentFailedHandler(logger, ServiceReturningNotFound().Object);

        await handler.HandleAsync(new PaymentFailedEvent
        {
            TradeNo = "T-2",
            BusinessOrderNo = "SUB-GONE",
            BusinessType = BusinessType.Subscription,
            ExtraData = ExtraData
        });

        logger.Entries.Where(e => e.Level == LogLevel.Warning).ShouldHaveSingleItem().Message.ShouldContain("T-2");
    }

    [Fact]
    public async Task AnExpiredPaymentThatCannotBeApplied_IsLoggedAsAWarning()
    {
        var logger = new CapturingLogger<SubscriptionPaymentExpiredHandler>();
        var handler = new SubscriptionPaymentExpiredHandler(logger, ServiceReturningNotFound().Object);

        await handler.HandleAsync(new PaymentExpiredEvent
        {
            TradeNo = "T-3",
            BusinessOrderNo = "SUB-GONE",
            BusinessType = BusinessType.Subscription,
            ExtraData = ExtraData
        });

        logger.Entries.Where(e => e.Level == LogLevel.Warning).ShouldHaveSingleItem().Message.ShouldContain("T-3");
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
