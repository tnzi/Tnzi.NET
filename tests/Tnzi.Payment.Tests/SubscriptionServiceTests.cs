using System.Linq.Expressions;
using Mapster;
using MapsterMapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Tnzi.Domain.Repositories;
using Tnzi.Mapster;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Entities;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Options;
using Tnzi.Payment.Providers;
using Tnzi.Payment.Services;
using Tnzi.Results;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Tests;

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

        // 设置 IServiceProvider mock
        var serviceProviderMock = new Mock<IServiceProvider>();
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactoryMock.Object);

        _service = new SubscriptionService(
            _subscriptionRepositoryMock.Object,
            _planRepositoryMock.Object,
            _changeRepositoryMock.Object,
            _paymentServiceMock.Object,
            _providerFactoryMock.Object,
            _optionsMock.Object,
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

    [Fact]
    public async Task CancelSubscriptionAsync_ImmediateCancel_SetsCancelledStatus()
    {
        // Arrange
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            SubscriptionNo = "SUB001",
            Status = SubscriptionStatus.Active,
            UserId = Guid.NewGuid()
        };

        _subscriptionRepositoryMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<Subscription, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);
        _subscriptionRepositoryMock.Setup(r => r.UpdateAsync(It.IsAny<Subscription>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new CancelSubscriptionDto { Reason = "No longer needed", Immediate = true };

        // Act
        var result = await _service.CancelSubscriptionAsync(subscription.Id, request);

        // Assert
        result.Succeeded.ShouldBeTrue();
        subscription.Status.ShouldBe(SubscriptionStatus.Cancelled);
        subscription.CancelReason.ShouldBe("No longer needed");
        subscription.EndTime.ShouldNotBeNull();
    }

    [Fact]
    public async Task CancelSubscriptionAsync_DeferredCancel_SetsPendingRenewalStatus()
    {
        // Arrange
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            SubscriptionNo = "SUB002",
            Status = SubscriptionStatus.Active,
            AutoRenew = true,
            UserId = Guid.NewGuid()
        };

        _subscriptionRepositoryMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<Subscription, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);
        _subscriptionRepositoryMock.Setup(r => r.UpdateAsync(It.IsAny<Subscription>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new CancelSubscriptionDto { Reason = "Switching plan", Immediate = false };

        // Act
        var result = await _service.CancelSubscriptionAsync(subscription.Id, request);

        // Assert
        result.Succeeded.ShouldBeTrue();
        subscription.Status.ShouldBe(SubscriptionStatus.PendingRenewal);
        subscription.AutoRenew.ShouldBeFalse();
    }

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
            UserId = Guid.NewGuid()
        };
        SetupSubscription(subscription);

        // Act
        var result = await _service.ApplyPaymentCompletedAsync(new SubscriptionPaymentContext
        {
            Purpose = SubscriptionBillingPurpose.Initial,
            SubscriptionId = subscription.Id,
            SubscriptionNo = subscription.SubscriptionNo,
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
            SubscriptionNo = subscription.SubscriptionNo
        });

        // Assert
        result.Succeeded.ShouldBeTrue();
        subscription.Status.ShouldBe(SubscriptionStatus.Pending);
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
