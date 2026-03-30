using System.Linq.Expressions;
using Mapster;
using MapsterMapper;
using Microsoft.Extensions.Logging;
using Moq;
using Tnzi.Domain.Repositories;
using Tnzi.Mapster;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Entities;
using Tnzi.Payment.Metadata;
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
}
