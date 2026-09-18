using System.Linq.Expressions;
using Mapster;
using MapsterMapper;
using Microsoft.Extensions.Logging;
using Tnzi.Mapster;

namespace Tnzi.Payment.Subscriptions.Tests;

/// <summary>
/// SubscriptionService 单元测试
/// </summary>
public class SubscriptionServiceTests
{
    private readonly Mock<IRepository<Subscription, Guid>> _subscriptionRepositoryMock;
    private readonly Mock<IRepository<SubscriptionPlan, Guid>> _planRepositoryMock;
    private readonly Mock<IRepository<SubscriptionChange, Guid>> _changeRepositoryMock;
    private readonly Mock<IPaymentService> _paymentServiceMock;
    private readonly Mock<IPaymentProviderFactory> _providerFactoryMock;
    private readonly Mock<IOptionsMonitor<PaymentOptions>> _optionsMock;
    // 拆分后订阅配置是独立一节（Payment:Subscription），由本模块自己绑，因此单独注入。
    private readonly Mock<IOptionsMonitor<SubscriptionOptions>> _subscriptionOptionsMock;
    private readonly Mock<IPaymentMethodService> _paymentMethodServiceMock;
    private readonly SubscriptionService _service;

    public SubscriptionServiceTests()
    {
        // Initialize Mapster
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        _subscriptionRepositoryMock = new Mock<IRepository<Subscription, Guid>>();
        _planRepositoryMock = new Mock<IRepository<SubscriptionPlan, Guid>>();
        _changeRepositoryMock = new Mock<IRepository<SubscriptionChange, Guid>>();
        _paymentServiceMock = new Mock<IPaymentService>();
        _providerFactoryMock = new Mock<IPaymentProviderFactory>();
        _optionsMock = new Mock<IOptionsMonitor<PaymentOptions>>();
        _optionsMock.Setup(x => x.CurrentValue).Returns(new PaymentOptions());
        _subscriptionOptionsMock = new Mock<IOptionsMonitor<SubscriptionOptions>>();
        _subscriptionOptionsMock.Setup(x => x.CurrentValue).Returns(new SubscriptionOptions());

        // 设置 IServiceProvider mock
        var serviceProviderMock = new Mock<IServiceProvider>();
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactoryMock.Object);

        _paymentMethodServiceMock = new Mock<IPaymentMethodService>();

        _service = new SubscriptionService(
            _subscriptionRepositoryMock.Object,
            _planRepositoryMock.Object,
            _changeRepositoryMock.Object,
            _paymentServiceMock.Object,
            _providerFactoryMock.Object,
            _paymentMethodServiceMock.Object,
            _optionsMock.Object,
            _subscriptionOptionsMock.Object,
            serviceProviderMock.Object
        );
    }

    #region CancelSubscriptionAsync Tests

    [Fact]
    public async Task CancelSubscriptionAsync_WithNonExistingSubscription_Returns404()
    {
        // Arrange
        _subscriptionRepositoryMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<Subscription, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Subscription?)null);

        var request = new CancelSubscriptionDto { Reason = "Test", Immediate = true };

        // Act
        var result = await _service.CancelSubscriptionAsync(Guid.NewGuid(), request);

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task CancelSubscriptionAsync_WithAlreadyCancelled_ReturnsFailure()
    {
        // Arrange
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            SubscriptionNo = "SUB001",
            Status = SubscriptionStatus.Cancelled,
            UserId = Guid.NewGuid()
        };

        _subscriptionRepositoryMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<Subscription, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);

        var request = new CancelSubscriptionDto { Reason = "Test", Immediate = true };

        // Act
        var result = await _service.CancelSubscriptionAsync(subscription.Id, request);

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe(ErrorCodes.SubscriptionAlreadyCancelledOrExpired);
    }

    // 取消的两条成功路径改用条件更新（CAS）抢计费锁，需要真实 EF Provider，
    // 因此覆盖移到集成测试 SubscriptionCancellationIntegrationTests。
    // 这里保留的是在抢锁之前就该拒绝的分支（不存在 / 已取消或已过期）。

    #endregion

    #region GetSubscriptionAsync Tests

    [Fact]
    public async Task GetSubscriptionAsync_WithExistingSubscription_ReturnsSuccess()
    {
        // Arrange
        var subscriptionId = Guid.NewGuid();
        var subscription = new Subscription
        {
            Id = subscriptionId,
            SubscriptionNo = "SUB001",
            UserId = Guid.NewGuid(),
            Status = SubscriptionStatus.Active,
            Currency = "USD"
        };

        _subscriptionRepositoryMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<Subscription, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);

        // Act
        var result = await _service.GetSubscriptionAsync(subscriptionId);

        // Assert
        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task GetSubscriptionAsync_WithNonExisting_Returns404()
    {
        // Arrange
        _subscriptionRepositoryMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<Subscription, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Subscription?)null);

        // Act
        var result = await _service.GetSubscriptionAsync(Guid.NewGuid());

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    #endregion

    #region ApplyPaymentCompletedAsync (订阅状态机回流) Tests

    private void SetupSubscription(Subscription subscription)
    {
        _subscriptionRepositoryMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<Subscription, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);
        _subscriptionRepositoryMock.Setup(r => r.UpdateAsync(It.IsAny<Subscription>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        // 续费 / 试用转正成功会去找仍在等补差款的变更；这里的订阅没有
        _changeRepositoryMock.Setup(r => r.ToListAsync(
                It.IsAny<Expression<Func<SubscriptionChange, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    [Fact]
    public async Task ApplyPaymentCompletedAsync_Initial_ActivatesPendingSubscription()
    {
        // Arrange
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            SubscriptionNo = "SUB-INIT",
            Status = SubscriptionStatus.Pending,
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            OriginalPrice = 50m,
            UserId = Guid.NewGuid()
        };
        SetupSubscription(subscription);

        // Act
        var result = await _service.ApplyPaymentCompletedAsync(new SubscriptionPaymentContext
        {
            Purpose = SubscriptionBillingPurpose.Initial,
            SubscriptionId = subscription.Id,
            SubscriptionNo = subscription.SubscriptionNo,
            PayerUserId = subscription.UserId,
            Amount = 50m
        });

        // Assert
        result.Succeeded.ShouldBeTrue();
        subscription.Status.ShouldBe(SubscriptionStatus.Active);
        subscription.NextBillingTime.ShouldNotBeNull();
        subscription.PaidAmount.ShouldBe(50m);
    }

    [Fact]
    public async Task ApplyPaymentCompletedAsync_Renewal_AdvancesPeriodAndClearsDunning()
    {
        // Arrange
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            SubscriptionNo = "SUB-RENEW",
            Status = SubscriptionStatus.PastDue,
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            OriginalPrice = 30m,
            NextBillingTime = DateTime.UtcNow.AddDays(-2),
            RenewalRetryCount = 2,
            PastDueSince = DateTime.UtcNow.AddDays(-2),
            UserId = Guid.NewGuid()
        };
        SetupSubscription(subscription);

        // Act
        var result = await _service.ApplyPaymentCompletedAsync(new SubscriptionPaymentContext
        {
            Purpose = SubscriptionBillingPurpose.Renewal,
            SubscriptionId = subscription.Id,
            SubscriptionNo = subscription.SubscriptionNo,
            PayerUserId = subscription.UserId,
            Amount = 30m
        });

        // Assert
        result.Succeeded.ShouldBeTrue();
        subscription.Status.ShouldBe(SubscriptionStatus.Active);
        subscription.NextBillingTime!.Value.ShouldBeGreaterThan(DateTime.UtcNow);
        subscription.RenewalRetryCount.ShouldBe(0);
        subscription.PastDueSince.ShouldBeNull();
    }

    [Fact]
    public async Task ApplyPaymentCompletedAsync_TrialConversion_ConvertsToActive()
    {
        // Arrange
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            SubscriptionNo = "SUB-TRIAL",
            Status = SubscriptionStatus.Trial,
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            OriginalPrice = 20m,
            TrialEndTime = DateTime.UtcNow.AddDays(-1),
            UserId = Guid.NewGuid()
        };
        SetupSubscription(subscription);

        // Act
        var result = await _service.ApplyPaymentCompletedAsync(new SubscriptionPaymentContext
        {
            Purpose = SubscriptionBillingPurpose.TrialConversion,
            SubscriptionId = subscription.Id,
            SubscriptionNo = subscription.SubscriptionNo,
            PayerUserId = subscription.UserId,
            Amount = 20m
        });

        // Assert
        result.Succeeded.ShouldBeTrue();
        subscription.Status.ShouldBe(SubscriptionStatus.Active);
        subscription.TrialConvertedTime.ShouldNotBeNull();
        subscription.NextBillingTime.ShouldNotBeNull();
    }

    [Fact]
    public async Task ApplyPaymentCompletedAsync_Renewal_OnCancelledSubscription_DoesNotResurrect()
    {
        // Arrange：订阅已取消（取消与在途扣款竞态）
        var nextBilling = DateTime.UtcNow.AddDays(-1);
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            SubscriptionNo = "SUB-CANCELLED",
            Status = SubscriptionStatus.Cancelled,
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            NextBillingTime = nextBilling,
            UserId = Guid.NewGuid()
        };
        SetupSubscription(subscription);

        // Act：续费支付完成回流
        var result = await _service.ApplyPaymentCompletedAsync(new SubscriptionPaymentContext
        {
            Purpose = SubscriptionBillingPurpose.Renewal,
            SubscriptionId = subscription.Id,
            SubscriptionNo = subscription.SubscriptionNo,
            PayerUserId = subscription.UserId,
            PaymentTradeNo = "PAY-ORPHAN",
            Amount = 30m
        });

        // Assert：不被复活，状态与周期保持不变
        result.Succeeded.ShouldBeTrue();
        subscription.Status.ShouldBe(SubscriptionStatus.Cancelled);
        subscription.NextBillingTime.ShouldBe(nextBilling);
    }

    [Fact]
    public async Task ApplyPaymentCompletedAsync_DuplicateTradeNo_IsIdempotent()
    {
        // Arrange：同一支付已应用过一次
        var nextBilling = DateTime.UtcNow.AddDays(20);
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            SubscriptionNo = "SUB-DUP",
            Status = SubscriptionStatus.Active,
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            NextBillingTime = nextBilling,
            LastBillingTradeNo = "PAY-DUP",
            UserId = Guid.NewGuid()
        };
        SetupSubscription(subscription);

        // Act：同一 TradeNo 再次投递
        var result = await _service.ApplyPaymentCompletedAsync(new SubscriptionPaymentContext
        {
            Purpose = SubscriptionBillingPurpose.Renewal,
            SubscriptionId = subscription.Id,
            SubscriptionNo = subscription.SubscriptionNo,
            PayerUserId = subscription.UserId,
            PaymentTradeNo = "PAY-DUP",
            Amount = 30m
        });

        // Assert：幂等，周期不再次推进
        result.Succeeded.ShouldBeTrue();
        subscription.NextBillingTime.ShouldBe(nextBilling);
    }

    [Fact]
    public async Task ApplyPaymentCompletedAsync_NonExistentSubscription_Returns404()
    {
        // Arrange
        _subscriptionRepositoryMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<Subscription, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Subscription?)null);

        // Act
        var result = await _service.ApplyPaymentCompletedAsync(new SubscriptionPaymentContext
        {
            Purpose = SubscriptionBillingPurpose.Renewal,
            SubscriptionId = Guid.NewGuid(),
            SubscriptionNo = "NOPE"
        });

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task ApplyPaymentFailedAsync_Renewal_SetsPastDueAndIncrementsRetry()
    {
        // Arrange
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            SubscriptionNo = "SUB-FAIL",
            Status = SubscriptionStatus.Active,
            RenewalRetryCount = 0,
            UserId = Guid.NewGuid()
        };
        SetupSubscription(subscription);

        // Act
        var result = await _service.ApplyPaymentFailedAsync(new SubscriptionPaymentContext
        {
            Purpose = SubscriptionBillingPurpose.Renewal,
            SubscriptionId = subscription.Id,
            SubscriptionNo = subscription.SubscriptionNo,
            PayerUserId = subscription.UserId,
            FailReason = "card_declined"
        });

        // Assert
        result.Succeeded.ShouldBeTrue();
        subscription.Status.ShouldBe(SubscriptionStatus.PastDue);
        subscription.RenewalRetryCount.ShouldBe(1);
        subscription.PastDueSince.ShouldNotBeNull();
    }

    [Fact]
    public async Task ApplyPaymentFailedAsync_Initial_KeepsPending()
    {
        // Arrange
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            SubscriptionNo = "SUB-INITFAIL",
            Status = SubscriptionStatus.Pending,
            UserId = Guid.NewGuid()
        };
        SetupSubscription(subscription);

        // Act
        var result = await _service.ApplyPaymentFailedAsync(new SubscriptionPaymentContext
        {
            Purpose = SubscriptionBillingPurpose.Initial,
            SubscriptionId = subscription.Id,
            SubscriptionNo = subscription.SubscriptionNo,
            PayerUserId = subscription.UserId
        });

        // Assert
        result.Succeeded.ShouldBeTrue();
        subscription.Status.ShouldBe(SubscriptionStatus.Pending);
    }

    // ───────── 元数据只是路由键：付款人与金额都要核 ─────────

    /// <summary>
    /// 别人的支付（哪怕元数据完整）不得推进这条订阅：否则任何人拿自己的 0.5 元支付单
    /// 加一段自填的 ExtraData 就能激活 / 续期任意价位的订阅。
    /// </summary>
    [Fact]
    public async Task ApplyPaymentCompletedAsync_FromAnotherUser_IsRejectedAndLeavesTheSubscriptionAlone()
    {
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            SubscriptionNo = "SUB-STRANGER",
            Status = SubscriptionStatus.Pending,
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            OriginalPrice = 99m,
            UserId = Guid.NewGuid()
        };
        SetupSubscription(subscription);

        var result = await _service.ApplyPaymentCompletedAsync(new SubscriptionPaymentContext
        {
            Purpose = SubscriptionBillingPurpose.Initial,
            SubscriptionId = subscription.Id,
            SubscriptionNo = subscription.SubscriptionNo,
            PayerUserId = Guid.NewGuid(),
            Amount = 99m
        });

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe(ErrorCodes.SubscriptionPaymentOwnerMismatch);
        subscription.Status.ShouldBe(SubscriptionStatus.Pending);
        subscription.PaidAmount.ShouldBe(0m);
        _subscriptionRepositoryMock.Verify(r => r.UpdateAsync(It.IsAny<Subscription>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>付款人未知（null）同样拒绝：失败方向关闭。</summary>
    [Fact]
    public async Task ApplyPaymentCompletedAsync_WithoutAPayer_IsRejected()
    {
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            SubscriptionNo = "SUB-NOPAYER",
            Status = SubscriptionStatus.Pending,
            OriginalPrice = 99m,
            UserId = Guid.NewGuid()
        };
        SetupSubscription(subscription);

        var result = await _service.ApplyPaymentCompletedAsync(new SubscriptionPaymentContext
        {
            Purpose = SubscriptionBillingPurpose.Initial,
            SubscriptionId = subscription.Id,
            SubscriptionNo = subscription.SubscriptionNo,
            Amount = 99m
        });

        result.Succeeded.ShouldBeFalse();
        subscription.Status.ShouldBe(SubscriptionStatus.Pending);
    }

    /// <summary>付款人对了但钱不够（0.5 元买 99 元的计划）：不激活。</summary>
    [Fact]
    public async Task ApplyPaymentCompletedAsync_BelowThePlanPrice_IsRejected()
    {
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            SubscriptionNo = "SUB-CHEAP",
            Status = SubscriptionStatus.Pending,
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            OriginalPrice = 99m,
            Currency = "USD",
            UserId = Guid.NewGuid()
        };
        SetupSubscription(subscription);

        var result = await _service.ApplyPaymentCompletedAsync(new SubscriptionPaymentContext
        {
            Purpose = SubscriptionBillingPurpose.Initial,
            SubscriptionId = subscription.Id,
            SubscriptionNo = subscription.SubscriptionNo,
            PayerUserId = subscription.UserId,
            Amount = 0.5m,
            Currency = "USD"
        });

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe(ErrorCodes.SubscriptionPaymentAmountTooLow);
        subscription.Status.ShouldBe(SubscriptionStatus.Pending);
    }

    /// <summary>首付按「标价 − 券折扣」收，下界要把折扣算进去，否则合法的折扣首付会被拒。</summary>
    [Fact]
    public async Task ApplyPaymentCompletedAsync_Initial_AcceptsTheDiscountedPrice()
    {
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            SubscriptionNo = "SUB-DISC",
            Status = SubscriptionStatus.Pending,
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            OriginalPrice = 99m,
            DiscountAmount = 20m,
            Currency = "USD",
            UserId = Guid.NewGuid()
        };
        SetupSubscription(subscription);

        var result = await _service.ApplyPaymentCompletedAsync(new SubscriptionPaymentContext
        {
            Purpose = SubscriptionBillingPurpose.Initial,
            SubscriptionId = subscription.Id,
            SubscriptionNo = subscription.SubscriptionNo,
            PayerUserId = subscription.UserId,
            Amount = 79m,
            Currency = "USD"
        });

        result.Succeeded.ShouldBeTrue();
        subscription.Status.ShouldBe(SubscriptionStatus.Active);
    }

    /// <summary>99 JPY 不是 99 USD：币种对不上一律拒绝。</summary>
    [Fact]
    public async Task ApplyPaymentCompletedAsync_InAnotherCurrency_IsRejected()
    {
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            SubscriptionNo = "SUB-JPY",
            Status = SubscriptionStatus.Pending,
            OriginalPrice = 99m,
            Currency = "USD",
            UserId = Guid.NewGuid()
        };
        SetupSubscription(subscription);

        var result = await _service.ApplyPaymentCompletedAsync(new SubscriptionPaymentContext
        {
            Purpose = SubscriptionBillingPurpose.Initial,
            SubscriptionId = subscription.Id,
            SubscriptionNo = subscription.SubscriptionNo,
            PayerUserId = subscription.UserId,
            Amount = 99m,
            Currency = "JPY"
        });

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe(ErrorCodes.SubscriptionPaymentAmountTooLow);
    }

    /// <summary>
    /// 拿受害者的订阅号建一张不付的单，等它过期：失败回流不得把别人的订阅打成 PastDue。
    /// </summary>
    [Fact]
    public async Task ApplyPaymentFailedAsync_FromAStranger_DoesNotPushTheSubscriptionPastDue()
    {
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            SubscriptionNo = "SUB-VICTIM",
            Status = SubscriptionStatus.Active,
            UserId = Guid.NewGuid()
        };
        SetupSubscription(subscription);

        var result = await _service.ApplyPaymentFailedAsync(new SubscriptionPaymentContext
        {
            Purpose = SubscriptionBillingPurpose.Renewal,
            SubscriptionId = subscription.Id,
            SubscriptionNo = subscription.SubscriptionNo,
            PayerUserId = Guid.NewGuid(),
            FailReason = "Payment order expired"
        });

        result.Succeeded.ShouldBeFalse();
        subscription.Status.ShouldBe(SubscriptionStatus.Active);
        subscription.RenewalRetryCount.ShouldBe(0);
    }

    #endregion

    #region SubscriptionBillingMetadata Tests

    [Fact]
    public void SubscriptionBillingMetadata_RoundTrips_ThroughExtraData()
    {
        // Arrange
        var original = new SubscriptionBillingMetadata
        {
            Purpose = SubscriptionBillingPurpose.Proration,
            SubscriptionId = Guid.NewGuid(),
            ChangeId = Guid.NewGuid()
        };

        // Act
        var json = original.ToExtraData();
        var parsed = SubscriptionBillingMetadata.TryParse(json);

        // Assert
        parsed.ShouldNotBeNull();
        parsed!.Purpose.ShouldBe(SubscriptionBillingPurpose.Proration);
        parsed.SubscriptionId.ShouldBe(original.SubscriptionId);
        parsed.ChangeId.ShouldBe(original.ChangeId);
    }

    [Fact]
    public void SubscriptionBillingMetadata_TryParse_ReturnsNullForUserExtraData()
    {
        // 非订阅计费的普通 ExtraData 不应被误判为计费元数据
        SubscriptionBillingMetadata.TryParse("{\"foo\":\"bar\"}").ShouldBeNull();
        SubscriptionBillingMetadata.TryParse(null).ShouldBeNull();
        SubscriptionBillingMetadata.TryParse("not json").ShouldBeNull();
    }

    #endregion
}
